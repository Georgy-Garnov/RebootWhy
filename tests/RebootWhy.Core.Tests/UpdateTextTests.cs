using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Tests;

public class UpdateTextTests
{
    [Theory]
    [InlineData("2026-10 Security Update (KB5127648) (26200.9711)", "KB5127648")]
    [InlineData("Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.461.171.0)", "KB2267602")]
    [InlineData("Intel Corporation Display Driver Update (32.0.101.7085)", null)]
    public void KbIsExtractedFromTitle(string title, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], UpdateCatalog.KbFromTitle(title));

    [Fact]
    public void DefinitionUpdatesAreRecognized()
    {
        Assert.True(UpdateCatalog.IsDefinitionUpdate("Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.461.176.0)"));
        Assert.False(UpdateCatalog.IsDefinitionUpdate("2026-10 Security Update (KB5127648)"));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(unchecked((int)0x80246007), "0x80246007 — the update was not downloaded")]
    [InlineData(unchecked((int)0x8024FFFF), "0x8024FFFF")]
    public void ErrorCodesAreDescribed(int hresult, string? expected) =>
        Assert.Equal(expected, UpdateText.Error(hresult));

    [Fact]
    public void CopyTextContainsFactsAndDescription()
    {
        var update = new InstalledUpdate(
            new DateTime(2026, 10, 24, 1, 30, 59), "Intel Corporation Display Driver Update (32.0.101.7085)", "Intel driver update",
            UpdateOperation.Installation, UpdateResult.Succeeded, 0, "MoUpdateOrchestrator", null, "3b096b01-5676-4a86-b8f1-1e0ca38ed902", 1, []);

        var lines = UpdateText.Build(update.Title, UpdateText.Facts(update), update.Description).Split(Environment.NewLine);

        Assert.Equal("Intel Corporation Display Driver Update (32.0.101.7085)", lines[0]);
        Assert.Contains("Result: Installed (installation)", lines);
        Assert.Contains("Initiator: Windows Update (automatic)", lines);
        Assert.Equal("Intel driver update", lines[^1]);
    }
}
