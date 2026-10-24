using System.Globalization;
using RebootWhy.Core;

namespace RebootWhy.Core.Tests;

/// <summary>Builds a synthetic System log in the form EventXmlParser produces.</summary>
internal sealed class EventLogBuilder
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly List<SystemEvent> _events = [];
    private long _recordId;

    public IReadOnlyList<SystemEvent> Events => _events;

    public EventLogBuilder Add(string provider, int id, DateTimeOffset time, params (string Key, string Value)[] data) =>
        Add(provider, id, time, level: 4, data);

    public EventLogBuilder Add(string provider, int id, DateTimeOffset time, int level, params (string Key, string Value)[] data)
    {
        var values = data.ToDictionary(d => d.Key, d => d.Value, StringComparer.OrdinalIgnoreCase);
        _events.Add(new SystemEvent(provider, id, time, level, ++_recordId, values));
        return this;
    }

    /// <summary>Full boot: Kernel-General 12, Kernel-Boot 27 and the event log service start.</summary>
    public EventLogBuilder ColdBoot(DateTimeOffset time) =>
        Add(Providers.KernelGeneral, EventCatalog.OsStarted, time.AddSeconds(1), ("StartTime", time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'00Z'", CultureInfo.InvariantCulture)))
            .Add(Providers.KernelBoot, EventCatalog.BootTypeReported, time.AddSeconds(1), ("BootType", "0"))
            .Add(Providers.EventLog, EventCatalog.EventLogStarted, time.AddSeconds(25));

    /// <summary>Boot without an event log service restart: fast startup (1) or resume from hibernation (2).</summary>
    public EventLogBuilder ResumeBoot(DateTimeOffset time, int bootType) =>
        Add(Providers.KernelBoot, EventCatalog.BootTypeReported, time, ("BootType", bootType.ToString()));

    public EventLogBuilder ShutdownRequest(
        DateTimeOffset time,
        string process,
        string kind = "restart",
        string reasonCode = "0x500ff",
        string user = @"PC\alice",
        string comment = "") =>
        Add(
            Providers.User32,
            EventCatalog.ShutdownRequested,
            time,
            ("param1", $"{process} (PC)"),
            ("param2", "PC"),
            ("param3", "No title for this reason could be found"),
            ("param4", reasonCode),
            ("param5", kind),
            ("param6", comment),
            ("param7", user));

    /// <summary>Clean shutdown: the event log service stopped, the power manager and the kernel shut down.</summary>
    public EventLogBuilder CleanStop(DateTimeOffset time) =>
        Add(Providers.EventLog, EventCatalog.EventLogStopped, time)
            .Add(Providers.KernelPower, EventCatalog.ShutdownTransition, time.AddSeconds(4))
            .Add(Providers.KernelGeneral, EventCatalog.OsStopped, time.AddSeconds(5), ("StopTime", time.AddSeconds(5).UtcDateTime.ToString("O")));

    public EventLogBuilder KernelPower41(DateTimeOffset time, long bugcheck = 0, long powerButton = 0, long sleep = 0) =>
        Add(
            Providers.KernelPower,
            EventCatalog.UnexpectedReboot,
            time,
            level: 1,
            ("BugcheckCode", bugcheck.ToString()),
            ("PowerButtonTimestamp", powerButton.ToString()),
            ("SleepInProgress", sleep.ToString()));

    public EventLogBuilder UnexpectedShutdown(DateTimeOffset time) =>
        Add(Providers.EventLog, EventCatalog.UnexpectedShutdown, time, level: 2, ("Data0", "8:00:00"), ("Data1", "01.10.2026"));

    public EventLogBuilder BugCheckReport(DateTimeOffset time, string code, string dump = @"C:\WINDOWS\Minidump\100126-1-01.dmp") =>
        Add(Providers.BugCheck, EventCatalog.BugCheckReport, time, level: 2, ("param1", $"{code} (0x0, 0x0, 0x0, 0x0)"), ("param2", dump), ("param3", "id"));

    public EventLogBuilder UpdateInstalled(DateTimeOffset time, string title) =>
        Add(Providers.WindowsUpdate, EventCatalog.UpdateInstalled, time, ("updateTitle", title));

    public EventLogBuilder Whea(DateTimeOffset time, int id = 18, int level = 2) =>
        Add(Providers.Whea, id, time, level);
}
