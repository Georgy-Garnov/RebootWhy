using RebootWhy.Core;
using RebootWhy.Core.Analysis;

namespace RebootWhy.Core.Tests;

public class UptimeCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FastStartup_CountsUptimeFromPowerOn_AndFullBootFromKernelStart()
    {
        var coldBoot = Now.AddDays(-5);
        var fastStartup = Now.AddHours(-3);
        RebootRecord[] records = [Record(fastStartup, BootType.FastStartup), Record(coldBoot, BootType.Cold)];

        var uptime = UptimeCalculator.Calculate(records, Now, sinceKernelStart: Now - coldBoot, activeSinceKernelStart: TimeSpan.FromHours(40));

        Assert.Equal(fastStartup, uptime.LastStartUtc);
        Assert.Equal(BootType.FastStartup, uptime.LastStartType);
        Assert.Equal(TimeSpan.FromHours(3), uptime.SinceLastStart);
        Assert.Equal(coldBoot, uptime.LastFullBootUtc);
        Assert.Equal(TimeSpan.FromHours(40), uptime.ActiveSinceFullBoot);
    }

    [Fact]
    public void BootMissingFromLog_FallsBackToKernelCounter()
    {
        RebootRecord[] records = [Record(Now.AddDays(-40), BootType.Cold)];

        var uptime = UptimeCalculator.Calculate(records, Now, sinceKernelStart: TimeSpan.FromDays(2), activeSinceKernelStart: null);

        Assert.Equal(Now.AddDays(-2), uptime.LastStartUtc);
        Assert.Equal(Now.AddDays(-2), uptime.LastFullBootUtc);
        Assert.Equal(TimeSpan.FromDays(2), uptime.SinceLastStart);
    }

    private static RebootRecord Record(DateTimeOffset boot, BootType type) => new()
    {
        BootTimeUtc = boot,
        BootType = type,
        Cause = RebootCause.Unknown,
        Confidence = Confidence.Low,
        Title = "test",
    };
}
