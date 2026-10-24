using System.Globalization;
using System.Runtime.Versioning;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Tests;

/// <summary>
/// Real USO logs from 2026-10-23/24: an Intel driver waited for a restart since 15:11,
/// and Windows restarted the computer at 01:30 (active hours 7:00–1:00).
/// </summary>
[SupportedOSPlatform("windows")]
public class UpdateRebootAnalyzerFixtureTests
{
    private static readonly Lazy<List<UsoEvent>> Night = new(LoadNight);

    private static readonly UpdateEnvironment Environment = new()
    {
        ActiveHours = new ActiveHours(7, 1),
        LastBootLocal = new DateTime(2026, 10, 23, 15, 3, 46),
    };

    /// <summary>UTC offset of the computer where the fixture logs were recorded.</summary>
    private static readonly TimeSpan RecordedOffset = TimeSpan.FromHours(3);

    private const string IntelDriver = "Intel Corporation Display Driver Update (32.0.101.7085)";

    [Fact]
    public void BeforeDriverWasStaged_NoRebootExpected()
    {
        var forecast = UpdateRebootAnalyzer.Analyze(Night.Value, Environment, new DateTime(2026, 10, 23, 15, 10, 0));

        Assert.Equal(ForecastState.None, forecast.State);
    }

    [Fact]
    public void Evening_DriverWaitsForReboot_NotBeforeEndOfActiveHours()
    {
        var forecast = UpdateRebootAnalyzer.Analyze(Night.Value, Environment, new DateTime(2026, 10, 23, 19, 14, 0));

        Assert.Equal(ForecastState.Planned, forecast.State);
        var update = Assert.Single(forecast.PendingUpdates);
        Assert.Equal(IntelDriver, update.Title);
        Assert.Equal("Driver", update.Type);
        Assert.True(update.IsOverdue);
        Assert.Equal(new DateTime(2026, 10, 23, 15, 11, 0), Truncate(update.PendingSince!.Value));
        Assert.Equal(new DateTime(2026, 10, 24, 1, 15, 0), forecast.EarliestReboot);
        var notes = ForecastText.Notes(forecast);
        Assert.Contains(notes, n => n.StartsWith($"\"{IntelDriver}\": at Oct 23, 15:11 Windows allowed the installation"));
        Assert.Contains(notes, n => n.StartsWith("Active hours: 7:00–1:00."));
        Assert.Contains(ForecastText.Evidence(forecast), e => e.StartsWith("Waiting for a restart since Oct 23, 15:11"));
    }

    [Fact]
    public void LateEvening_UserAtComputer_DeferralIsReported()
    {
        var forecast = UpdateRebootAnalyzer.Analyze(Night.Value, Environment, new DateTime(2026, 10, 23, 23, 0, 0));

        Assert.Equal(ForecastState.Planned, forecast.State);
        Assert.Contains(forecast.Notes, n => n.Kind == ForecastNoteKind.DeferredWhileActive);
    }

    [Fact]
    public void LessThanHourBefore_IsImminent()
    {
        var forecast = UpdateRebootAnalyzer.Analyze(Night.Value, Environment, new DateTime(2026, 10, 24, 0, 30, 0));

        Assert.Equal(ForecastState.Imminent, forecast.State);
        Assert.Equal($"Windows may restart the computer within the hour (around Oct 24, 01:15) to install \"{IntelDriver}\"", ForecastText.Summary(forecast));
    }

    [Fact]
    public void AfterReboot_EventsOfPreviousSessionAreIgnored()
    {
        var afterReboot = Environment with { LastBootLocal = new DateTime(2026, 10, 24, 1, 31, 20) };

        var forecast = UpdateRebootAnalyzer.Analyze(Night.Value, afterReboot, new DateTime(2026, 10, 24, 2, 0, 0));

        Assert.Equal(ForecastState.None, forecast.State);
    }

    private static DateTime Truncate(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0);

