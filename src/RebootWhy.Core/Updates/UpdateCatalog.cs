using System.Text.RegularExpressions;

namespace RebootWhy.Core.Updates;

public enum UpdateOperation
{
    Other = 0,
    Installation = 1,
    Uninstallation = 2,
}

/// <summary>OperationResultCode from the Windows Update Agent API.</summary>
public enum UpdateResult
{
    NotStarted = 0,
    InProgress = 1,
    Succeeded = 2,
    SucceededWithErrors = 3,
    Failed = 4,
    Aborted = 5,
}

/// <summary>InstallationBehavior.RebootBehavior.</summary>
public enum UpdateRebootBehavior
{
    NeverReboots = 0,
    AlwaysRequiresReboot = 1,
    CanRequestReboot = 2,
}

/// <summary>A Windows Update installation history entry.</summary>
public sealed record InstalledUpdate(
    DateTime TimeLocal,
    string Title,
    string? Description,
    UpdateOperation Operation,
    UpdateResult Result,
    int HResult,
    string? Initiator,
    string? SupportUrl,
    string UpdateId,
    int Revision,
    IReadOnlyList<string> Categories)
{
    public IReadOnlyList<string> KbArticles => UpdateCatalog.KbFromTitle(Title);

    public bool IsDefinitionUpdate => UpdateCatalog.IsDefinitionUpdate(Title);

    public bool IsRoutine => UpdateCatalog.IsRoutine(Title);
}

/// <summary>An update Windows already knows about but has not installed yet.</summary>
public sealed record AvailableUpdate(
    string UpdateId,
    int Revision,
    string Title,
    string? Description,
    bool IsDriver,
    bool IsDownloaded,
    bool IsMandatory,
    UpdateRebootBehavior RebootBehavior,
    IReadOnlyList<string> KbArticles,
    string? SupportUrl,
    DateTime? Published,
    string? Severity,
    IReadOnlyList<string> Categories)
{
    public bool IsDefinitionUpdate => UpdateCatalog.IsDefinitionUpdate(Title);

    public bool IsRoutine => UpdateCatalog.IsRoutine(Title);
}

public static partial class UpdateCatalog
{
    /// <summary>Housekeeping updates: antivirus definitions and Microsoft Store apps ("9WZDNCRFJBMP-Microsoft.WindowsStore").</summary>
    public static bool IsRoutine(string title) => IsDefinitionUpdate(title) || StorePackage().IsMatch(title);

    /// <summary>Microsoft Defender definitions arrive several times a day and clutter the history.</summary>
    public static bool IsDefinitionUpdate(string title) =>
        title.Contains("Security Intelligence Update", StringComparison.OrdinalIgnoreCase)
        || title.Contains("KB2267602", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> KbFromTitle(string title) =>
        KbNumber().Matches(title).Select(m => "KB" + m.Groups[1].Value).Distinct().ToList();

    [GeneratedRegex("^[0-9A-Z]{12}-")]
    private static partial Regex StorePackage();

    [GeneratedRegex(@"\bKB(\d{6,8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex KbNumber();
}
