namespace RebootWhy.App.Settings;

/// <summary>How to warn about a restart planned by Windows Update.</summary>
public sealed class AppSettings
{
    public bool ShowNotification { get; set; } = true;

    /// <summary>A window on top as soon as it is detected that Windows is waiting for a restart.</summary>
    public bool ShowAlertOnDetection { get; set; } = true;

    /// <summary>A window on top once more when little time is left before a possible restart.</summary>
    public bool ShowAlertWindow { get; set; } = true;

    public int AlertWindowMinutesBefore { get; set; } = 60;

    public bool PlaySound { get; set; } = true;

    /// <summary>mp3, wav or ogg; empty means the Windows system sound.</summary>
    public string? SoundFile { get; set; }

    /// <summary>While Windows is waiting for a restart, hold a "display required" request so that Windows considers the user present.</summary>
    public bool KeepUserActive { get; set; } = true;

    /// <summary>
    /// UI language: "en", "ru", "uk", "hy"; an empty string follows the Windows display language.
    /// null only while reading settings saved by versions without this option (see <see cref="SettingsStore"/>).
    /// </summary>
    public string? Language { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public bool SameAs(AppSettings other) =>
        ShowNotification == other.ShowNotification
        && ShowAlertOnDetection == other.ShowAlertOnDetection
        && ShowAlertWindow == other.ShowAlertWindow
        && AlertWindowMinutesBefore == other.AlertWindowMinutesBefore
        && PlaySound == other.PlaySound
        && SoundFile == other.SoundFile
        && KeepUserActive == other.KeepUserActive
        && Language == other.Language;
}
