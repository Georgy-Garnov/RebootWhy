using RebootWhy.Core;
using RebootWhy.Core.Analysis;
using RebootWhy.Core.Text;

namespace RebootWhy.Core.Tests;

public class DecoderTests
{
    [Fact]
    public void ShutdownReason_DecodesPlannedServicePack()
    {
        var reason = new ShutdownReason(0x80020010);

        Assert.True(reason.Planned);
        Assert.True(reason.IsServicing);
        Assert.Equal("Planned: operating system — service pack (0x80020010)", reason.Describe());
    }

    [Fact]
    public void ShutdownReason_UnplannedOtherIsNotServicing()
    {
        var reason = new ShutdownReason(0x500FF);

        Assert.False(reason.Planned);
        Assert.False(reason.IsServicing);
        Assert.Equal("Unplanned: system — not specified (0x500FF)", reason.Describe());
    }

    [Theory]
    [InlineData(0x7Eu, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED")]
    [InlineData(0x1000007Eu, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED")]
    [InlineData(0x133u, "DPC_WATCHDOG_VIOLATION")]
    [InlineData(0xDEADu, "0xDEAD")]
    public void Bugcheck_IsResolvedByName(uint code, string expected) =>
        Assert.Equal(expected, BugcheckCatalog.Describe(code).Name);

    [Theory]
    [InlineData(@"C:\Program Files (x86)\App\app.exe (PC)", @"C:\Program Files (x86)\App\app.exe")]
    [InlineData(@"C:\Program Files (x86)\App\app.exe", @"C:\Program Files (x86)\App\app.exe")]
    [InlineData(@"C:\WINDOWS\Explorer.EXE (DESKTOP-01)", @"C:\WINDOWS\Explorer.EXE")]
    public void ProcessPath_StripsComputerName(string raw, string expected) =>
        Assert.Equal(expected, ProcessClassifier.StripComputerName(raw));

    [Theory]
    [InlineData("restart", ShutdownKind.Restart)]
    [InlineData("Restart", ShutdownKind.Restart)]
    [InlineData("power off", ShutdownKind.PowerOff)]
    [InlineData("Neustart", ShutdownKind.Unknown)]
    [InlineData("", ShutdownKind.Unknown)]
    public void ShutdownKind_IsParsedFromEnglishText(string raw, ShutdownKind expected) =>
        Assert.Equal(expected, ShutdownRequest.ParseKind(raw));

    [Theory]
    [InlineData("0x9f", 159L)]
    [InlineData("159", 159L)]
    [InlineData("true", 1L)]
    [InlineData("‎42", 42L)]
    [InlineData("abc", null)]
    public void EventValue_ParsesNumbers(string raw, long? expected) =>
        Assert.Equal(expected, EventValue.ParseInt64(raw));

    [Fact]
    public void EventValue_ParsesNanosecondTimestamps() =>
        Assert.Equal(
            new DateTimeOffset(2026, 10, 24, 1, 31, 21, TimeSpan.Zero).AddTicks(5000000),
            EventValue.ParseTimestamp("2026-10-24T01:31:21.500000000Z"));

    [Theory]
    [InlineData(0, "less than a minute")]
    [InlineData(61, "1 min")]
    [InlineData(3 * 86400 + 4 * 3600 + 12 * 60, "3 d 4 h 12 min")]
    [InlineData(2 * 86400, "2 d")]
    public void Duration_IsFormatted(int seconds, string expected) =>
        Assert.Equal(expected, Display.Duration(TimeSpan.FromSeconds(seconds)));
}
