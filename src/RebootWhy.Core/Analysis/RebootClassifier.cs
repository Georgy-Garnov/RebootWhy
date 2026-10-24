using RebootWhy.Core.Properties;
using RebootWhy.Core.Text;

namespace RebootWhy.Core.Analysis;

/// <summary>Determines how the session before a specific boot ended.</summary>
internal static class RebootClassifier
{
    /// <summary>
    /// Crash evidence (Kernel-Power 41, BugCheck 1001, EventLog 6008, WHEA) is written after the next boot.
    /// It is searched strictly after that boot starts; otherwise, with rapid reboots, a crash would be attributed to the wrong session.
    /// </summary>
    private static readonly TimeSpan AfterBootWindow = TimeSpan.FromMinutes(20);

    /// <summary>If the session start is not in the log, updates are only searched for during the last days before the boot.</summary>
    private static readonly TimeSpan UpdatesLookbackWithoutSession = TimeSpan.FromDays(3);

    /// <summary>
    /// A restart brings the machine back within a couple of minutes; a power-off usually lasts much longer.
    /// Used when the shutdown type in event 1074 is written in a display language we cannot read.
    /// </summary>
    private static readonly TimeSpan RestartGap = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PowerOffGap = TimeSpan.FromMinutes(10);

    public static RebootRecord Classify(
        BootCluster boot,
        BootCluster? previous,
        BootCluster? next,
        IReadOnlyList<SystemEvent> events,
        ISet<long> claimedUpdates)
    {
        var sessionStart = previous?.TimeUtc ?? DateTimeOffset.MinValue;
        var afterBootEnd = Min(boot.TimeUtc + AfterBootWindow, next?.TimeUtc ?? DateTimeOffset.MaxValue);
        var previousAfterBootEnd = previous is null
            ? DateTimeOffset.MinValue
            : Min(previous.TimeUtc + AfterBootWindow, boot.TimeUtc);

        var beforeBoot = Range(events, sessionStart, boot.TimeUtc);
        var afterBoot = Range(events, boot.TimeUtc, afterBootEnd);

        var request = beforeBoot.LastOrDefault(e => e.Is(Providers.User32, EventCatalog.ShutdownRequested));
        var cleanStops = beforeBoot.Where(IsCleanStop).ToList();
        var kernelPower = afterBoot.LastOrDefault(e => e.Is(Providers.KernelPower, EventCatalog.UnexpectedReboot));
        var bugcheckReport = afterBoot.LastOrDefault(e => e.Is(Providers.BugCheck, EventCatalog.BugCheckReport));
        var unexpectedShutdown = afterBoot.LastOrDefault(e => e.Is(Providers.EventLog, EventCatalog.UnexpectedShutdown));
        var hardwareErrors = Range(events, previousAfterBootEnd, afterBootEnd).Where(e => e.IsFrom(Providers.Whea)).ToList();
        var updatesFrom = previous is null ? boot.TimeUtc - UpdatesLookbackWithoutSession : sessionStart;
        var installedUpdates = Range(events, updatesFrom, afterBootEnd)
            .Where(e => e.Is(Providers.WindowsUpdate, EventCatalog.UpdateInstalled)
                && !claimedUpdates.Contains(e.RecordId)
                && WindowsUpdateFilter.MayRequireRestart(UpdateTitle(e)))
            .ToList();
        var logCleared = beforeBoot.Any(e => e.Is(Providers.EventLogChannel, EventCatalog.LogCleared));

        var draft = new Draft(boot);
        var bugcheck = ReadBugcheck(kernelPower, bugcheckReport);
        var crashed = bugcheck is not null || kernelPower is not null || unexpectedShutdown is not null;

        if (crashed)
        {
            ClassifyCrash(draft, bugcheck, kernelPower);
            draft.Evidence.AddRange(new[] { kernelPower, bugcheckReport, unexpectedShutdown }.OfType<SystemEvent>());
            if (DumpPath(bugcheckReport) is { } dump)
            {
                draft.Notes.Add(Display.Format(Strings.Note_DumpPath, dump));
            }

            if (cleanStops.Count > 0)
            {
                draft.Notes.Add(Strings.Note_ShutdownHung);
                draft.Evidence.AddRange(cleanStops);
            }
            else if (request is not null)
            {
                var parsed = ShutdownRequest.Parse(request);
                draft.Notes.Add(Display.Format(Strings.Note_RequestBeforeCrash, parsed.ProcessName, Display.KindNoun(parsed.Kind)));
                draft.Evidence.Add(request);
            }
        }
        else if (boot.Type == BootType.HibernateResume)
        {
            draft.Set(RebootCause.Hibernation, Confidence.High, Strings.Title_Hibernation);
        }
        else if (request is not null)
        {
            draft.ShutdownTimeUtc = StopTime(cleanStops) ?? request.TimeUtc;
            ClassifyRequest(draft, ShutdownRequest.Parse(request), installedUpdates.Count > 0, boot);
            draft.Evidence.Add(request);
            draft.Evidence.AddRange(cleanStops);
        }
        else if (cleanStops.Count > 0)
        {
            draft.Set(RebootCause.CleanShutdown, Confidence.Medium, Strings.Title_CleanShutdown);
            draft.Notes.Add(Strings.Note_CleanShutdownHint);
            draft.Evidence.AddRange(cleanStops);
            draft.ShutdownTimeUtc = StopTime(cleanStops);
        }
        else
        {
            draft.Set(RebootCause.Unknown, Confidence.Low, Strings.Title_Unknown);
            draft.Notes.Add(previous is null ? Strings.Note_EarliestBoot : Strings.Note_NoShutdownRecord);
        }

        if (boot.Type == BootType.FastStartup)
        {
            draft.Notes.Add(Strings.Note_FastStartup);
        }

        if (hardwareErrors.Count > 0)
        {
            var critical = hardwareErrors.Count(e => e.Level is 1 or 2);
            var note = Display.Format(Strings.Note_Whea, hardwareErrors.Count, critical);
            draft.Notes.Add(crashed && critical > 0 ? note + Strings.Note_WheaLikelyCause : note);
            draft.Evidence.AddRange(hardwareErrors);
        }

        if (logCleared)
        {
            draft.Notes.Add(Strings.Note_LogCleared);
        }

        if (draft.Cause == RebootCause.WindowsUpdate)
        {
            // The same update is often logged several times in a row; keep only the latest record as evidence.
            var latestPerTitle = installedUpdates.GroupBy(UpdateTitle).Select(g => g.Last()).OrderBy(e => e.TimeUtc).ToList();
            draft.Updates.AddRange(latestPerTitle.Select(UpdateTitle));
            draft.Evidence.AddRange(latestPerTitle);
            claimedUpdates.UnionWith(installedUpdates.Select(e => e.RecordId));
        }

        return draft.ToRecord();
    }

