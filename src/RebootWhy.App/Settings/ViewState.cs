using System.IO;
using System.Text.Json;

namespace RebootWhy.App.Settings;

/// <summary>
/// How the user set up the view: period, filters, column widths, window size.
/// Stored separately from the settings (%AppData%\RebootWhy\view.json) so that the settings window does not overwrite it.
/// </summary>
public sealed class ViewState
{
    public int PeriodDays { get; set; } = 30;

    public bool OnlyFullBoots { get; set; }

    public bool OnlyImportantUpdates { get; set; } = true;

    public bool OnlyRebootUpdates { get; set; }

    /// <summary>Column widths per table; if set, the user changed them and auto-fit is not needed.</summary>
    public Dictionary<string, double[]> ColumnWidths { get; set; } = [];

    /// <summary>Window position and size in the normal (not maximized) state, in screen pixels.</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>The monitor the window was on (for example, \\.\DISPLAY2), for reference; restored by coordinates.</summary>
    public string? WindowMonitor { get; set; }
}

public static class ViewStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RebootWhy", "view.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ViewState Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ViewState>(File.ReadAllText(FilePath), Options) ?? new ViewState()
                : new ViewState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ViewState();
        }
    }

    public static void Save(ViewState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The view was not saved; no reason to interrupt the work.
        }
    }
}
