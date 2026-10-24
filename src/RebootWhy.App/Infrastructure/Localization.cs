using System.Globalization;

namespace RebootWhy.App;

/// <summary>Selects the UI language. Strings come from Properties\Strings*.resx of the app and of RebootWhy.Core.</summary>
internal static class Localization
{
    /// <summary>The setting value meaning "same as the Windows display language".</summary>
    public const string SystemLanguage = "";

    /// <summary>The language of settings saved before the program was localized.</summary>
    public const string LegacyLanguage = "ru";

    public const string Fallback = "en";

    /// <summary>Languages the program is translated into, in the order shown in the settings.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "ru", "uk", "hy"];

    /// <summary>The Windows display language at startup, before <see cref="Apply"/> changed it.</summary>
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;

    public static string Resolve(string? language)
    {
        if (!string.IsNullOrEmpty(language) && Supported.Contains(language))
        {
            return language;
        }

        var windows = WindowsCulture.TwoLetterISOLanguageName;
        return Supported.Contains(windows) ? windows : Fallback;
    }

    public static void Apply(string? language) => Use(CultureInfo.GetCultureInfo(Resolve(language)));

    public static void UseEnglish() => Use(CultureInfo.GetCultureInfo(Fallback));

    /// <summary>Name of a language in that language, for the language picker.</summary>
    public static string NativeName(string language)
    {
        var name = CultureInfo.GetCultureInfo(language).NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(language)) + name[1..] : language;
    }

    private static void Use(CultureInfo culture)
    {
        // Only the UI language changes: number and date formats are chosen explicitly where they are shown.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
