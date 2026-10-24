namespace RebootWhy.Core;

/// <summary>Event IDs used by the analyzers and the rules for selecting relevant events.</summary>
public static class EventCatalog
{
    /// <summary>Kernel-General: the OS started (StartTime field).</summary>
    public const int OsStarted = 12;

    /// <summary>Kernel-General: the OS shut down (StopTime field).</summary>
    public const int OsStopped = 13;

    /// <summary>Kernel-Boot: boot type (BootType field).</summary>
    public const int BootTypeReported = 27;

    /// <summary>Kernel-Power: the system rebooted without cleanly shutting down.</summary>
    public const int UnexpectedReboot = 41;

    /// <summary>Kernel-Power: the kernel power manager initiated a shutdown transition.</summary>
    public const int ShutdownTransition = 109;

    /// <summary>User32: a process requested a restart or shutdown.</summary>
    public const int ShutdownRequested = 1074;

    /// <summary>EventLog: the event log service started.</summary>
    public const int EventLogStarted = 6005;

    /// <summary>EventLog: the event log service stopped.</summary>
    public const int EventLogStopped = 6006;

    /// <summary>EventLog: the previous shutdown was unexpected.</summary>
    public const int UnexpectedShutdown = 6008;

    /// <summary>Microsoft-Windows-Eventlog: the log was cleared.</summary>
    public const int LogCleared = 104;

    /// <summary>WER-SystemErrorReporting: the system recovered from a bugcheck (blue screen).</summary>
    public const int BugCheckReport = 1001;

    /// <summary>WindowsUpdateClient: an update was installed.</summary>
    public const int UpdateInstalled = 19;

    public static bool IsRelevant(SystemEvent e) =>
        e.Is(Providers.KernelGeneral, OsStarted)
        || e.Is(Providers.KernelGeneral, OsStopped)
        || e.Is(Providers.KernelBoot, BootTypeReported)
        || e.Is(Providers.KernelPower, UnexpectedReboot)
        || e.Is(Providers.KernelPower, ShutdownTransition)
        || e.Is(Providers.User32, ShutdownRequested)
        || e.Is(Providers.EventLog, EventLogStarted)
        || e.Is(Providers.EventLog, EventLogStopped)
        || e.Is(Providers.EventLog, UnexpectedShutdown)
        || e.Is(Providers.EventLogChannel, LogCleared)
        || e.Is(Providers.BugCheck, BugCheckReport)
        || e.Is(Providers.WindowsUpdate, UpdateInstalled)
        || (e.IsFrom(Providers.Whea) && e.Level is >= 1 and <= 3);

    /// <summary>Maps the BootType field of Kernel-Boot event 27.</summary>
    public static BootType ToBootType(long? raw) => raw switch
    {
        0 => BootType.Cold,
        1 => BootType.FastStartup,
        2 => BootType.HibernateResume,
        _ => BootType.Unknown,
    };
}
