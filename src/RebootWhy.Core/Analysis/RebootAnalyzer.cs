namespace RebootWhy.Core.Analysis;

public static class RebootAnalyzer
{
    /// <summary>Builds the boot history from System log events, newest first.</summary>
    public static IReadOnlyList<RebootRecord> Analyze(IEnumerable<SystemEvent> events)
    {
        var ordered = events.OrderBy(e => e.TimeUtc).ThenBy(e => e.RecordId).ToList();
        var boots = BootClusterer.Cluster(ordered);
        var claimedUpdates = new HashSet<long>();

        var records = new List<RebootRecord>(boots.Count);
        for (var i = 0; i < boots.Count; i++)
        {
            var previous = i > 0 ? boots[i - 1] : null;
            var next = i + 1 < boots.Count ? boots[i + 1] : null;
            records.Add(RebootClassifier.Classify(boots[i], previous, next, ordered, claimedUpdates));
        }

        records.Reverse();
        return records;
    }
}
