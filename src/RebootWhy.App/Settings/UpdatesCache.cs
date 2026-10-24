using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.Settings;

/// <summary>The last update history read, so that the tab opens instantly while fresh data loads in the background.</summary>
public sealed record UpdatesSnapshot(DateTime SavedAt, List<InstalledUpdate> History, List<AvailableUpdate> Available);

/// <summary>%LocalAppData%\RebootWhy\updates-cache.json: data of this computer, so not in the roaming profile.</summary>
public static class UpdatesCache
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RebootWhy", "updates-cache.json");

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static UpdatesSnapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<UpdatesSnapshot>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // A broken or outdated cache is simply not used; the data will be read again.
            return null;
        }
    }

    public static void Save(UpdatesSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The program works without the cache; the tab just opens more slowly.
        }
    }
}
