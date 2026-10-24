using System.Windows.Threading;
using RebootWhy.Core.Monitoring;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.Monitoring;

/// <summary>
/// The program's forecast source: the RebootWhy service or, without it, its own analysis
/// (without the Task Scheduler schedule, which only administrators can read).
/// Events are raised on the UI thread.
/// </summary>
public sealed class ForecastCoordinator(Dispatcher dispatcher) : IDisposable
{
    private static readonly TimeSpan LocalPeriod = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LocalDebounce = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ServiceReplyTimeout = TimeSpan.FromSeconds(90);

    /// <summary>A check usually takes a fraction of a second; without a pause the indicator would just flicker unnoticed.</summary>
    private static readonly TimeSpan MinimumVisibleCheck = TimeSpan.FromMilliseconds(700);

    private readonly ForecastClient _client = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _localStop;
    private ForecastEngine? _local;
    private RebootSimulation? _localSimulation;

    /// <summary>A new analysis result (including an unchanged one after a manual check).</summary>
    public event Action<RebootForecast>? ForecastUpdated;

    public event Action? SourceChanged;

    public event Action? CheckingChanged;

    public RebootForecast? Latest { get; private set; }

    public bool FromService { get; private set; }

    public bool IsChecking { get; private set; }

    public bool IsSimulating => Latest?.IsSimulation == true;

    public void Start()
    {
        _client.ForecastReceived += forecast => dispatcher.InvokeAsync(() => Publish(forecast, fromService: true));
        _client.ConnectionChanged += connected => dispatcher.InvokeAsync(() => OnConnectionChanged(connected));
        _ = RunQuietlyAsync(_client.RunAsync(_stop.Token));
    }

    /// <summary>An immediate check requested by the user.</summary>
    public async Task AnalyzeNowAsync()
    {
        if (IsChecking)
        {
            return;
        }

        SetChecking(true);
        var started = DateTime.Now;
        try
        {
            var result = FromService ? await _client.AnalyzeAsync(ServiceReplyTimeout) : null;
            if (result is null && _local is { } local)
            {
                // The result also arrives through the Updated event, so there is no need to publish it here.
                await local.RecalculateNowAsync();
            }

            var elapsed = DateTime.Now - started;
            if (elapsed < MinimumVisibleCheck)
            {
                await Task.Delay(MinimumVisibleCheck - elapsed);
            }
        }
        finally
        {
            SetChecking(false);
        }
    }

    /// <summary>Reaction test: a substituted restart wait for a few minutes, without changes to the system.</summary>
    public async Task<bool> StartSimulationAsync()
    {
        if (FromService)
        {
            return await _client.StartSimulationAsync();
        }

        if (_local is null || _localSimulation is null)
        {
            return false;
        }

        _localSimulation.Start(_local);
        return true;
    }

    public async Task StopSimulationAsync()
    {
        if (FromService)
        {
            await _client.StopSimulationAsync();
        }
        else if (_local is not null)
        {
            _localSimulation?.Stop(_local);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _localStop?.Cancel();
    }

    private void OnConnectionChanged(bool connected)
    {
        FromService = connected;
        if (connected)
        {
            _localStop?.Cancel();
            _local = null;
            _localSimulation = null;
        }
        else if (_local is null)
        {
            var monitor = new UpdateRebootMonitor();
            _localSimulation = new RebootSimulation(monitor.AnalyzeNow);
            _local = new ForecastEngine(_localSimulation.Analyze, LocalPeriod, LocalDebounce, UsoLogSource.DefaultDirectory);
            _local.Updated += forecast => dispatcher.InvokeAsync(() => Publish(forecast, fromService: false));
            _localStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _ = RunQuietlyAsync(_local.RunAsync(_localStop.Token));
        }

        SourceChanged?.Invoke();
    }

    private void Publish(RebootForecast forecast, bool fromService)
    {
        // A late result of the local analysis is not needed once connected to the service.
        if (fromService != FromService)
        {
            return;
        }

        Latest = forecast;
        ForecastUpdated?.Invoke(forecast);
    }

    private void SetChecking(bool value)
    {
        IsChecking = value;
        CheckingChanged?.Invoke();
    }

    private static async Task RunQuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
