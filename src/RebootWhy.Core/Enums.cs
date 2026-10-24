namespace RebootWhy.Core;

public enum BootType
{
    Unknown,

    /// <summary>Full (cold) boot.</summary>
    Cold,

    /// <summary>Fast Startup: the kernel was resumed from hibernation after "Shut down".</summary>
    FastStartup,

    /// <summary>Resume from hibernation.</summary>
    HibernateResume,
}

public enum RebootCause
{
    Unknown,
    WindowsUpdate,
    User,
    Command,
    Installer,
    Application,
    BlueScreen,
    PowerButtonHold,
    SleepTransitionFailure,
    PowerLossOrHang,
    CleanShutdown,
    Hibernation,
}

public enum Confidence
{
    Low,
    Medium,
    High,
}

public enum ShutdownKind
{
    Unknown,
    Restart,
    PowerOff,
}