    private static List<UsoEvent> LoadNight()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Decoding USO logs requires tracerpt from Windows.");
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "uso-2026-10-24");
        // tracerpt converts event times to the local time of the machine that decodes the log;
        // bring them back to the local time of the recording machine, as the expectations use it.
        return Directory.GetFiles(directory, "*.etl")
            .Order()
            .SelectMany(file => UsoXmlParser.Parse(UsoLogSource.Decode(file)))
            .Select(e => e with { Time = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(e.Time, DateTimeKind.Unspecified), TimeZoneInfo.Local) + RecordedOffset })
            .ToList();
    }
}

public class UpdateRebootAnalyzerTests
{
    private static readonly DateTime Evening = new(2026, 10, 23, 20, 0, 0);

    [Fact]
    public void WindowsUpdateFlagAlone_PlansRebootAfterActiveHours()
    {
        var environment = new UpdateEnvironment { ActiveHours = new ActiveHours(8, 22), WindowsUpdateRebootRequired = true };

        var forecast = UpdateRebootAnalyzer.Analyze([], environment, Evening);

        Assert.Equal(ForecastState.Planned, forecast.State);
        Assert.Equal(new DateTime(2026, 10, 23, 22, 0, 0), forecast.EarliestReboot);
        Assert.Contains(forecast.Notes, n => n.Kind == ForecastNoteKind.RebootRequired);
    }

    [Fact]
    public void SchedulerTasks_TakePriorityOverLog()
    {
        var environment = new UpdateEnvironment
        {
            ComponentServicingRebootPending = true,
            ScheduledTasks = new Dictionary<string, DateTime?> { ["Schedule Wake To Work"] = Evening.AddMinutes(40), ["Reboot_AC"] = null },
        };

        var forecast = UpdateRebootAnalyzer.Analyze([], environment, Evening);

        Assert.Equal(ForecastState.Imminent, forecast.State);
        Assert.Equal(Evening.AddMinutes(40), forecast.EarliestReboot);
        Assert.Single(forecast.Schedule);
    }

    [Theory]
    [InlineData(7, 1, 0, true)]
    [InlineData(7, 1, 1, false)]
    [InlineData(7, 1, 6, false)]
    [InlineData(7, 1, 7, true)]
    [InlineData(7, 1, 23, true)]
    [InlineData(8, 17, 12, true)]
    [InlineData(8, 17, 17, false)]
    public void ActiveHours_HandleMidnight(int start, int end, int hour, bool active) =>
        Assert.Equal(active, new ActiveHours(start, end).IsActive(Evening.Date.AddHours(hour)));

    [Fact]
    public void ActiveHours_NextOutsideCrossesMidnight() =>
        Assert.Equal(new DateTime(2026, 10, 24, 1, 0, 0), new ActiveHours(7, 1).NextOutside(Evening));

    [Theory]
    [InlineData("2026-10-24T01:30:14.514867000+02:59", "2026-10-24T01:30:14.5148670")]
    [InlineData("2026-10-24T01:15:00.000Z", "2026-10-24T01:15:00.0000000")]
    [InlineData("1899-12-30T00:00:00.000Z", null)]
    [InlineData("garbage", null)]
    public void LocalTime_IgnoresBrokenOffsets(string raw, string? expected) =>
        Assert.Equal(expected, UsoXmlParser.ParseLocalTime(raw)?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));

    [Fact]
    public void Parser_FlattensComplexData()
    {
        const string xml = """
            <Events><Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System><Provider Name="Microsoft.Windows.Update.Orchestrator.Decisions" /><TimeCreated SystemTime="2026-10-24T01:30:14.514867000+02:59" /></System>
              <EventData>
                <Data Name="Question">Start action</Data>
                <ComplexData Name="update"><Data Name="uniqueId">3b096b01-5676-4a86-b8f1-1e0ca38ed902:1</Data></ComplexData>
              </EventData>
              <RenderingInfo Culture="en-FI"><Task>Decision</Task></RenderingInfo>
            </Event></Events>
            """;

        var e = Assert.Single(UsoXmlParser.Parse(xml));

        Assert.Equal("Decision", e.Name);
        Assert.Equal("Start action", e.Get("Question"));
        Assert.Equal("3b096b01-5676-4a86-b8f1-1e0ca38ed902:1", e.Get("update.uniqueId"));
    }
}
