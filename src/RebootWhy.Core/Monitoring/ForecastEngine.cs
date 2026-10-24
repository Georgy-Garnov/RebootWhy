using System.Threading.Channels;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Monitoring;

/// <summary>
/// Recalculates the forecast periodically and immediately when USO writes to its log. Reports changes only.
/// Shared by the service and by the tray app when the service is unavailable.
/// </summary>
public sealed class ForecastEngine(
    Func<RebootForecast> analyze,
    TimeSpan period,
    TimeSpan debounce,
    string? watchDirectory)
{
    private readonly Channel<bool> _triggers = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Lock _waitersLock = new();
    private List<TaskCompletionSource<RebootForecast>> _waiters = [];
    private string? _latestKey;

    /// <summary>The forecast changed (or was received for the first time). Raised on a background thread.</summary>
    public event Action<RebootForecast>? Changed;

    /// <summary>
    /// Every recalculation, even without changes: the program needs a fresh check time,
    /// otherwise it looks as if nothing has been checked for a long time.
    /// </summary>
    public event Action<RebootForecast>? Updated;

    public RebootForecast? Latest { get; private set; }

    /// <summary>Recalculate immediately (true) or after a pause to coalesce a burst of log changes (false).</summary>
    public void Trigger(bool immediate = true) => _triggers.Writer.TryWrite(immediate);

    /// <summary>A check requested by the user: the result of a fresh recalculation, even if it equals the previous one.</summary>
    public Task<RebootForecast> RecalculateNowAsync()
    {
        var waiter = new TaskCompletionSource<RebootForecast>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waitersLock)
        {
            _waiters.Add(waiter);
        }

        Trigger();
        return waiter.Task;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var watcher = CreateWatcher();
        Trigger();
        while (!cancellationToken.IsCancellationRequested)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(period);
            try
            {
                var immediate = await _triggers.Reader.ReadAsync(wait.Token);
                if (!immediate)
                {
                    await Task.Delay(debounce, cancellationToken);
                    _triggers.Reader.TryRead(out _);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Periodic recalculation: even without new records, "Planned" turns into "Within the hour" over time.
            }

            await Task.Run(Recalculate, cancellationToken);
        }
    }

    private void Recalculate()
    {
        // Waiters receive the result of a recalculation started after their request.
        List<TaskCompletionSource<RebootForecast>> waiters;
        lock (_waitersLock)
        {
            waiters = _waiters;
            _waiters = [];
        }

        RebootForecast forecast;
        try
        {
            forecast = analyze();
        }
        catch (Exception ex)
        {
            // The analysis relies on an undocumented log: show any failure to the user instead of stopping the monitoring.
            forecast = new RebootForecast
            {
                State = ForecastState.None,
                AnalyzedAt = DateTime.Now,
                Problems = [new ForecastProblem(ForecastProblemKind.AnalysisFailed, Detail: ex.Message)],
            };
        }

        Latest = forecast;
        var key = ForecastProtocol.ContentKey(forecast);
        if (key != _latestKey)
        {
            _latestKey = key;
            Changed?.Invoke(forecast);
        }

        Updated?.Invoke(forecast);
        foreach (var waiter in waiters)
        {
            waiter.TrySetResult(forecast);
        }
    }

    private FileSystemWatcher? CreateWatcher()
    {
        if (watchDirectory is null || !Directory.Exists(watchDirectory))
        {
            return null;
        }

        var watcher = new FileSystemWatcher(watchDirectory, "*.etl")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => Trigger(immediate: false);
        watcher.Created += (_, _) => Trigger(immediate: false);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}
