using System.Text;
using RebootWhy.Core.Properties;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Text;

/// <summary>Localized labels and clipboard text for Windows updates.</summary>
public static class UpdateText
{
    /// <summary>Error codes that have an explanation in the resources (UpdateError_XXXXXXXX).</summary>
    private static readonly HashSet<uint> KnownErrors =
    [
        0x80070002, 0x80070005, 0x80070643, 0x80073712, 0x800F081F, 0x800F0922, 0x80240016,
        0x80240017, 0x8024001E, 0x80240022, 0x8024200D, 0x80242016, 0x8024402C, 0x80246007,
    ];

    public static string Result(UpdateResult result) => result switch
    {
        UpdateResult.Succeeded => Strings.UpdateResult_Succeeded,
        UpdateResult.SucceededWithErrors => Strings.UpdateResult_SucceededWithErrors,
        UpdateResult.Failed => Strings.UpdateResult_Failed,
        UpdateResult.Aborted => Strings.UpdateResult_Aborted,
        UpdateResult.InProgress => Strings.UpdateResult_InProgress,
        _ => Strings.UpdateResult_NotStarted,
    };

    public static string Operation(UpdateOperation operation) => operation switch
    {
        UpdateOperation.Installation => Strings.UpdateOperation_Installation,
        UpdateOperation.Uninstallation => Strings.UpdateOperation_Uninstallation,
        _ => Strings.UpdateOperation_Other,
    };

    public static string? Error(int hresult)
    {
        if (hresult == 0)
        {
            return null;
        }

        var code = unchecked((uint)hresult);
        return KnownErrors.Contains(code) && Strings.ResourceManager.GetString($"UpdateError_{code:X8}", Strings.Culture) is { } text
            ? Display.Format(Strings.UpdateError_WithText, code, text)
            : $"0x{code:X8}";
    }

    public static string Initiator(string? clientApplication) => clientApplication switch
    {
        null => Strings.Initiator_NotSpecified,
        "MoUpdateOrchestrator" or "UpdateOrchestrator" or "MoUsoCoreWorker" => Strings.Initiator_WindowsUpdate,
        "Windows Defender" => "Microsoft Defender",
        "AutomaticUpdates" or "AutomaticUpdatesWuApp" => Strings.Initiator_AutomaticUpdates,
        "Microsoft Store" or "WindowsStore" => "Microsoft Store",
        _ => clientApplication,
    };

    public static string RebootBehavior(UpdateRebootBehavior behavior) => behavior switch
    {
        UpdateRebootBehavior.AlwaysRequiresReboot => Strings.RebootBehavior_Always,
        UpdateRebootBehavior.CanRequestReboot => Strings.RebootBehavior_Maybe,
        _ => Strings.RebootBehavior_Never,
    };

    /// <summary>Update type as reported by Update Orchestrator ("Driver", "FeatureUpdate", ...).</summary>
    public static string UpdateType(string? type) => type switch
    {
        "Driver" => Strings.UpdateType_Driver,
        "FeatureUpdate" => Strings.UpdateType_Feature,
        "Quality" or "QualityUpdate" => Strings.UpdateType_Quality,
        null or "" => Strings.UpdateType_Generic,
        _ => type,
    };

    public static IReadOnlyList<Fact> Facts(InstalledUpdate update)
    {
        var facts = new List<Fact>
        {
            new(Strings.UpdateFact_Date, Display.LocalDateTime(new DateTimeOffset(update.TimeLocal), withSeconds: true)),
            new(Strings.UpdateFact_Result, Display.Format(Strings.UpdateFact_ResultValue, Result(update.Result), Operation(update.Operation))),
        };
        if (Error(update.HResult) is { } error)
        {
            facts.Add(new(Strings.UpdateFact_ErrorCode, error));
        }

        if (update.KbArticles.Count > 0)
        {
            facts.Add(new(Strings.UpdateFact_Article, string.Join(", ", update.KbArticles)));
        }

        if (update.Categories.Count > 0)
        {
            facts.Add(new(Strings.UpdateFact_Category, string.Join(", ", update.Categories)));
        }

        facts.Add(new(Strings.UpdateFact_Initiator, Initiator(update.Initiator)));
        facts.Add(new(Strings.UpdateFact_Identifier, Display.Format(Strings.UpdateFact_IdentifierValue, update.UpdateId, update.Revision)));
        if (update.SupportUrl is { } url)
        {
            facts.Add(new(Strings.UpdateFact_MoreInfo, url));
        }

        return facts;
    }

    public static IReadOnlyList<Fact> Facts(AvailableUpdate update, bool waitsForReboot)
    {
        var facts = new List<Fact>
        {
            new(Strings.UpdateFact_State, AvailableState(update, waitsForReboot)),
            new(Strings.UpdateFact_Type, update.IsDriver ? Strings.UpdateType_Driver : Strings.UpdateType_Software),
            new(Strings.UpdateFact_Restart, RebootBehavior(update.RebootBehavior)),
        };
        if (update.Published is { } published)
        {
            facts.Add(new(Strings.UpdateFact_Published, Display.Date(published)));
        }

        if (update.KbArticles.Count > 0)
        {
            facts.Add(new(Strings.UpdateFact_Article, string.Join(", ", update.KbArticles)));
        }

        if (update.Severity is { } severity)
        {
            facts.Add(new(Strings.UpdateFact_Severity, severity));
        }

        if (update.Categories.Count > 0)
        {
            facts.Add(new(Strings.UpdateFact_Category, string.Join(", ", update.Categories)));
        }

        facts.Add(new(Strings.UpdateFact_Identifier, Display.Format(Strings.UpdateFact_IdentifierValue, update.UpdateId, update.Revision)));
        if (update.SupportUrl is { } url)
        {
            facts.Add(new(Strings.UpdateFact_MoreInfo, url));
        }

        return facts;
    }

    public static string AvailableState(AvailableUpdate update, bool waitsForReboot) =>
        waitsForReboot ? Strings.AvailableState_WaitsForReboot
        : update.IsDownloaded ? Strings.AvailableState_Downloaded
        : Strings.AvailableState_Known;

    public static string Build(string title, IReadOnlyList<Fact> facts, string? description)
    {
        var text = new StringBuilder();
        text.AppendLine(title);
        text.AppendLine();
        foreach (var fact in facts)
        {
            text.AppendLine($"{fact.Label}: {fact.Value}");
        }

        if (description is not null)
        {
            text.AppendLine();
            text.AppendLine($"{Strings.Copy_Description}:");
            text.AppendLine(description);
        }

        return text.ToString().TrimEnd();
    }
}