    private static void ClassifyCrash(Draft draft, BugcheckInfo? bugcheck, SystemEvent? kernelPower)
    {
        if (bugcheck is not null)
        {
            draft.Reason = bugcheck.Describe();
            if (bugcheck.IsPowerButtonHold)
            {
                draft.Set(RebootCause.PowerButtonHold, Confidence.High, Strings.Title_PowerButtonBugcheck);
            }
            else
            {
                draft.Set(RebootCause.BlueScreen, Confidence.High, Display.Format(Strings.Title_BlueScreen, bugcheck.Name));
            }

            return;
        }

        if (kernelPower?.GetInt64("LongPowerButtonPressDetected") > 0 || kernelPower?.GetInt64("PowerButtonTimestamp") > 0)
        {
            draft.Set(RebootCause.PowerButtonHold, Confidence.High, Strings.Title_PowerButtonHold);
        }
        else if (kernelPower?.GetInt64("SleepInProgress") > 0)
        {
            draft.Set(RebootCause.SleepTransitionFailure, Confidence.Medium, Strings.Title_SleepFailure);
        }
        else
        {
            draft.Set(
                RebootCause.PowerLossOrHang,
                kernelPower is null ? Confidence.Low : Confidence.Medium,
                Strings.Title_PowerLoss);
        }
    }

    private static void ClassifyRequest(Draft draft, ShutdownRequest request, bool hasInstalledUpdates, BootCluster boot)
    {
        var kind = request.Kind != ShutdownKind.Unknown ? request.Kind : InferKind(boot, draft.ShutdownTimeUtc);
        var verb = Display.KindVerb(kind);
        var servicing = request.Reason?.IsServicing == true;
        var (cause, confidence, title) = ProcessClassifier.Classify(request.ProcessName) switch
        {
            InitiatorCategory.WindowsUpdate when request.IsOnBehalfOfUser => (RebootCause.WindowsUpdate, Confidence.High, Display.Format(Strings.Title_WindowsUpdateByUser, verb)),
            InitiatorCategory.WindowsUpdate => (RebootCause.WindowsUpdate, Confidence.High, Display.Format(Strings.Title_WindowsUpdate, verb)),
            InitiatorCategory.User when servicing => (RebootCause.WindowsUpdate, Confidence.High, Display.Format(Strings.Title_WindowsUpdateByUser, verb)),
            InitiatorCategory.User => (RebootCause.User, Confidence.High, Display.Format(Strings.Title_User, verb)),
            InitiatorCategory.Command => (RebootCause.Command, Confidence.High, Display.Format(Strings.Title_Command, verb, request.ProcessName)),
            InitiatorCategory.Installer => (RebootCause.Installer, Confidence.High, Display.Format(Strings.Title_Installer, verb, request.ProcessName)),
            _ when servicing && hasInstalledUpdates => (RebootCause.WindowsUpdate, Confidence.Medium, Display.Format(Strings.Title_WindowsUpdateGuess, verb, request.ProcessName)),
            _ => (RebootCause.Application, Confidence.High, Display.Format(Strings.Title_Application, verb, request.ProcessName)),
        };

        draft.Set(cause, confidence, title);
        draft.ShutdownKind = kind;
        draft.Initiator = request.ProcessPath;
        draft.User = request.User;
        draft.Reason = request.Reason?.Describe();
        if (request.Comment is { } comment)
        {
            draft.Notes.Add(Display.Format(Strings.Note_Comment, comment));
        }
    }

