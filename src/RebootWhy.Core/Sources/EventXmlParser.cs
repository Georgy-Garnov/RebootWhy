using System.Globalization;
using System.Xml.Linq;

namespace RebootWhy.Core.Sources;

/// <summary>Parses event XML (EventRecord.ToXml()); the format is the same for the live log and .evtx files.</summary>
public static class EventXmlParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static SystemEvent Parse(string xml)
    {
        var root = XElement.Parse(xml);
        var system = root.Element(Ns + "System") ?? throw new FormatException("The event XML has no System element.");

        var provider = (string?)system.Element(Ns + "Provider")?.Attribute("Name") ?? string.Empty;
        var id = int.TryParse(system.Element(Ns + "EventID")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)
            ? parsedId
            : throw new FormatException("The event XML has no EventID.");
        var level = int.TryParse(system.Element(Ns + "Level")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel)
            ? parsedLevel
            : 0;
        var recordId = long.TryParse(system.Element(Ns + "EventRecordID")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRecordId)
            ? parsedRecordId
            : 0;
        var time = EventValue.ParseTimestamp((string?)system.Element(Ns + "TimeCreated")?.Attribute("SystemTime"))
            ?? throw new FormatException("The event XML has no TimeCreated.");

        return new SystemEvent(provider, id, time, level, recordId, ReadData(root));
    }

    private static Dictionary<string, string> ReadData(XElement root)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var eventData = root.Element(Ns + "EventData");
        if (eventData is not null)
        {
            var index = 0;
            foreach (var item in eventData.Elements(Ns + "Data"))
            {
                // Classic providers (for example, EventLog 6008) write unnamed fields.
                var name = (string?)item.Attribute("Name");
                data[string.IsNullOrEmpty(name) ? $"Data{index}" : name] = item.Value;
                index++;
            }
        }

        // Some providers write fields to UserData with their own schema; take leaf elements by local name.
        var userData = root.Element(Ns + "UserData");
        if (userData is not null)
        {
            foreach (var leaf in userData.Descendants().Where(e => !e.HasElements))
            {
                data.TryAdd(leaf.Name.LocalName, leaf.Value);
            }
        }

        return data;
    }
}
