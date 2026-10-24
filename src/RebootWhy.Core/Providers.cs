namespace RebootWhy.Core;

/// <summary>System log providers whose events the analyzers need.</summary>
public static class Providers
{
    public const string KernelGeneral = "Microsoft-Windows-Kernel-General";
    public const string KernelBoot = "Microsoft-Windows-Kernel-Boot";
    public const string KernelPower = "Microsoft-Windows-Kernel-Power";
    public const string User32 = "User32";
    public const string EventLog = "EventLog";
    public const string EventLogChannel = "Microsoft-Windows-Eventlog";
    public const string BugCheck = "Microsoft-Windows-WER-SystemErrorReporting";
    public const string Whea = "Microsoft-Windows-WHEA-Logger";
    public const string WindowsUpdate = "Microsoft-Windows-WindowsUpdateClient";

    public static IReadOnlyList<string> All { get; } =
    [
        KernelGeneral, KernelBoot, KernelPower, User32, EventLog, EventLogChannel, BugCheck, Whea, WindowsUpdate,
    ];
}
