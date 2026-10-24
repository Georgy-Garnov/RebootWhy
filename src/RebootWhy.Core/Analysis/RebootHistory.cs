using RebootWhy.Core.Sources;

namespace RebootWhy.Core.Analysis;

public sealed record RebootReport(IReadOnlyList<RebootRecord> Records, int EventCount);

public static class RebootHistory
{
    /// <param name="sinceUtc">Start of the period; null means the whole log.</param>
    public static RebootReport Load(IEventSource source, DateTimeOffset? sinceUtc)
    {
        var events = source.ReadEvents(sinceUtc);
        return new RebootReport(RebootAnalyzer.Analyze(events), events.Count);
    }
}
