namespace RebootWhy.Core;

/// <summary>
/// A Windows event log record in normalized form: system fields and EventData.
/// Message texts are not used: they are localized and depend on the system language.
/// </summary>
public sealed record SystemEvent(
    string Provider,
    int Id,
    DateTimeOffset TimeUtc,
    int Level,
    long RecordId,
    IReadOnlyDictionary<string, string> Data)
{
    public string? Get(string name) => Data.TryGetValue(name, out var value) ? value : null;

    public long? GetInt64(string name) => EventValue.ParseInt64(Get(name));

    public bool IsFrom(string provider) => string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);

    public bool Is(string provider, int id) => Id == id && IsFrom(provider);
}
