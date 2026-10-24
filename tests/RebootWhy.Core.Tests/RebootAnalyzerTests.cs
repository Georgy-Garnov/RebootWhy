using RebootWhy.Core;
using RebootWhy.Core.Analysis;
using static RebootWhy.Core.Tests.EventLogBuilder;

namespace RebootWhy.Core.Tests;

public class RebootAnalyzerTests
{
    [Fact]
    public void WindowsUpdateRestart_IsAttributedToWindowsUpdate_WithRestartRelevantUpdates()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .UpdateInstalled(T0.AddHours(2), "2026-10 Security Update (KB5127648)")
            .UpdateInstalled(T0.AddHours(2), "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.461.171.0)")
            .UpdateInstalled(T0.AddHours(2), "9WZDNCRFJBMP-MICROSOFT.WINDOWSSTORE")
            .ShutdownRequest(T0.AddHours(3), @"C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe", reasonCode: "0x80020010", user: @"NT AUTHORITY\SYSTEM")
            .CleanStop(T0.AddHours(3).AddMinutes(1))
            .ColdBoot(T0.AddHours(3).AddMinutes(2));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.WindowsUpdate, latest.Cause);
        Assert.Equal(Confidence.High, latest.Confidence);
        Assert.Equal(ShutdownKind.Restart, latest.ShutdownKind);
        Assert.Equal("Restart to install Windows updates", latest.Title);
        Assert.Equal(["2026-10 Security Update (KB5127648)"], latest.Updates);
        Assert.Equal(@"C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe", latest.Initiator);
        Assert.Equal(T0.AddHours(3).AddMinutes(1).AddSeconds(5), latest.ShutdownTimeUtc);
    }

    [Fact]
    public void UpdateNotificationClickedByUser_IsUserRestartForUpdates()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(T0.AddHours(1), @"C:\WINDOWS\uus\AMD64\MoNotificationUx.exe", reasonCode: "0x80020010", user: @"DESKTOP-01\alice")
            .CleanStop(T0.AddHours(1).AddMinutes(1))
            .ColdBoot(T0.AddHours(1).AddMinutes(2));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.WindowsUpdate, latest.Cause);
        Assert.Equal("Restart by the user to install Windows updates", latest.Title);
    }

    [Fact]
    public void StartMenuRestart_OnNonEnglishWindows_KindIsInferredFromTiming()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(T0.AddHours(1), @"C:\WINDOWS\Explorer.EXE", kind: "Neustart", reasonCode: "0x0")
            .CleanStop(T0.AddHours(1).AddMinutes(1))
            .ColdBoot(T0.AddHours(1).AddMinutes(2));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.User, latest.Cause);
        Assert.Equal(ShutdownKind.Restart, latest.ShutdownKind);
        Assert.Equal("Restart by the user", latest.Title);
        Assert.Equal(@"PC\alice", latest.User);
    }

    [Fact]
    public void UserRestartWithServicingReason_IsRestartForUpdates()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(T0.AddHours(1), @"C:\Windows\System32\winlogon.exe", reasonCode: "0x80020010")
            .CleanStop(T0.AddHours(1).AddMinutes(1))
            .ColdBoot(T0.AddHours(1).AddMinutes(2));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.WindowsUpdate, latest.Cause);
        Assert.Equal("Restart by the user to install Windows updates", latest.Title);
    }

    [Fact]
    public void ShutdownCommand_IsCommandWithComment()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(T0.AddHours(1), @"C:\Windows\system32\shutdown.exe", kind: "power off", reasonCode: "0x80000000", comment: "nightly maintenance")
            .CleanStop(T0.AddHours(1).AddMinutes(1))
            .ColdBoot(T0.AddHours(9));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.Command, latest.Cause);
        Assert.Equal("Shutdown by the command shutdown.exe", latest.Title);
        Assert.Contains("Comment: nightly maintenance", latest.Notes);
    }

    [Fact]
    public void InstallerRestart_IsInstaller()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(T0.AddHours(1), @"C:\Program Files (x86)\Vendor\Setup (2)\VendorSetup.exe", reasonCode: "0x80040002")
            .CleanStop(T0.AddHours(1).AddMinutes(1))
            .ColdBoot(T0.AddHours(1).AddMinutes(2));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.Installer, latest.Cause);
        Assert.Equal(@"C:\Program Files (x86)\Vendor\Setup (2)\VendorSetup.exe", latest.Initiator);
    }

    [Fact]
    public void BugcheckInKernelPower41_IsBlueScreenWithDumpPath()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6), bugcheck: 159)
            .BugCheckReport(boot.AddSeconds(30), "0x0000009f", @"C:\WINDOWS\Minidump\100126-1-01.dmp")
            .UnexpectedShutdown(boot.AddSeconds(31));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.BlueScreen, latest.Cause);
        Assert.Equal("Blue screen (BSOD): DRIVER_POWER_STATE_FAILURE", latest.Title);
        Assert.StartsWith("Code 0x9F.", latest.Reason);
        Assert.Contains(@"Memory dump: C:\WINDOWS\Minidump\100126-1-01.dmp", latest.Notes);
        Assert.Null(latest.ShutdownTimeUtc);
    }

    [Fact]
    public void BugcheckOnlyInWerReport_IsBlueScreen()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(boot)
            .BugCheckReport(boot.AddMinutes(2), "0x00000124");

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.BlueScreen, latest.Cause);
        Assert.Equal("Blue screen (BSOD): WHEA_UNCORRECTABLE_ERROR", latest.Title);
    }

    [Fact]
    public void LongPowerButtonPress_IsForcedPowerOff()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6), powerButton: 134058912345678901);

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.PowerButtonHold, latest.Cause);
        Assert.Equal(Confidence.High, latest.Confidence);
    }

    [Fact]
    public void PowerButtonHoldBugcheck_IsForcedPowerOffNotBlueScreen()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6), bugcheck: 0x1C8);

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.PowerButtonHold, latest.Cause);
    }

    [Fact]
    public void KernelPower41WithoutDetails_IsPowerLossOrHang()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6))
            .UnexpectedShutdown(boot.AddSeconds(30));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.PowerLossOrHang, latest.Cause);
        Assert.Equal(Confidence.Medium, latest.Confidence);
    }

    [Fact]
    public void CrashDuringSleep_IsSleepTransitionFailure()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6), sleep: 1);

        Assert.Equal(RebootCause.SleepTransitionFailure, RebootAnalyzer.Analyze(log.Events)[0].Cause);
    }

    [Fact]
    public void CriticalWheaErrorsBeforeCrash_AreReportedAsLikelyHardwareCause()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .Whea(T0.AddHours(4))
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Contains(latest.Notes, n => n.Contains("(WHEA) were logged: 1 (critical: 1)") && n.Contains("hardware cause"));
    }

    [Fact]
    public void CrashAfterRestartRequest_KeepsCrashAndMentionsRequest()
    {
        var boot = T0.AddHours(5);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(boot.AddMinutes(-5), @"C:\WINDOWS\Explorer.EXE")
            .ColdBoot(boot)
            .KernelPower41(boot.AddSeconds(6), powerButton: 1);

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.PowerButtonHold, latest.Cause);
        Assert.Contains(latest.Notes, n => n.StartsWith("Explorer.EXE requested a restart before the crash"));
    }

    [Fact]
    public void RapidRebootAfterBlueScreen_DoesNotInheritPreviousCrash()
    {
        // A real scenario: BSOD, boot, and a Windows Update restart a minute later.
        var crashBoot = T0.AddHours(5);
        var updateBoot = crashBoot.AddSeconds(70);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(crashBoot)
            .KernelPower41(crashBoot.AddSeconds(6), bugcheck: 0x3B)
            .BugCheckReport(crashBoot.AddSeconds(26), "0x0000003b")
            .UnexpectedShutdown(crashBoot.AddSeconds(27))
            .ShutdownRequest(crashBoot.AddSeconds(45), @"C:\WINDOWS\servicing\TrustedInstaller.exe", reasonCode: "0x80020003", user: @"NT AUTHORITY\SYSTEM")
            .CleanStop(crashBoot.AddSeconds(50))
            .ColdBoot(updateBoot);

        var records = RebootAnalyzer.Analyze(log.Events);

        Assert.Equal(RebootCause.WindowsUpdate, records[0].Cause);
        Assert.Equal(RebootCause.BlueScreen, records[1].Cause);
    }

    [Fact]
    public void HibernateResume_IsHibernation()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .Add(Providers.KernelPower, EventCatalog.ShutdownTransition, T0.AddHours(2))
            .ResumeBoot(T0.AddHours(10), bootType: 2);

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.Hibernation, latest.Cause);
        Assert.Equal(BootType.HibernateResume, latest.BootType);
        Assert.False(latest.IsFullBoot);
    }

    [Fact]
    public void FastStartupAfterUserPowerOff_IsUserPowerOff()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(T0.AddHours(8), @"C:\WINDOWS\Explorer.EXE", kind: "Ausschalten")
            .Add(Providers.KernelPower, EventCatalog.ShutdownTransition, T0.AddHours(8).AddSeconds(10))
            .ResumeBoot(T0.AddHours(20), bootType: 1);

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.User, latest.Cause);
        Assert.Equal(ShutdownKind.PowerOff, latest.ShutdownKind);
        Assert.Equal(BootType.FastStartup, latest.BootType);
        Assert.Equal("Shutdown by the user", latest.Title);
        Assert.Contains(latest.Notes, n => n.Contains("Fast Startup"));
    }

    [Fact]
    public void CleanStopWithoutRequest_IsCleanShutdown()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .CleanStop(T0.AddHours(3))
            .ColdBoot(T0.AddHours(4));

        var latest = RebootAnalyzer.Analyze(log.Events)[0];

        Assert.Equal(RebootCause.CleanShutdown, latest.Cause);
        Assert.Equal(T0.AddHours(3).AddSeconds(5), latest.ShutdownTimeUtc);
    }

    [Fact]
    public void OldestBootWithoutHistory_IsUnknown()
    {
        var records = RebootAnalyzer.Analyze(new EventLogBuilder().ColdBoot(T0).Events);

        var only = Assert.Single(records);
        Assert.Equal(RebootCause.Unknown, only.Cause);
        Assert.Equal(Confidence.Low, only.Confidence);
        Assert.Contains(only.Notes, n => n.StartsWith("This is the earliest boot"));
    }

    [Fact]
    public void BootMarkers_AreMergedIntoOneBoot_UsingKernelStartTime()
    {
        var records = RebootAnalyzer.Analyze(new EventLogBuilder().ColdBoot(T0).Events);

        var only = Assert.Single(records);
        Assert.Equal(T0, only.BootTimeUtc);
        Assert.Equal(BootType.Cold, only.BootType);
        Assert.Equal(3, only.Evidence.Count);
    }

    [Fact]
    public void Records_AreReturnedNewestFirst()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ColdBoot(T0.AddDays(1))
            .ColdBoot(T0.AddDays(2));

        var records = RebootAnalyzer.Analyze(log.Events);

        Assert.Equal([T0.AddDays(2), T0.AddDays(1), T0], records.Select(r => r.BootTimeUtc));
    }

    [Fact]
    public void UpdatesCompletedAfterRestart_AreNotRepeatedInNextUpdateRestart()
    {
        var first = T0.AddHours(1);
        var second = T0.AddHours(2);
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .ShutdownRequest(first.AddMinutes(-1), @"C:\WINDOWS\servicing\TrustedInstaller.exe", reasonCode: "0x80020003", user: @"NT AUTHORITY\SYSTEM")
            .ColdBoot(first)
            .UpdateInstalled(first.AddMinutes(1), "2026-10 Security Update (KB5127648)")
            .ShutdownRequest(second.AddMinutes(-1), @"C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe", reasonCode: "0x80020010", user: @"NT AUTHORITY\SYSTEM")
            .ColdBoot(second);

        var records = RebootAnalyzer.Analyze(log.Events);

        Assert.Equal(["2026-10 Security Update (KB5127648)"], records[1].Updates);
        Assert.Empty(records[0].Updates);
    }
}
