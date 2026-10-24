using RebootWhy.Core.Properties;

namespace RebootWhy.Core.Text;

public sealed record Fact(string Label, string Value);

/// <summary>Record details as "label — value" pairs, shared by the window and the console.</summary>
public static class RecordFacts
{
    public static IReadOnlyList<Fact> Build(RebootRecord record)
    {
        var facts = new List<Fact>
        {
            new(Strings.Fact_Boot, $"{Display.LocalDateTime(record.BootTimeUtc, withSeconds: true)} · {Display.BootType(record.BootType)}"),
        };

        if (record.ShutdownTimeUtc is { } shutdown)
        {
            facts.Add(new(Strings.Fact_Shutdown, Display.LocalDateTime(shutdown, withSeconds: true)));
        }

        if (record.Initiator is { } initiator)
        {
            facts.Add(new(Strings.Fact_Initiator, initiator));
        }

        if (record.User is { } user)
        {
            facts.Add(new(Strings.Fact_User, user));
        }

        if (record.Reason is { } reason)
        {
            facts.Add(new(Strings.Fact_Details, reason));
        }

        facts.Add(new(Strings.Fact_Confidence, Display.Confidence(record.Confidence)));
        return facts;
    }
}
