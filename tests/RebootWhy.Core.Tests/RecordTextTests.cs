using RebootWhy.Core;
using RebootWhy.Core.Analysis;
using RebootWhy.Core.Text;
using static RebootWhy.Core.Tests.EventLogBuilder;

namespace RebootWhy.Core.Tests;

public class RecordTextTests
{
    [Fact]
    public void ContainsEverythingShownInDetailsPanel()
    {
        var log = new EventLogBuilder()
            .ColdBoot(T0)
            .UpdateInstalled(T0.AddHours(2), "2026-10 Security Update (KB5127648)")
            .ShutdownRequest(T0.AddHours(3), @"C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe", reasonCode: "0x80020010", user: @"NT AUTHORITY\SYSTEM")
            .CleanStop(T0.AddHours(3).AddMinutes(1))
            .ResumeBoot(T0.AddHours(3).AddMinutes(2), bootType: 1);
        var record = RebootAnalyzer.Analyze(log.Events)[0];

        var lines = RecordText.Build(record).Split(Environment.NewLine);

        Assert.Equal("Restart to install Windows updates", lines[0]);
        Assert.Equal(string.Empty, lines[1]);
        Assert.Contains(@"Initiator: C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe", lines);
        Assert.Contains("Details: Planned: operating system — service pack (0x80020010)", lines);
        Assert.Contains("Confidence: high", lines);
        Assert.Contains("Notes:", lines);
        Assert.Contains(lines, l => l.StartsWith("• Power-on after \"Shut down\" with Fast Startup"));
        Assert.Contains("Installed updates:", lines);
        Assert.Contains("• 2026-10 Security Update (KB5127648)", lines);
        Assert.Contains("Log events this conclusion is based on:", lines);
        Assert.Equal(record.Evidence.Count, lines.Count(l => l.Length > 0 && char.IsDigit(l[0])));
        Assert.False(lines[^1].Length == 0);
    }

    [Fact]
    public void OmitsEmptySections()
    {
        var record = RebootAnalyzer.Analyze(new EventLogBuilder().ColdBoot(T0).CleanStop(T0.AddHours(1)).ColdBoot(T0.AddHours(2)).Events)[0];
        record = record with { Notes = [] };

        var text = RecordText.Build(record);

        Assert.DoesNotContain("Notes:", text);
        Assert.DoesNotContain("Installed updates", text);
        Assert.Contains("Log events this conclusion is based on:", text);
    }
}
