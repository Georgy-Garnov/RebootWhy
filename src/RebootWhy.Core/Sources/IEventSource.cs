namespace RebootWhy.Core.Sources;

public interface IEventSource
{
    /// <summary>Reads the events needed for the analysis.</summary>
    /// <param name="sinceUtc">Start of the period; null means all events of the source.</param>
    IReadOnlyList<SystemEvent> ReadEvents(DateTimeOffset? sinceUtc);
}