    private static BugcheckInfo? ReadBugcheck(SystemEvent? kernelPower, SystemEvent? report)
    {
        if (kernelPower?.GetInt64("BugcheckCode") is long code and > 0)
        {
            return BugcheckCatalog.Describe((uint)code);
        }

        if (report is null)
        {
            return null;
        }

        // param1 looks like "0x0000009f (0x0000000000000003, ...)".
        var raw = report.Get("param1") ?? report.Data.Values.FirstOrDefault(v => v.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
        var token = raw?.Split([' ', '('], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return BugcheckCatalog.Describe(EventValue.ParseInt64(token) is long parsed and > 0 ? (uint)parsed : 0);
    }

    private static string UpdateTitle(SystemEvent e) => e.Get("updateTitle") ?? Strings.UntitledUpdate;

    /// <summary>
    /// Restart or power-off, when event 1074 does not say it in a language we understand:
    /// a Fast Startup boot always follows a power-off; otherwise the length of the gap tells.
    /// </summary>
    private static ShutdownKind InferKind(BootCluster boot, DateTimeOffset? shutdownTime)
    {
        if (boot.Type == BootType.FastStartup)
        {
            return ShutdownKind.PowerOff;
        }

        if (shutdownTime is not { } stopped)
        {
            return ShutdownKind.Unknown;
        }

        var gap = boot.TimeUtc - stopped;
        return gap <= RestartGap ? ShutdownKind.Restart : gap >= PowerOffGap ? ShutdownKind.PowerOff : ShutdownKind.Unknown;
    }

    private static string? DumpPath(SystemEvent? report) =>
        report?.Data.Values.FirstOrDefault(v => v.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));

    private static DateTimeOffset? StopTime(IReadOnlyList<SystemEvent> cleanStops)
    {
        var osStopped = cleanStops.LastOrDefault(e => e.Is(Providers.KernelGeneral, EventCatalog.OsStopped));
        return EventValue.ParseTimestamp(osStopped?.Get("StopTime")) ?? osStopped?.TimeUtc ?? cleanStops.LastOrDefault()?.TimeUtc;
    }

    private static bool IsCleanStop(SystemEvent e) =>
        e.Is(Providers.KernelGeneral, EventCatalog.OsStopped)
        || e.Is(Providers.EventLog, EventCatalog.EventLogStopped)
        || e.Is(Providers.KernelPower, EventCatalog.ShutdownTransition);

    private static List<SystemEvent> Range(IReadOnlyList<SystemEvent> events, DateTimeOffset from, DateTimeOffset to) =>
        events.Where(e => e.TimeUtc >= from && e.TimeUtc < to).ToList();

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private sealed class Draft(BootCluster boot)
    {
        public RebootCause Cause { get; private set; }

        public Confidence Confidence { get; private set; }

        public string Title { get; private set; } = string.Empty;

        public ShutdownKind ShutdownKind { get; set; }

        public DateTimeOffset? ShutdownTimeUtc { get; set; }

        public string? Initiator { get; set; }

        public string? User { get; set; }

        public string? Reason { get; set; }

        public List<string> Updates { get; } = [];

        public List<string> Notes { get; } = [];

        public List<SystemEvent> Evidence { get; } = [.. boot.Markers];

        public void Set(RebootCause cause, Confidence confidence, string title)
        {
            Cause = cause;
            Confidence = confidence;
            Title = title;
        }

        public RebootRecord ToRecord() => new()
        {
            BootTimeUtc = boot.TimeUtc,
            BootType = boot.Type,
            Cause = Cause,
            Confidence = Confidence,
            Title = Title,
            ShutdownKind = ShutdownKind,
            ShutdownTimeUtc = ShutdownTimeUtc,
            Initiator = Initiator,
            User = User,
            Reason = Reason,
            Updates = Updates,
            Notes = Notes,
            Evidence = Evidence.Distinct().OrderBy(e => e.TimeUtc).ThenBy(e => e.RecordId).ToList(),
        };
    }
}
