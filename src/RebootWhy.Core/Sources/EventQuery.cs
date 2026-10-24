using System.Globalization;

namespace RebootWhy.Core.Sources;

/// <summary>
/// XPath query to the log. Filters by providers and time only and selects IDs in code:
/// Windows event log XPath limits the number of conditions in a query.
/// </summary>
public static class EventQuery
{
    public static string Build(DateTimeOffset? sinceUtc)
    {
        var providers = string.Join(" or ", Providers.All.Select(p => $"@Name='{p}'"));
        var time = sinceUtc is { } since
            ? $" and TimeCreated[@SystemTime>='{since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}']"
            : string.Empty;
        return $"*[System[Provider[{providers}]{time}]]";
    }
}
