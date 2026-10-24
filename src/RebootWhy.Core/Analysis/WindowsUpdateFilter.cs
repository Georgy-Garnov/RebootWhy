using System.Text.RegularExpressions;

namespace RebootWhy.Core.Analysis;

/// <summary>Filters out updates that never require a restart.</summary>
public static partial class WindowsUpdateFilter
{
    public static bool MayRequireRestart(string title) =>
        !title.Contains("Security Intelligence Update", StringComparison.OrdinalIgnoreCase)
        && !title.Contains("KB2267602", StringComparison.OrdinalIgnoreCase)
        && !StorePackage().IsMatch(title);

    /// <summary>Microsoft Store apps: "9WZDNCRFJBMP-Microsoft.WindowsStore".</summary>
    [GeneratedRegex("^[0-9A-Z]{12}-")]
    private static partial Regex StorePackage();
}
