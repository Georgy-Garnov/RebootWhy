using System.Globalization;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Monitoring;

/// <summary>
/// Reaction test: for a few minutes replaces the Windows Update log with synthetic records:
/// "a driver is installed and waits for a restart, a Windows task is scheduled in 3 minutes".
/// The records go through the real analyzer and then the usual path to notifications.
/// Nothing changes in the system, so no real restart can happen.
/// </summary>
public sealed class RebootSimulation(Func<RebootForecast> realAnalysis)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RebootIn = TimeSpan.FromMinutes(3);
    public const string UpdateId = "5245424f-4f54-4000-8000-000000000001:1";
    /// <summary>The title as it appears in the log; the program shows a localized one (matched by <see cref="UpdateId"/>).</summary>
    public const string UpdateTitle = "RebootWhy test update (driver)";

    private readonly Lock _lock = new();
    private DateTime? _startedAt;

    public DateTime? EndsAt
    {
        get
        {
            lock (_lock)
            {
                return _startedAt + Duration;
            }
        }
    }

    /// <summary>Start the test and recalculate the forecast at the key moments: right away, when the "restart time" comes, and at the end.</summary>
    public void Start(ForecastEngine engine)
    {
        lock (_lock)
        {
            _startedAt = DateTime.Now;
        }

        engine.Trigger();
        _ = Task.Delay(RebootIn + TimeSpan.FromSeconds(1)).ContinueWith(_ => engine.Trigger(), TaskScheduler.Default);
        _ = Task.Delay(Duration + TimeSpan.FromSeconds(1)).ContinueWith(_ => engine.Trigger(), TaskScheduler.Default);
    }

    public void Stop(ForecastEngine engine)
    {
        lock (_lock)
        {
            _startedAt = null;
        }

        engine.Trigger();
    }

    public RebootForecast Analyze() => Analyze(DateTime.Now);

    public RebootForecast Analyze(DateTime now)
    {
        DateTime? started;
        lock (_lock)
        {
            if (_startedAt is { } at && now >= at + Duration)
            {
                _startedAt = null;
            }

            started = _startedAt;
        }

        if (started is not { } start)
        {
            return realAnalysis();
        }

        // No active hours: the "restart" is possible right on schedule, so the test does not wait for the night.
        var forecast = UpdateRebootAnalyzer.Analyze(Events(start), new UpdateEnvironment(), now);
        return forecast with
        {
            IsSimulation = true,
            Notes = [new ForecastNote(ForecastNoteKind.Simulation, Time: start + Duration), .. forecast.Notes],
        };
    }

    /// <summary>Update Orchestrator log records as Windows writes them (modeled on real logs from the night of 2026-10-23/24).</summary>
    public static List<UsoEvent> Events(DateTime started)
    {
        var rebootAt = started + RebootIn;
        return
        [
            // Records are backdated by a few seconds: the analysis right after the start must already see them.
            Event(started.AddSeconds(-3), "Microsoft.Windows.Update.Orchestrator.WuProvider", "Install",
                ("update", UpdateId), ("title", UpdateTitle)),
            Event(started.AddSeconds(-3), "Microsoft.Windows.Update.Orchestrator.Decisions", "Decision",
                ("Question", "Start action"), ("Result", "true"), ("Tag", "UAP"), ("Path", "Update approved(true)=true->true"),
                ("update.uniqueId", UpdateId), ("update.type", "Driver"), ("update.isOverdue", "false")),
            Event(started.AddSeconds(-2), "Microsoft.Windows.Update.Orchestrator.Worker", "EvaluatingUpdates",
                ("updates", $"{UpdateId}[Commit]")),
            Event(started.AddSeconds(-1), "Microsoft.Windows.Update.Orchestrator", "TaskModified",
                ("TaskName", "Schedule Wake To Work"), ("NextRuntimeLocalSystem", rebootAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))),
        ];
    }

    private static UsoEvent Event(DateTime time, string provider, string name, params (string Key, string Value)[] data) =>
        new(time, provider, name, data.ToDictionary(d => d.Key, d => d.Value, StringComparer.OrdinalIgnoreCase));
}
