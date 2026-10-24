using System.Text.Json;
using System.Text.RegularExpressions;

namespace RebootWhy.Core.Updates;

/// <summary>
/// Reconstructs from the USO log whether Windows Update is waiting for a restart and when it may perform it.
/// The log format is undocumented: anything that cannot be recognized is simply ignored.
/// </summary>
public static partial class UpdateRebootAnalyzer
{
    public static readonly TimeSpan ImminentWindow = TimeSpan.FromHours(1);

    private const string CommitAction = "Commit";

    /// <summary>Tasks on which USO may install an update and restart the computer (Schedule Scan only searches for updates).</summary>
    private static readonly HashSet<string> WorkTasks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Schedule Work", "Schedule Wake To Work", "Schedule Maintenance Work", "Reboot_AC", "Reboot_Battery",
    };
    private const string DiagnosticsPrefix = "ManagerDiagnostics: WriteToRegistry - ";

    public static RebootForecast Analyze(
        IReadOnlyList<UsoEvent> allEvents,
        UpdateEnvironment environment,
        DateTime now,
        IReadOnlyList<ForecastProblem>? problems = null)
    {
        var events = allEvents
            .Where(e => e.Time <= now && (environment.LastBootLocal is not { } boot || e.Time >= boot))
            .OrderBy(e => e.Time)
            .ToList();

        var catalog = BuildCatalog(events);
        var snapshots = QueueSnapshots(events);
        var pendingIds = snapshots.Count > 0
            ? snapshots[^1].Actions.Where(a => a.Value == CommitAction).Select(a => a.Key).ToList()
            : [];
        AddPendingFromDiagnostics(events, pendingIds);

        var pending = pendingIds
            .Select(id => CreatePendingUpdate(id, catalog, snapshots))
            .ToList();

        var schedule = ReadSchedule(events, environment);
        var scheduleFromTaskScheduler = environment.ScheduledTasks is { Count: > 0 };
        var notes = new List<ForecastNote>();

        if (environment.WindowsUpdateRebootRequired == true && pending.Count == 0)
        {
            notes.Add(new ForecastNote(ForecastNoteKind.RebootRequired));
        }

        if (environment.ComponentServicingRebootPending)
        {
            notes.Add(new ForecastNote(ForecastNoteKind.ServicingPending));
        }

        var rebootExpected = pending.Count > 0
            || environment.WindowsUpdateRebootRequired == true
            || environment.ComponentServicingRebootPending;

        if (!rebootExpected)
        {
            return new RebootForecast
            {
                State = ForecastState.None,
                AnalyzedAt = now,
                Schedule = schedule,
                ScheduleFromTaskScheduler = scheduleFromTaskScheduler,
                Problems = problems ?? [],
            };
        }

        var notBefore = environment.ActiveHours?.NextOutside(now) ?? now;
        var nextRun = schedule.Select(s => s.NextRun).Where(t => t >= notBefore).Order().Cast<DateTime?>().FirstOrDefault();
        var earliest = nextRun ?? notBefore;
        var state = earliest - now <= ImminentWindow ? ForecastState.Imminent : ForecastState.Planned;

        foreach (var update in pending)
        {
            if (ApprovalTime(events, update.Id) is { } approvedAt)
            {
                notes.Add(new ForecastNote(ForecastNoteKind.ApprovedEarlier, Subject: update.Id, Time: approvedAt));
            }

            if (update.IsOverdue)
            {
                notes.Add(new ForecastNote(ForecastNoteKind.Overdue, Subject: update.Id));
            }
        }

        if (environment.ActiveHours is { } activeHours)
        {
            notes.Add(new ForecastNote(ForecastNoteKind.ActiveHours, From: activeHours.StartHour, To: activeHours.EndHour));
        }

        if (LastDeferral(events) is { } deferral && deferral.Get("Result") == "UserEngaged")
        {
            notes.Add(new ForecastNote(ForecastNoteKind.DeferredWhileActive, Time: deferral.Time));
        }

        if (environment.NoAutoRebootWithLoggedOnUsers)
        {
            notes.Add(new ForecastNote(ForecastNoteKind.NoAutoRebootPolicy));
        }

        return new RebootForecast
        {
            State = state,
            AnalyzedAt = now,
            EarliestReboot = earliest,
            PendingUpdates = pending,
            Schedule = schedule,
            ScheduleFromTaskScheduler = scheduleFromTaskScheduler,
            Notes = notes,
            Problems = problems ?? [],
        };
    }

    /// <summary>USO queue snapshots: "id[Action],…". Commit is the action performed on restart.</summary>
    private static List<(DateTime Time, Dictionary<string, string> Actions)> QueueSnapshots(List<UsoEvent> events)
    {
        var snapshots = new List<(DateTime, Dictionary<string, string>)>();
        foreach (var e in events)
        {
            var raw = e.Get("updates");
            if (raw is null || raw.StartsWith('['))
            {
                continue;
            }

            var actions = QueueItem().Matches(raw).ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => m.Groups[2].Value);
            if (actions.Count > 0)
            {
                snapshots.Add((e.Time, actions));
            }
        }

        return snapshots;
    }

    /// <summary>USO also writes a state summary such as "Installed-PendingReboot".</summary>
    private static void AddPendingFromDiagnostics(List<UsoEvent> events, List<string> pendingIds)
    {
        var message = events.LastOrDefault(e => e.Get("message")?.StartsWith(DiagnosticsPrefix, StringComparison.Ordinal) == true)?.Get("message");
        if (message is null)
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(message[DiagnosticsPrefix.Length..]);
            var entries = new List<JsonElement>();
            if (json.RootElement.TryGetProperty("Drivers", out var drivers) && drivers.ValueKind == JsonValueKind.Array)
            {
                entries.AddRange(drivers.EnumerateArray());
            }

            if (json.RootElement.TryGetProperty("OS", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                entries.Add(os);
            }

            foreach (var entry in entries)
            {
                if (entry.TryGetProperty("State", out var stateValue)
                    && stateValue.GetString()?.Contains("PendingReboot", StringComparison.OrdinalIgnoreCase) == true
                    && entry.TryGetProperty("ID", out var idValue)
                    && idValue.GetString()?.ToLowerInvariant() is { } id
                    && !pendingIds.Contains(id))
                {
                    pendingIds.Add(id);
                }
            }
        }
        catch (JsonException)
        {
            // The summary format changed; rely on the queue only.
        }
    }

    private static PendingUpdate CreatePendingUpdate(
        string id,
        Dictionary<string, (string? Title, string? Kind, bool Overdue)> catalog,
        List<(DateTime Time, Dictionary<string, string> Actions)> snapshots)
    {
        DateTime? since = null;
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            if (!snapshots[i].Actions.TryGetValue(id, out var action) || action != CommitAction)
            {
                break;
            }

            since = snapshots[i].Time;
        }

        catalog.TryGetValue(id, out var info);
        return new PendingUpdate(id, info.Title, info.Kind, since, info.Overdue);
    }

    private static Dictionary<string, (string? Title, string? Kind, bool Overdue)> BuildCatalog(List<UsoEvent> events)
    {
        var catalog = new Dictionary<string, (string? Title, string? Kind, bool Overdue)>();
        foreach (var e in events)
        {
            var id = (UpdateId(e.Get("update")) ?? UpdateId(e.Get("uniqueId")) ?? UpdateId(e.Get("update.uniqueId")))?.ToLowerInvariant();
            if (id is null)
            {
                continue;
            }

            catalog.TryGetValue(id, out var info);
            catalog[id] = (
                e.Get("title") ?? info.Title,
                e.Get("type") ?? e.Get("update.type") ?? info.Kind,
                info.Overdue || (e.Get("isOverdue") ?? e.Get("update.isOverdue")) == "true");
        }

        return catalog;
    }

    private static string? UpdateId(string? value) => value is not null && QueueId().IsMatch(value) ? value : null;

    /// <summary>When USO allowed the installation without asking the user because the update counts as approved.</summary>
    private static DateTime? ApprovalTime(List<UsoEvent> events, string id)
    {
        var decision = events.LastOrDefault(e =>
            e.Get("Question") == "Start action"
            && e.Get("Result") == "true"
            && string.Equals(e.Get("update.uniqueId") ?? e.Get("uniqueId"), id, StringComparison.OrdinalIgnoreCase)
            && e.Get("Tag") == "UAP");

        return decision?.Time;
    }

    private static UsoEvent? LastDeferral(List<UsoEvent> events) =>
        events.LastOrDefault(e => e.Get("Question") == "Current Active Deferrals");

    private static List<ScheduledRun> ReadSchedule(List<UsoEvent> events, UpdateEnvironment environment)
    {
        if (environment.ScheduledTasks is { Count: > 0 } tasks)
        {
            return tasks
                .Where(t => t.Value is not null && WorkTasks.Contains(t.Key))
                .Select(t => new ScheduledRun(t.Key, t.Value!.Value))
                .OrderBy(t => t.NextRun)
                .ToList();
        }

        // Task Scheduler is unavailable without admin rights; use the last schedule USO wrote to its log.
        return events
            .Where(e => e.Name == "TaskModified" && e.Get("TaskName") is { } task && WorkTasks.Contains(task))
            .GroupBy(e => e.Get("TaskName")!)
            .Select(g => (Task: g.Key, Next: UsoXmlParser.ParseLocalTime(g.Last().Get("NextRuntimeLocalSystem"))))
            .Where(t => t.Next is not null)
            .Select(t => new ScheduledRun(t.Task, t.Next!.Value))
            .OrderBy(t => t.NextRun)
            .ToList();
    }

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}:\d+)\[(\w*)\]")]
    private static partial Regex QueueItem();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}:\d+$")]
    private static partial Regex QueueId();
}
