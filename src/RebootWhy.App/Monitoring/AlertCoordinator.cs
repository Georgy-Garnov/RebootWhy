using System.Windows.Threading;
using RebootWhy.App.Platform;
using RebootWhy.App.Properties;
using RebootWhy.App.Settings;
using RebootWhy.App.Tray;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.Monitoring;

/// <summary>
/// Reacts to the forecast according to the user's settings:
/// as soon as Windows starts waiting for a restart — a notification, a sound and (optionally) a topmost window;
/// when little time is left before a possible restart — the window once more;
/// "Remind me" — the window again after the chosen delay; "display required" — until the wait is over.
/// </summary>
public sealed class AlertCoordinator : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly TrayIcon _tray;
    private readonly Action<RebootForecast> _showAlertWindow;
    private readonly AlertSound _sound = new();
    private readonly UserPresenceKeeper _presence = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };

    private RebootForecast? _forecast;
    private string? _detectedEpisode;
    private string? _beforeRebootShownFor;

    public AlertCoordinator(Func<AppSettings> settings, TrayIcon tray, Action<RebootForecast> showAlertWindow)
    {
        _settings = settings;
        _tray = tray;
        _showAlertWindow = showAlertWindow;
        _clock.Tick += (_, _) => CheckTimers();
        _clock.Start();
        _sound.StateChanged += () => SoundStateChanged?.Invoke();
    }

    public event Action? SoundStateChanged;

    public bool IsSoundPlaying => _sound.IsPlaying;

    public void StopSound() => _sound.Stop();

    public event Action? ProtectionChanged;

    public bool IsKeepingUserActive => _presence.IsActive;

    public DateTime? SnoozedUntil { get; private set; }

    public void OnForecast(RebootForecast forecast)
    {
        _forecast = forecast;
        UpdatePresence();
        if (forecast.State == ForecastState.None)
        {
            _detectedEpisode = null;
            _beforeRebootShownFor = null;
            SnoozedUntil = null;
            return;
        }

        var episode = Episode(forecast);
        if (episode != _detectedEpisode)
        {
            _detectedEpisode = episode;
            SnoozedUntil = null;
            var settings = _settings();
            if (settings.ShowNotification)
            {
                _tray.ShowNotification(
                    forecast.IsSimulation ? Strings.Alert_NotifyTitleTest : Strings.Alert_NotifyTitle,
                    ForecastText.Summary(forecast));
            }

            if (settings.ShowAlertOnDetection)
            {
                // If the restart is already close, this window also replaces the "before the restart" one; do not show it twice in a row.
                if (IsWithinAlertWindow(forecast, settings))
                {
                    _beforeRebootShownFor = episode;
                }

                ShowAlert(forecast);
            }
            else
            {
                PlaySound();
            }
        }

        CheckTimers();
    }

    /// <summary>Show the window again after <paramref name="delay"/>.</summary>
    public void Snooze(TimeSpan delay) => SnoozedUntil = DateTime.Now + delay;

    public void OnSettingsChanged()
    {
        UpdatePresence();
        CheckTimers();
    }

    /// <returns>Error text if the file could not be played.</returns>
    public string? TestSound(string? file) => _sound.Play(file);

    /// <summary>Notification and sound for the sample alert, using the settings from the window even if unsaved.</summary>
    public void PlaySampleFeedback(AppSettings settings)
    {
        if (settings.ShowNotification)
        {
            _tray.ShowNotification(Strings.Alert_SampleTitle, Strings.Alert_SampleText);
        }

        if (settings.PlaySound)
        {
            _sound.Play(settings.SoundFile);
        }
    }

    public void Dispose()
    {
        _clock.Stop();
        _presence.Dispose();
        _sound.Dispose();
    }

    private void CheckTimers()
    {
        if (_forecast is not { State: not ForecastState.None } forecast)
        {
            return;
        }

        var now = DateTime.Now;
        if (SnoozedUntil is { } until)
        {
            if (now < until)
            {
                return;
            }

            SnoozedUntil = null;
            ShowAlert(forecast);
            return;
        }

        var settings = _settings();
        if (!settings.ShowAlertWindow || !IsWithinAlertWindow(forecast, settings))
        {
            return;
        }

        // One "before the restart" window per wait: after that "Remind me" takes over, otherwise the window would pop up after every check.
        var episode = Episode(forecast);
        if (episode != _beforeRebootShownFor)
        {
            _beforeRebootShownFor = episode;
            ShowAlert(forecast);
        }
    }

    private static bool IsWithinAlertWindow(RebootForecast forecast, AppSettings settings) =>
        forecast.EarliestReboot is { } earliest && earliest - DateTime.Now <= TimeSpan.FromMinutes(settings.AlertWindowMinutesBefore);

    private void ShowAlert(RebootForecast forecast)
    {
        // Window first: opening a new one closes the previous alert and its sound along with it.
        _showAlertWindow(forecast);
        PlaySound();
    }

    /// <summary>Every alert plays the sound (if enabled): a repeated window without sound is easy to miss.</summary>
    private void PlaySound()
    {
        var settings = _settings();
        if (settings.PlaySound)
        {
            _sound.Play(settings.SoundFile);
        }
    }

    private void UpdatePresence()
    {
        var wasActive = _presence.IsActive;
        if (_settings().KeepUserActive && _forecast is { State: not ForecastState.None })
        {
            _presence.Activate(Strings.Alert_PresenceReason);
        }
        else
        {
            _presence.Release();
        }

        if (wasActive != _presence.IsActive)
        {
            ProtectionChanged?.Invoke();
        }
    }

    private static string Episode(RebootForecast forecast) =>
        forecast.PendingUpdates.Count == 0 ? "reboot-flag" : string.Join(",", forecast.PendingUpdates.Select(u => u.Id).Order());
}
