using System.Collections.Concurrent;
using System.Runtime.Versioning;
using RebootWhy.Core.Monitoring;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Tests;

[SupportedOSPlatform("windows")]
public class ForecastChannelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ManualCheck_AnswersRequester_AndEveryCheckReachesAllSubscribers()
    {
        var pipeName = $"RebootWhy.Test.{Guid.NewGuid():N}";
        var state = ForecastState.None;
        var simulation = new RebootSimulation(() => Forecast(state));
        var engine = new ForecastEngine(simulation.Analyze, period: TimeSpan.FromHours(1), debounce: TimeSpan.Zero, watchDirectory: null);
        using var stop = new CancellationTokenSource();
        var server = new ForecastServer(engine, simulation, pipeName).RunAsync(stop.Token);

        var (requester, requesterInbox, requesterRun) = await ConnectAsync(pipeName, stop.Token);
        var (_, observerInbox, observerRun) = await ConnectAsync(pipeName, stop.Token);
        var first = Receive(requesterInbox, f => f.State == ForecastState.None);
        Receive(observerInbox, f => f.State == ForecastState.None);

        // Nothing changed, but everyone must get the new check time; otherwise it looks as if nothing was checked for a long time.
        var checkedAt = DateTime.Now;
        var unchanged = await requester.AnalyzeAsync(Timeout);
        Assert.NotNull(unchanged);
        Assert.True(unchanged.AnalyzedAt >= checkedAt);
        Receive(observerInbox, f => f.State == ForecastState.None && f.AnalyzedAt >= checkedAt);

        state = ForecastState.Imminent;
        var changed = await requester.AnalyzeAsync(Timeout);
        Assert.Equal(ForecastState.Imminent, changed!.State);
        var pushed = Receive(observerInbox, f => f.State == ForecastState.Imminent);
        Assert.Equal("package with \"quotes\" and «guillemets»", Assert.Single(pushed.PendingUpdates).Title);

        // The reaction test is started and stopped through the same service.
        state = ForecastState.None;
        Assert.True(await requester.StartSimulationAsync());
        var simulated = Receive(observerInbox, f => f.IsSimulation);
        Assert.Equal(RebootSimulation.UpdateTitle, Assert.Single(simulated.PendingUpdates).Title);
        Assert.True(await requester.StopSimulationAsync());
        Receive(observerInbox, f => !f.IsSimulation && f.State == ForecastState.None);

        await stop.CancelAsync();
        await Task.WhenAll(server, requesterRun, observerRun).WaitAsync(Timeout);
        Assert.True(first.AnalyzedAt <= checkedAt);
    }

    [Fact]
    public async Task WithoutService_ClientReportsDisconnected()
    {
        var client = new ForecastClient($"RebootWhy.Missing.{Guid.NewGuid():N}");
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionChanged += value => connected.TrySetResult(value);
        using var stop = new CancellationTokenSource();
        var run = client.RunAsync(stop.Token);

        Assert.False(await connected.Task.WaitAsync(Timeout));
        Assert.Null(await client.AnalyzeAsync(TimeSpan.FromSeconds(1)));
        Assert.False(await client.StartSimulationAsync());

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    /// <summary>Waits for a message with the expected content: broadcasts and replies may arrive interleaved.</summary>
    private static RebootForecast Receive(BlockingCollection<RebootForecast> inbox, Func<RebootForecast, bool> match)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (inbox.TryTake(out var forecast, deadline - DateTime.UtcNow) && match(forecast))
            {
                return forecast;
            }
        }

        throw new Xunit.Sdk.XunitException("The expected message from the service did not arrive");
    }

    private static async Task<(ForecastClient Client, BlockingCollection<RebootForecast> Inbox, Task Run)> ConnectAsync(string pipeName, CancellationToken stop)
    {
        var client = new ForecastClient(pipeName);
        var inbox = new BlockingCollection<RebootForecast>();
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ForecastReceived += inbox.Add;
        client.ConnectionChanged += value => connected.TrySetResult(value);
        var run = client.RunAsync(stop);
        Assert.True(await connected.Task.WaitAsync(Timeout));
        return (client, inbox, run);
    }

    private static RebootForecast Forecast(ForecastState state) => new()
    {
        State = state,
        AnalyzedAt = DateTime.Now,
        PendingUpdates = state == ForecastState.None ? [] : [new PendingUpdate("id:1", "package with \"quotes\" and «guillemets»", "Driver", DateTime.Today, false)],
    };
}
