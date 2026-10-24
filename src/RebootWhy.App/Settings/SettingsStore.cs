using System.IO;
using System.Text.Json;

namespace RebootWhy.App.Settings;

/// <summary>User settings: %AppData%\RebootWhy\settings.json.</summary>
public static class SettingsStore
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RebootWhy", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) is { } saved)
            {
                // Versions before localization were Russian-only: keep the language their users already had.
                saved.Language ??= Localization.LegacyLanguage;
                return saved;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new AppSettings { Language = Localization.SystemLanguage };
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
