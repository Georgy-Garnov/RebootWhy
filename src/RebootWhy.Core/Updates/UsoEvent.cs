namespace RebootWhy.Core.Updates;

/// <summary>
/// An Update Orchestrator (USO) log event. Time is local: USO itself schedules tasks in local time.
/// Fields of nested blocks (ComplexData) are available as "block.field", for example "update.uniqueId".
/// </summary>
public sealed record UsoEvent(DateTime Time, string Provider, string Name, IReadOnlyDictionary<string, string> Data)
{
    public string? Get(string key) => Data.TryGetValue(key, out var value) ? value.Trim() : null;
}
