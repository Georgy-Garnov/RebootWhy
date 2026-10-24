using System.Text;
using RebootWhy.Core.Properties;

namespace RebootWhy.Core.Text;

/// <summary>Boot details as plain text for the clipboard.</summary>
public static class RecordText
{
    public static string Build(RebootRecord record)
    {
        var text = new StringBuilder();
        text.AppendLine(record.Title);
        text.AppendLine();
        foreach (var fact in RecordFacts.Build(record))
        {
            text.AppendLine($"{fact.Label}: {fact.Value}");
        }

        AppendList(text, Strings.Copy_Notes, record.Notes);
        AppendList(text, Strings.Copy_InstalledUpdates, record.Updates);

        if (record.Evidence.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"{Strings.Copy_Evidence}:");
            foreach (var e in record.Evidence)
            {
                text.AppendLine(EventDescriber.Line(e));
            }
        }

        return text.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder text, string header, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine($"{header}:");
        foreach (var item in items)
        {
            text.AppendLine($"• {item}");
        }
    }
}
