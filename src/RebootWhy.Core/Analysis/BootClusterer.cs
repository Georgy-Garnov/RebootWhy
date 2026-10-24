namespace RebootWhy.Core.Analysis;

internal sealed record BootCluster(DateTimeOffset TimeUtc, BootType Type, IReadOnlyList<SystemEvent> Markers);

/// <summary>
/// Groups boot-start events (Kernel-General 12, Kernel-Boot 27, EventLog 6005) into individual boots.
/// </summary>
internal static class BootClusterer
{
    private static readonly TimeSpan ClusterWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromMinutes(10);

    public static List<BootCluster> Cluster(IReadOnlyList<SystemEvent> ordered)
    {
        var groups = new List<List<SystemEvent>>();
        foreach (var e in ordered.Where(IsBootMarker))
        {
            var current = groups.Count > 0 ? groups[^1] : null;

            // A repeated marker means a new boot, even if it happened quickly (boot loops).
            var belongsToCurrent = current is not null
                && e.TimeUtc - current[0].TimeUtc <= ClusterWindow
                && !current.Any(m => m.Id == e.Id && m.IsFrom(e.Provider));

            if (belongsToCurrent)
            {
                current!.Add(e);
            }
            else
            {
                groups.Add([e]);
            }
        }

        return groups.Select(ToCluster).ToList();
    }

    private static bool IsBootMarker(SystemEvent e) =>
        e.Is(Providers.KernelGeneral, EventCatalog.OsStarted)
        || e.Is(Providers.KernelBoot, EventCatalog.BootTypeReported)
        || e.Is(Providers.EventLog, EventCatalog.EventLogStarted);

    private static BootCluster ToCluster(List<SystemEvent> markers)
    {
        var time = markers.Min(m => m.TimeUtc);
        var startTime = EventValue.ParseTimestamp(
            markers.FirstOrDefault(m => m.Is(Providers.KernelGeneral, EventCatalog.OsStarted))?.Get("StartTime"));
        if (startTime is { } start && (time - start).Duration() <= StartTimeTolerance)
        {
            time = start;
        }

        var bootTypeEvent = markers.FirstOrDefault(m => m.Is(Providers.KernelBoot, EventCatalog.BootTypeReported));
        var type = EventCatalog.ToBootType(bootTypeEvent?.GetInt64("BootType"));

        // The event log service restarts only on a full boot; it keeps running through fast startup and resume from hibernation.
        if (type == BootType.Unknown && markers.Any(m => m.Is(Providers.EventLog, EventCatalog.EventLogStarted)))
        {
            type = BootType.Cold;
        }

        return new BootCluster(time, type, markers);
    }
}
