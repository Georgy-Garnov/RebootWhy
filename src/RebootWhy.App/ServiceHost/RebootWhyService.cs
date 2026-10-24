using System.Diagnostics;
using System.ServiceProcess;
using RebootWhy.App.Platform;
using RebootWhy.Core.Monitoring;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.ServiceHost;

/// <summary>
/// The service: watches Windows Update as SYSTEM (including Task Scheduler, unavailable without admin rights)
/// and sends the forecast to tray programs over a named pipe.
/// </summary>
internal sealed class RebootWhyService : ServiceBase
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(15);

    private CancellationTokenSource? _stop;
    private Task? _run;

    public RebootWhyService()
    {
        ServiceName = ServiceSetup.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        _stop = new CancellationTokenSource();
        var monitor = new UpdateRebootMonitor();
        var simulation = new RebootSimulation(monitor.AnalyzeNow);
        var engine = new ForecastEngine(simulation.Analyze, Period, Debounce, UsoLogSource.DefaultDirectory);
        _run = Task.Run(() => new ForecastServer(engine, simulation).RunAsync(_stop.Token));
        _run.ContinueWith(
            task =>
            {
                EventLog.WriteEntry($"Monitoring stopped because of an error: {task.Exception?.GetBaseException()}", EventLogEntryType.Error);
                Stop();
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    protected override void OnStop() => StopMonitoring();

    protected override void OnShutdown() => StopMonitoring();

    private void StopMonitoring()
    {
        _stop?.Cancel();
        try
        {
            _run?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // The error has already been written to the event log.
        }
    }
}
