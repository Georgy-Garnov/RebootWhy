using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RebootWhy.Core.Updates;

/// <summary>Parses the XML tracerpt produces for USO (TraceLogging) logs.</summary>
public static partial class UsoXmlParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static List<UsoEvent> Parse(string xml)
    {
        var events = new List<UsoEvent>();
        foreach (var element in XDocument.Parse(xml).Descendants(Ns + "Event"))
        {
            var system = element.Element(Ns + "System");
            var time = ParseLocalTime((string?)system?.Element(Ns + "TimeCreated")?.Attribute("SystemTime"));
            var eventData = element.Element(Ns + "EventData");
            if (time is null || eventData is null)
            {
                continue;
            }

            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Flatten(eventData, prefix: null, data);
            events.Add(new UsoEvent(
                time.Value,
                (string?)system!.Element(Ns + "Provider")?.Attribute("Name") ?? string.Empty,
                element.Element(Ns + "RenderingInfo")?.Element(Ns + "Task")?.Value ?? string.Empty,
                data));
        }

        return events;
    }

    /// <summary>
    /// tracerpt writes local time with a distorted offset ("+02:59" instead of "+03:00"), so the offset is dropped.
    /// The same goes for fields like NextRuntimeLocalSystem: they hold local time with a wrong "Z" suffix.
    /// Values before 2000 (1899-12-30) mean "not set".
    /// </summary>
    public static DateTime? ParseLocalTime(string? raw)
    {
        var match = raw is null ? null : LocalTimestamp().Match(raw);
        if (match is null || !match.Success)
        {
            return null;
        }

        var fraction = match.Groups[2].Success ? match.Groups[2].Value[..Math.Min(8, match.Groups[2].Value.Length)] : string.Empty;
        var value = DateTime.ParseExact(match.Groups[1].Value, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        if (fraction.Length > 1)
        {
            value = value.AddTicks(long.Parse(fraction[1..].PadRight(7, '0'), CultureInfo.InvariantCulture));
        }

        return value.Year < 2000 ? null : value;
    }

    private static void Flatten(XElement parent, string? prefix, Dictionary<string, string> data)
    {
        foreach (var child in parent.Elements())
        {
            var name = (string?)child.Attribute("Name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var key = prefix is null ? name : $"{prefix}.{name}";
            if (child.Name.LocalName == "ComplexData")
            {
                Flatten(child, key, data);
            }
            else
            {
                data[key] = child.Value;
            }
        }
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(\.\d+)?")]
    private static partial Regex LocalTimestamp();
}
