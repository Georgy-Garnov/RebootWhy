using System.Text.Json.Serialization;

namespace RebootWhy.Core.Updates;

public enum ForecastState
{
    /// <summary>No restart for updates is expected.</summary>
    None,

    /// <summary>An update is waiting for a restart; Windows will perform it when it sees fit.</summary>
    Planned,

    /// <summary>A restart is possible within the hour.</summary>
    Imminent,
}

/// <param name="Title">Update title from the USO log; null if the log did not mention it.</param>
/// <param name="Type">Raw USO update type: "Driver", "FeatureUpdate", "Quality"…</param>
public sealed record PendingUpdate(string Id, string? Title, string? Type, DateTime? PendingSince, bool IsOverdue);

public sealed record ScheduledRun(string Task, DateTime NextRun);

public enum ForecastNoteKind
{
    /// <summary>Windows Update reports that a restart is required.</summary>
    RebootRequired,

    /// <summary>Component Based Servicing is waiting for a restart.</summary>
    ServicingPending,

    /// <summary>USO allowed the installation because the update counts as approved (Subject — update, Time — decision).</summary>
    ApprovedEarlier,

    /// <summary>The update is overdue (Subject — update).</summary>
    Overdue,

    /// <summary>Active hours (From, To — hours).</summary>
    ActiveHours,

    /// <summary>The restart was postponed because the user was active (Time — decision).</summary>
    DeferredWhileActive,

    /// <summary>NoAutoRebootWithLoggedOnUsers is enabled.</summary>
    NoAutoRebootPolicy,

    /// <summary>The forecast comes from the reaction test (Time — end of the test).</summary>
    Simulation,

    /// <summary>Sample alert: where the reason for the restart will appear.</summary>
    SampleReason,

    /// <summary>Sample alert: no real restart is scheduled.</summary>
    SampleNotice,
}

/// <summary>
/// An explanation of the forecast. Stored as data rather than text: the service and the program may use
/// different languages, so the text is produced on the program side (see <c>ForecastText</c>).
/// </summary>
public sealed record ForecastNote(ForecastNoteKind Kind, string? Subject = null, DateTime? Time = null, int? From = null, int? To = null);

public enum ForecastProblemKind
{
    /// <summary>Subject — the folder.</summary>
    NoUsoFolder,

    /// <summary>Subject — the file, Detail — the error.</summary>
    CannotRead,

    /// <summary>Detail — the error.</summary>
    WindowsUpdateSilent,

    /// <summary>The analysis itself failed; Detail — the error.</summary>
    AnalysisFailed,
}

public sealed record ForecastProblem(ForecastProblemKind Kind, string? Subject = null, string? Detail = null);

/// <summary>Analyzer conclusion about Windows Update plans. Serialized to JSON between the service and the tray app.</summary>
public sealed record RebootForecast
{
    public required ForecastState State { get; init; }

    public required DateTime AnalyzedAt { get; init; }

    /// <summary>Windows may restart the computer no earlier than this moment.</summary>
    public DateTime? EarliestReboot { get; init; }

    public IReadOnlyList<PendingUpdate> PendingUpdates { get; init; } = [];

    public IReadOnlyList<ScheduledRun> Schedule { get; init; } = [];

    /// <summary>The schedule comes from Task Scheduler (exact) rather than from the USO log (last recorded).</summary>
    public bool ScheduleFromTaskScheduler { get; init; }

    public IReadOnlyList<ForecastNote> Notes { get; init; } = [];

    /// <summary>A sample for previewing alerts, not the real state.</summary>
    public bool IsSample { get; init; }

    /// <summary>Reaction test: the analyzer received substituted log records; no real restart will happen.</summary>
    public bool IsSimulation { get; init; }

    /// <summary>What could not be read (USO log, schedule, etc.).</summary>
    public IReadOnlyList<ForecastProblem> Problems { get; init; } = [];

    [JsonIgnore]
    public bool AnalysisFailed => Problems.Any(p => p.Kind == ForecastProblemKind.AnalysisFailed);
}
