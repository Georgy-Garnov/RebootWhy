using RebootWhy.Core.Monitoring;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Tests;

public class RebootSimulationTests
{
    private static RebootForecast Real() => new() { State = ForecastState.None, AnalyzedAt = DateTime.Now, ScheduleFromTaskScheduler = true };

    [Fact]
    public void Events_GoThroughRealAnalyzer_AsPendingDriverWithScheduledReboot()
    {
        var start = new DateTime(2026, 10, 24, 14, 0, 0);

        var forecast = UpdateRebootAnalyzer.Analyze(RebootSimulation.Events(start), new UpdateEnvironment(), start);

        Assert.Equal(ForecastState.Imminent, forecast.State);
        var update = Assert.Single(forecast.PendingUpdates);
        Assert.Equal(RebootSimulation.UpdateTitle, update.Title);
        Assert.Equal("Driver", update.Type);
        Assert.Equal("RebootWhy test update (driver)", ForecastText.UpdateTitle(update));
        Assert.Equal(start + RebootSimulation.RebootIn, forecast.EarliestReboot);
        Assert.Contains(forecast.Notes, n => n.Kind == ForecastNoteKind.ApprovedEarlier && n.Subject == update.Id);
    }

    [Fact]
    public void Simulation_IsMarked_AndEndsByItself()
    {
        var simulation = new RebootSimulation(Real);
        var engine = new ForecastEngine(simulation.Analyze, TimeSpan.FromHours(1), TimeSpan.Zero, null);
        simulation.Start(engine);
        var start = DateTime.Now;

        var during = simulation.Analyze(start);
        var afterRebootTime = simulation.Analyze(start + RebootSimulation.RebootIn + TimeSpan.FromSeconds(30));
        var after = simulation.Analyze(start + RebootSimulation.Duration + TimeSpan.FromSeconds(5));

        Assert.True(during.IsSimulation);
        Assert.StartsWith("Test: Windows may restart the computer within the hour", ForecastText.Summary(during));
        Assert.StartsWith("This is a RebootWhy reaction test", ForecastText.Notes(during)[0]);
        Assert.True(afterRebootTime.IsSimulation);
        Assert.True(afterRebootTime.EarliestReboot <= start + RebootSimulation.RebootIn + TimeSpan.FromSeconds(30));
        Assert.False(after.IsSimulation);
        Assert.True(after.ScheduleFromTaskScheduler);
        Assert.Null(simulation.EndsAt);
    }

    [Fact]
    public void Stop_ReturnsToRealAnalysis()
    {
        var simulation = new RebootSimulation(Real);
        var engine = new ForecastEngine(simulation.Analyze, TimeSpan.FromHours(1), TimeSpan.Zero, null);
        simulation.Start(engine);

        simulation.Stop(engine);

        Assert.False(simulation.Analyze().IsSimulation);
    }
}
