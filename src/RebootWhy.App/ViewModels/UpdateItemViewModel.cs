using RebootWhy.App.Properties;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;
using CoreStrings = RebootWhy.Core.Properties.Strings;

namespace RebootWhy.App.ViewModels;

/// <summary>Whether the installation will require a restart, according to the update itself.</summary>
public enum RebootNeed
{
    /// <summary>Unknown: Windows does not keep this for installed updates.</summary>
    Unknown,
    Never,
    Maybe,
    Always,
}

/// <summary>A row of the Updates tab: an installation history entry or an update that is still waiting.</summary>
public sealed class UpdateItemViewModel
{
    public static string PendingGroup => Strings.Updates_GroupPending;

    public static string HistoryGroup => Strings.Updates_GroupHistory;

    private UpdateItemViewModel(string key, string group, string timeText, string title, string status, string severity, string kind,
        IReadOnlyList<Fact> facts, string? description, bool isDefinitionUpdate, DateTime sortTime, RebootNeed reboot = RebootNeed.Unknown)
    {
        Key = key;
        Reboot = reboot;
        Group = group;
        TimeText = timeText;
        Title = title;
        StatusText = status;
        Severity = severity;
        KindText = kind;
        Facts = facts;
        Description = description;
        IsDefinitionUpdate = isDefinitionUpdate;
        SortTime = sortTime;
    }

    /// <summary>Identifies the row so the selection survives list rebuilds.</summary>
    public string Key { get; }

    public string Group { get; }

    public RebootNeed Reboot { get; }

    public bool NeedsReboot => Reboot is RebootNeed.Always or RebootNeed.Maybe;

    public string RebootText => Reboot switch
    {
        RebootNeed.Always => Strings.Updates_RebootAlways,
        RebootNeed.Maybe => Strings.Updates_RebootMaybe,
        RebootNeed.Never => Strings.Updates_RebootNever,
        _ => string.Empty,
    };

    public string RebootHint => Reboot switch
    {
        RebootNeed.Always => Strings.Updates_RebootAlwaysHint,
        RebootNeed.Maybe => Strings.Updates_RebootMaybeHint,
        RebootNeed.Never => Strings.Updates_RebootNeverHint,
        _ => string.Empty,
    };

    public string TimeText { get; }

    public string Title { get; }

    public string StatusText { get; }

    /// <summary>Normal, Warning, Critical or Info: the color of the label.</summary>
    public string Severity { get; }

    public string KindText { get; }

    public IReadOnlyList<Fact> Facts { get; }

    public string? Description { get; }

    public bool HasDescription => Description is not null;

    public bool IsDefinitionUpdate { get; }

    public DateTime SortTime { get; }

    public string ToClipboardText() => UpdateText.Build(Title, Facts, Description);

    /// <summary>Every word of the query occurs in the title, type, state, description or facts.</summary>
    public bool Matches(IReadOnlyList<string> words) => words.All(word =>
        Title.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || KindText.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || StatusText.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || RebootText.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || TimeText.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || (Description?.Contains(word, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || Facts.Any(f => f.Value.Contains(word, StringComparison.CurrentCultureIgnoreCase)));

    public static UpdateItemViewModel FromHistory(InstalledUpdate update) => new(
        $"h|{update.UpdateId}:{update.Revision}|{update.TimeLocal:O}|{update.Result}",
        HistoryGroup,
        Display.LocalDateTime(new DateTimeOffset(update.TimeLocal)),
        update.Title,
        UpdateText.Result(update.Result),
        update.Result switch
        {
            UpdateResult.Succeeded => "Normal",
            UpdateResult.Failed or UpdateResult.Aborted => "Critical",
            UpdateResult.SucceededWithErrors => "Warning",
            _ => "Info",
        },
        Kind(update.Categories, update.Title),
        UpdateText.Facts(update),
        update.Description,
        update.IsDefinitionUpdate,
        update.TimeLocal);

    public static UpdateItemViewModel FromAvailable(AvailableUpdate update, bool waitsForReboot) => new(
        $"a|{update.UpdateId}:{update.Revision}|{waitsForReboot}|{update.IsDownloaded}",
        PendingGroup,
        update.Published is { } published ? Display.Date(published) : "—",
        update.Title,
        waitsForReboot ? Strings.Updates_StateWaitsForReboot : update.IsDownloaded ? Strings.Updates_StateDownloaded : Strings.Updates_StateNotDownloaded,
        waitsForReboot || update.RebootBehavior == UpdateRebootBehavior.AlwaysRequiresReboot ? "Critical" : "Warning",
        update.IsDriver ? Strings.Updates_KindDriver : Kind(update.Categories, update.Title),
        UpdateText.Facts(update, waitsForReboot),
        update.Description,
        update.IsDefinitionUpdate,
        DateTime.MaxValue,
        waitsForReboot ? RebootNeed.Always : update.RebootBehavior switch
        {
            UpdateRebootBehavior.AlwaysRequiresReboot => RebootNeed.Always,
            UpdateRebootBehavior.CanRequestReboot => RebootNeed.Maybe,
            _ => RebootNeed.Never,
        });

    /// <summary>An update the USO log says is waiting for a restart but that the Windows Update search did not return.</summary>
    public static UpdateItemViewModel FromForecast(PendingUpdate update) => new(
        $"f|{update.Id}",
        PendingGroup,
        update.PendingSince is { } since ? Display.Format(Strings.Updates_PendingSince, Display.ShortDateTime(since)) : "—",
        ForecastText.UpdateTitle(update),
        Strings.Updates_StateWaitsForReboot,
        "Critical",
        ForecastText.UpdateType(update),
        [
            new Fact(CoreStrings.UpdateFact_State, CoreStrings.AvailableState_WaitsForReboot),
            new Fact(CoreStrings.UpdateFact_Type, ForecastText.UpdateType(update)),
            new Fact(CoreStrings.UpdateFact_Identifier, update.Id),
        ],
        null,
        false,
        DateTime.MaxValue,
        RebootNeed.Always);

    /// <summary>A short type derived from the title: Windows Update categories can be odd (MSRT is in "EU Browser Choice").</summary>
    private static string Kind(IReadOnlyList<string> categories, string title) =>
        UpdateCatalog.IsDefinitionUpdate(title) ? Strings.Updates_KindDefinitions
        : UpdateCatalog.IsRoutine(title) ? Strings.Updates_KindStoreApp
        : title.Contains("Driver", StringComparison.OrdinalIgnoreCase) ? Strings.Updates_KindDriver
        : title.Contains(".NET", StringComparison.OrdinalIgnoreCase) ? ".NET"
        : title.Contains("Malicious Software Removal", StringComparison.OrdinalIgnoreCase) ? Strings.Updates_KindMsrt
        : title.Contains("Defender", StringComparison.OrdinalIgnoreCase) || title.Contains("Windows Security", StringComparison.OrdinalIgnoreCase) ? Strings.Updates_KindDefender
        : title.Contains("Security Update", StringComparison.OrdinalIgnoreCase) || title.Contains("Cumulative", StringComparison.OrdinalIgnoreCase) ? Strings.Updates_KindWindows
        : categories.FirstOrDefault() ?? Strings.Updates_KindGeneric;
}
