using System.Runtime.Versioning;

namespace RebootWhy.Core.Updates;

/// <summary>Full analysis cycle: USO logs + system state → forecast. Used by the service and by the tray app.</summary>
[SupportedOSPlatform("windows")]
public sealed class UpdateRebootMonitor
{
    private static readonly TimeSpan LogLookback = TimeSpan.FromDays(2);

    private readonly UsoLogSource _logs = new();

    public RebootForecast AnalyzeNow()
    {
        var now = DateTime.Now;
        var (environment, environmentProblems) = WindowsUpdateEnvironmentReader.Read();
        var since = environment.LastBootLocal is { } boot && boot > now - LogLookback ? boot : now - LogLookback;
        var (events, logProblems) = _logs.Read(since);
        return UpdateRebootAnalyzer.Analyze(events, environment, now, [.. environmentProblems, .. logProblems]);
    }
}
