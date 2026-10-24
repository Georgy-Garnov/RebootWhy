using RebootWhy.Core.Analysis;
using RebootWhy.Core.Properties;

namespace RebootWhy.Core.Text;

/// <summary>Short localized description of a log event for the evidence list.</summary>
public static class EventDescriber
{
    private const string MicrosoftWindowsPrefix = "Microsoft-Windows-";

    public static string Source(SystemEvent e)
    {
        var provider = e.Provider.StartsWith(MicrosoftWindowsPrefix, StringComparison.OrdinalIgnoreCase)
            ? e.Provider[MicrosoftWindowsPrefix.Length..]
            : e.Provider;
        return $"{provider} {e.Id}";
    }

    /// <summary>Evidence line: time, source, description.</summary>
    public static string Line(SystemEvent e) =>
        $"{Display.LocalDateTime(e.TimeUtc, withSeconds: true)}   {Source(e)}   {Describe(e)}";

    public static string Describe(SystemEvent e)
    {
        if (e.Is(Providers.KernelGeneral, EventCatalog.OsStarted))
        {
            return Strings.Event_OsStarted;
        }

        if (e.Is(Providers.KernelGeneral, EventCatalog.OsStopped))
        {
            return Strings.Event_OsStopped;
        }

        if (e.Is(Providers.KernelBoot, EventCatalog.BootTypeReported))
        {
            return Display.Format(Strings.Event_BootType, Display.BootType(EventCatalog.ToBootType(e.GetInt64("BootType"))));
        }

        if (e.Is(Providers.User32, EventCatalog.ShutdownRequested))
        {
            var request = ShutdownRequest.Parse(e);
            return request.User is null
                ? Display.Format(Strings.Event_Requested, request.ProcessName, Display.KindNoun(request.Kind))
                : Display.Format(Strings.Event_RequestedOnBehalf, request.ProcessName, Display.KindNoun(request.Kind), request.User);
        }

        if (e.Is(Providers.EventLog, EventCatalog.EventLogStarted))
        {
            return Strings.Event_LogStarted;
        }

        if (e.Is(Providers.EventLog, EventCatalog.EventLogStopped))
        {
            return Strings.Event_LogStopped;
        }

        if (e.Is(Providers.EventLog, EventCatalog.UnexpectedShutdown))
        {
            return Strings.Event_UnexpectedShutdown;
        }

        if (e.Is(Providers.EventLogChannel, EventCatalog.LogCleared))
        {
            return Strings.Event_LogCleared;
        }

        if (e.Is(Providers.KernelPower, EventCatalog.UnexpectedReboot))
        {
            var text = e.GetInt64("BugcheckCode") is long code and > 0
                ? Display.Format(Strings.Event_UncleanRebootCode, code)
                : Strings.Event_UncleanReboot;
            return e.GetInt64("PowerButtonTimestamp") > 0 ? text + Strings.Event_PowerButtonPressed : text;
        }

        if (e.Is(Providers.KernelPower, EventCatalog.ShutdownTransition))
        {
            return Strings.Event_ShutdownTransition;
        }

        if (e.Is(Providers.BugCheck, EventCatalog.BugCheckReport))
        {
            return Display.Format(Strings.Event_BlueScreen, e.Get("param1") ?? Strings.Bugcheck_NotRecorded);
        }

        if (e.Is(Providers.WindowsUpdate, EventCatalog.UpdateInstalled))
        {
            return Display.Format(Strings.Event_UpdateInstalled, e.Get("updateTitle") ?? Strings.UntitledUpdate);
        }

        if (e.IsFrom(Providers.Whea))
        {
            return e.Level is 1 or 2 ? Strings.Event_HardwareErrorCritical : Strings.Event_HardwareErrorCorrected;
        }

        return Source(e);
    }
}
