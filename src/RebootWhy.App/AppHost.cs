using System.ComponentModel;
using System.Windows;
using RebootWhy.App.Monitoring;
using RebootWhy.App.Properties;
using RebootWhy.App.Settings;
using RebootWhy.App.Tray;
using RebootWhy.App.ViewModels;
using RebootWhy.App.Views;
using RebootWhy.Core.Updates;

namespace RebootWhy.App;

/// <summary>Ties together the tray icon, Windows Update monitoring, alerts and the program windows.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly App _app;
    private readonly TrayIcon _tray = new();
    private readonly ForecastCoordinator _forecasts;
    private readonly AlertCoordinator _alerts;
    private AppSettings _settings = SettingsStore.Load();
    private readonly ViewState _view = ViewStateStore.Load();
    private MainWindow? _main;
    private ForecastWindow? _forecastWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isExiting;
    private bool _backgroundHintShown;

    public AppHost(App app)
    {
        _app = app;
        _forecasts = new ForecastCoordinator(app.Dispatcher);
        _alerts = new AlertCoordinator(() => _settings, _tray, forecast => ShowForecast(isAlert: true, forecast));

        _tray.OpenRequested += ShowMainWindow;
        _tray.AnalyzeRequested += () => _ = _forecasts.AnalyzeNowAsync();
        _tray.SimulationRequested += () => _ = _forecasts.StartSimulationAsync();
        _tray.SettingsRequested += ShowSettings;
        _tray.NotificationClicked += () =>
        {
            _alerts.StopSound();
            ShowForecast(isAlert: false);
        };
        _tray.ExitRequested += Exit;
        _tray.ExitAndStopServiceRequested += () => _ = ExitAndStopServiceAsync();
        _tray.StopSoundRequested += _alerts.StopSound;
        _alerts.SoundStateChanged += () => _tray.SetSoundPlaying(_alerts.IsSoundPlaying);

        _forecasts.ForecastUpdated += forecast =>
        {
            _alerts.OnForecast(forecast);
            _tray.Update(forecast, _alerts.IsKeepingUserActive);
            _forecastWindow?.ViewModel.SetForecast(forecast);
        };
        _alerts.ProtectionChanged += () => _tray.Update(_forecasts.Latest, _alerts.IsKeepingUserActive);
    }

    public void Start(bool startHidden)
    {
        // The service is useless without the program, and the program is more accurate with the service, so start it together with the program.
        Platform.ServiceSetup.StartIfStopped();
        _tray.Update(null, keepingUserActive: false);
        _forecasts.Start();
        if (!startHidden)
        {
            ShowMainWindow();
        }
    }

    public void ShowMainWindow()
    {
        // While the settings are open, work only with them; otherwise the settings window would get lost behind the main one.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        if (_main is null)
        {
            _main = new MainWindow(new MainViewModel(_forecasts, _alerts, _view, () => ShowForecast(isAlert: false), ShowSettings), _view);
            _main.Closing += OnMainClosing;
        }

        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Activate();
    }

    public void Dispose()
    {
        _alerts.Dispose();
        _forecasts.Dispose();
        _tray.Dispose();
    }

    /// <param name="forecast">What to show; the current forecast by default.</param>
    private void ShowForecast(bool isAlert, RebootForecast? forecast = null)
    {
        forecast ??= _forecasts.Latest;
        if (forecast is null)
        {
            return;
        }

        // An alert opens anew above all windows; regular viewing reuses the window that is already open.
        if (_forecastWindow is not null && (isAlert || _forecastWindow.ViewModel.IsAlert))
        {
            _forecastWindow.Close();
        }

        if (_forecastWindow is null)
        {
            var viewModel = new ForecastViewModel(forecast, isAlert, _forecasts, _alerts, ShowMainWindow, EnableProtection);
            _forecastWindow = new ForecastWindow(viewModel);
            _forecastWindow.Closed += (_, _) => _forecastWindow = null;
        }
        else
        {
            _forecastWindow.ViewModel.SetForecast(forecast);
        }

        _forecastWindow.Show();
        _forecastWindow.Activate();
    }

    private void EnableProtection()
    {
        _settings.KeepUserActive = true;
        SettingsStore.Save(_settings);
        _alerts.OnSettingsChanged();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(
            _settings,
            _forecasts,
            _alerts,
            saved =>
            {
                _settings = saved;
                SettingsStore.Save(saved);
                _alerts.OnSettingsChanged();
            },
            _alerts.TestSound,
            ShowSample);
        _settingsWindow = new SettingsWindow(viewModel);
        if (_main is { IsVisible: true })
        {
            _settingsWindow.Owner = _main;
        }
        else
        {
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        try
        {
            _settingsWindow.ShowDialog();
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    /// <summary>Sample alert: modal over the settings, with no actions on the real state.</summary>
    private void ShowSample(AppSettings settings)
    {
        _alerts.PlaySampleFeedback(settings);
        var viewModel = new ForecastViewModel(SampleForecast(), isAlert: true, forecasts: null, alerts: _alerts, openMain: null, enableProtection: null,
            sampleProtection: settings.KeepUserActive);
        var window = new ForecastWindow(viewModel) { Owner = _settingsWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.ShowDialog();
    }

    private static RebootForecast SampleForecast()
    {
        var earliest = DateTime.Now.AddMinutes(30);
        return new RebootForecast
        {
            State = ForecastState.Imminent,
            AnalyzedAt = DateTime.Now,
            IsSample = true,
            EarliestReboot = earliest,
            PendingUpdates = [new PendingUpdate("sample:1", Strings.Host_SampleUpdateTitle, "Driver", null, false)],
            Notes = [new ForecastNote(ForecastNoteKind.SampleReason), new ForecastNote(ForecastNoteKind.SampleNotice)],
        };
    }

    private void OnMainClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        // Closing the window does not stop monitoring: the program stays in the notification area.
        e.Cancel = true;
        _main!.Hide();
        if (!_backgroundHintShown)
        {
            _backgroundHintShown = true;
            _tray.ShowNotification(Strings.Host_StillRunningTitle, Strings.Host_StillRunningText);
        }
    }

    /// <summary>For updating the program: the service keeps the exe open, so it has to be stopped together with the program.</summary>
    private async Task ExitAndStopServiceAsync()
    {
        if (!await Platform.ServiceSetup.StopAsync())
        {
            _tray.ShowNotification(Strings.Host_ServiceNotStoppedTitle, Strings.Host_ServiceNotStoppedText);
            return;
        }

        Exit();
    }

    private void Exit()
    {
        _isExiting = true;
        _forecastWindow?.Close();
        _settingsWindow?.Close();
        _main?.Close();
        _app.Shutdown();
    }
}
