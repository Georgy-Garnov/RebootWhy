using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;

namespace RebootWhy.Core.Sources;

/// <summary>The System log of this computer or an exported .evtx file.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEventLogSource : IEventSource
{
    private readonly string _path;
    private readonly PathType _pathType;

    private WindowsEventLogSource(string path, PathType pathType)
    {
        _path = path;
        _pathType = pathType;
    }

    public static WindowsEventLogSource ForLiveSystemLog() => new("System", PathType.LogName);

    public static WindowsEventLogSource ForFile(string path) => new(path, PathType.FilePath);

    public IReadOnlyList<SystemEvent> ReadEvents(DateTimeOffset? sinceUtc)
    {
        var query = new EventLogQuery(_path, _pathType, EventQuery.Build(sinceUtc));
        using var reader = new EventLogReader(query);

        var events = new List<SystemEvent>();
        while (reader.ReadEvent() is { } record)
        {
            using (record)
            {
                var parsed = EventXmlParser.Parse(record.ToXml());
                if (EventCatalog.IsRelevant(parsed))
                {
                    events.Add(parsed);
                }
            }
        }

        return events;
    }
}
