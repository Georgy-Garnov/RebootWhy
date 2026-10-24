using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Microsoft.Win32;
using RebootWhy.App.Infrastructure;
using RebootWhy.App.Platform;
using RebootWhy.App.Properties;
using RebootWhy.App.Settings;
using RebootWhy.Core.Text;

namespace RebootWhy.App.ViewModels;

public sealed record SettingsPage(string Key, string Title, string Glyph, string Keywords);

public sealed record ChoiceOption<T>(T Value, string Title)
{
    /// <summary>Screen readers and UI Automation read list items through ToString.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// JetBrains-style settings: sections with search on the left, the page on the right, "OK / Cancel / Apply" at the bottom.
/// Changes accumulate and are applied together; Windows policies are applied with an administrator prompt.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly SettingsPage[] AllPages =
    [
        new("alerts", Strings.Settings_Page_Alerts, "\uE7E7", Strings.Settings_Keywords_Alerts),
        new("sound", Strings.Settings_Page_Sound, "\uE767", Strings.Settings_Keywords_Sound),
        new("protection", Strings.Settings_Page_Protection, "\uE72E", Strings.Settings_Keywords_Protection),
        new("startup", Strings.Settings_Page_Startup, "\uE7E8", Strings.Settings_Keywords_Startup),
        new("policies", Strings.Settings_Page_Policies, "\uE895", Strings.Settings_Keywords_Policies),
        new("language", Strings.Settings_Page_Language, "\uE774", Strings.Settings_Keywords_Language),
        new("about", Strings.Settings_Page_About, "\uE946", Strings.Settings_Keywords_About),
    ];

    private readonly Action<AppSettings> _saveSettings;
    private readonly Func<string?, string?> _testSound;
    private readonly Action<AppSettings> _showSample;
    private readonly Monitoring.ForecastCoordinator _forecasts;
    private readonly Monitoring.AlertCoordinator _alerts;
    private AppSettings _saved;
    private AppSettings _settings;
    private bool _savedAutostart = Platform.Autostart.IsEnabled;
    private bool _autostart;
    private UpdatePolicyState _policyState = UpdatePolicies.Read();
    private UpdatePolicyValues _policies;
    private SettingsPage? _selectedPage;
    private string _searchText = string.Empty;
    private string _serviceStatus = ServiceSetup.StatusText;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _isBusy;

    /// <summary>The language this process is running in; a different saved choice takes effect after a restart.</summary>
    private readonly string _runningLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public SettingsViewModel(AppSettings settings, Monitoring.ForecastCoordinator forecasts, Monitoring.AlertCoordinator alerts,
        Action<AppSettings> saveSettings, Func<string?, string?> testSound, Action<AppSettings> showSample)
    {
        _forecasts = forecasts;
        _alerts = alerts;
        _alerts.SoundStateChanged += () => OnPropertyChanged(nameof(IsSoundPlaying));
        _forecasts.ForecastUpdated += _ => OnPropertyChanged(nameof(IsSimulating));
        _saved = settings.Clone();
        _settings = settings.Clone();
        _autostart = _savedAutostart;
        _policies = _policyState.Configured;
        _saveSettings = saveSettings;
        _testSound = testSound;
        _showSample = showSample;

        Pages = new ObservableCollection<SettingsPage>(AllPages);
        _selectedPage = Pages[0];

        ChooseSoundCommand = new RelayCommand(ChooseSound);
        ResetSoundCommand = new RelayCommand(() => SoundFile = null, () => SoundFile is not null);
        TestSoundCommand = new RelayCommand(() =>
        {
            if (IsSoundPlaying)
            {
                _alerts.StopSound();
            }
            else
            {
                TestSound();
            }
        });
        ShowSampleCommand = new RelayCommand(() => _showSample(_settings.Clone()));
        StartSimulationCommand = new RelayCommand(() => _ = StartSimulationAsync(), () => !IsSimulating);
        StopSimulationCommand = new RelayCommand(() => _ = _forecasts.StopSimulationAsync(), () => IsSimulating);
        InstallServiceCommand = new RelayCommand(() => _ = ChangeServiceAsync(install: true), () => !IsBusy);
        UninstallServiceCommand = new RelayCommand(() => _ = ChangeServiceAsync(install: false), () => !IsBusy && ServiceSetup.IsInstalled);
        OpenActiveHoursCommand = new RelayCommand(() => Open("ms-settings:windowsupdate-options"));
        OpenSettingsFolderCommand = new RelayCommand(() => Open(System.IO.Path.GetDirectoryName(SettingsStore.FilePath)!));
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(close: false), () => IsDirty && !IsBusy);
        OkCommand = new RelayCommand(() => _ = ApplyAsync(close: true), () => !IsBusy);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => !IsBusy);
        RestartCommand = new RelayCommand(() => _ = RestartAsync(), () => !IsBusy);
    }

    public event Action? CloseRequested;

    // Navigation

    public ObservableCollection<SettingsPage> Pages { get; }

    public SettingsPage? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value is not null && Set(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(PageKey));
            }
        }
    }

    public string PageKey => SelectedPage?.Key ?? string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                FilterPages();
            }
        }
    }

    // Alerts

    public bool ShowNotification
    {
        get => _settings.ShowNotification;
        set => Change(_settings.ShowNotification, value, v => _settings.ShowNotification = v);
    }

    public bool ShowAlertOnDetection
    {
        get => _settings.ShowAlertOnDetection;
        set => Change(_settings.ShowAlertOnDetection, value, v => _settings.ShowAlertOnDetection = v);
    }

    public bool ShowAlertWindow
    {
        get => _settings.ShowAlertWindow;
        set => Change(_settings.ShowAlertWindow, value, v => _settings.ShowAlertWindow = v);
    }

    public IReadOnlyList<ChoiceOption<int>> MinutesOptions { get; } =
    [
        new(15, Strings.Settings_Minutes_15),
        new(30, Strings.Settings_Minutes_30),
        new(60, Strings.Settings_Minutes_60),
        new(120, Strings.Settings_Minutes_120),
        new(240, Strings.Settings_Minutes_240),
    ];

    public ChoiceOption<int> AlertMinutes
    {
        get => MinutesOptions.FirstOrDefault(o => o.Value == _settings.AlertWindowMinutesBefore) ?? MinutesOptions[2];
        set => Change(_settings.AlertWindowMinutesBefore, value.Value, v => _settings.AlertWindowMinutesBefore = v);
    }

    // Sound

    public bool PlaySound
    {
        get => _settings.PlaySound;
        set => Change(_settings.PlaySound, value, v => _settings.PlaySound = v);
    }

    public string? SoundFile
    {
        get => _settings.SoundFile;
        private set
        {
            Change(_settings.SoundFile, value, v => _settings.SoundFile = v);
            OnPropertyChanged(nameof(SoundFileName));
            OnPropertyChanged(nameof(SoundFileFolder));
        }
    }

    public string SoundFileName => SoundFile is null ? Strings.Sound_System : System.IO.Path.GetFileName(SoundFile);

    public string SoundFileFolder => SoundFile is null ? Strings.Sound_NoFile : System.IO.Path.GetDirectoryName(SoundFile) ?? string.Empty;

    // Restart protection

    public bool KeepUserActive
    {
        get => _settings.KeepUserActive;
        set => Change(_settings.KeepUserActive, value, v => _settings.KeepUserActive = v);
    }

    // Startup and service

    public bool Autostart
    {
        get => _autostart;
        set
        {
            if (Set(ref _autostart, value))
            {
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        private set => Set(ref _serviceStatus, value);
    }

    public bool ServiceInstalled => ServiceSetup.IsInstalled;

    public string InstallServiceText => ServiceInstalled ? Strings.Service_Reinstall : Strings.Service_Install;

    // Windows Update policies

    public bool PolicyNoAutoReboot
    {
        get => _policies.NoAutoRebootWithLoggedOnUsers;
        set => ChangePolicies(_policies with { NoAutoRebootWithLoggedOnUsers = value });
    }

    public bool PolicyExcludeDrivers
    {
        get => _policies.ExcludeDrivers;
        set => ChangePolicies(_policies with { ExcludeDrivers = value });
    }

    public IReadOnlyList<ChoiceOption<int?>> AutoUpdateOptions { get; } =
        new int?[] { null, 2, 3, 4 }.Select(v => new ChoiceOption<int?>(v, UpdatePolicies.AutoUpdateText(v))).ToList();

    public ChoiceOption<int?> PolicyAutoUpdate
    {
        get => AutoUpdateOptions.FirstOrDefault(o => o.Value == _policies.AutoUpdateOption) ?? AutoUpdateOptions[0];
        set => ChangePolicies(_policies with { AutoUpdateOption = value.Value });
    }

    public string NoAutoRebootState => PolicyStateText(c => c.NoAutoRebootWithLoggedOnUsers ? Strings.Policy_Enabled : Strings.Policy_NotSet);

    public string ExcludeDriversState => PolicyStateText(c => c.ExcludeDrivers ? Strings.Policy_Enabled : Strings.Policy_NotSet);

    public string AutoUpdateState => PolicyStateText(c => UpdatePolicies.AutoUpdateText(c.AutoUpdateOption).ToLower(CultureInfo.CurrentUICulture));

    public string ActiveHoursText => _policyState.ActiveHours is { } hours
        ? Display.Format(Strings.Policy_ActiveHours_Now, hours.Describe())
        : Strings.Policy_ActiveHours_NotSet;

    public bool PoliciesDirty => _policies != _policyState.Configured;

    // Language

    public IReadOnlyList<ChoiceOption<string>> LanguageOptions { get; } =
    [
        new(Localization.SystemLanguage, $"{Strings.Language_System} ({Localization.NativeName(Localization.Resolve(Localization.SystemLanguage))})"),
        .. Localization.Supported.Select(language => new ChoiceOption<string>(language, Localization.NativeName(language))),
    ];

    public ChoiceOption<string> LanguageOption
    {
        get => LanguageOptions.FirstOrDefault(o => o.Value == (_settings.Language ?? Localization.SystemLanguage)) ?? LanguageOptions[0];
        set
        {
            Change(_settings.Language, value.Value, v => _settings.Language = v);
            OnPropertyChanged(nameof(LanguageRestartNeeded));
        }
    }

    /// <summary>The chosen language (saved or not yet) differs from the one the program is running in.</summary>
    public bool LanguageRestartNeeded => Localization.Resolve(_settings.Language) != _runningLanguage;

    // About

    public string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "—";

    public string ExecutablePath => Environment.ProcessPath ?? "—";

    public string SettingsPath => SettingsStore.FilePath;

    // Common

    public bool IsDirty => !_settings.SameAs(_saved) || Autostart != _savedAutostart || PoliciesDirty;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            Set(ref _isBusy, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => Set(ref _statusIsError, value);
    }

    public ICommand ChooseSoundCommand { get; }

    public ICommand ResetSoundCommand { get; }

    public ICommand TestSoundCommand { get; }

    public ICommand ShowSampleCommand { get; }

    public ICommand StartSimulationCommand { get; }

    public ICommand StopSimulationCommand { get; }

    public bool IsSimulating => _forecasts.IsSimulating;

    public bool IsSoundPlaying => _alerts.IsSoundPlaying;

    public ICommand InstallServiceCommand { get; }

    public ICommand UninstallServiceCommand { get; }

    public ICommand OpenActiveHoursCommand { get; }

    public ICommand OpenSettingsFolderCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand OkCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RestartCommand { get; }

    private async Task ApplyAsync(bool close)
    {
        IsBusy = true;
        try
        {
            if (!_settings.SameAs(_saved))
            {
                _saveSettings(_settings.Clone());
                _saved = _settings.Clone();
            }

            if (Autostart != _savedAutostart)
            {
                Platform.Autostart.Set(Autostart);
                _savedAutostart = Autostart;
            }

            if (PoliciesDirty)
            {
                Report(Strings.Policy_Applying, error: false);
                var applied = await UpdatePolicies.ApplyAsync(_policies);
                _policyState = UpdatePolicies.Read();
                if (!applied)
                {
                    Report(Strings.Policy_NotApplied, error: true);
                    RefreshPolicies();
                    return;
                }

                _policies = _policyState.Configured;
                RefreshPolicies();
                Report(_policyState.SeenByWindowsUpdate == _policyState.Configured
                    ? Strings.Policy_Applied
                    : Strings.Policy_AppliedLater, error: false);
            }
            else
            {
                Report(close ? null : Strings.Settings_Saved, error: false);
            }

            OnPropertyChanged(nameof(IsDirty));
            if (close)
            {
                CloseRequested?.Invoke();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves pending changes and restarts the program so that the new language takes effect.</summary>
    private async Task RestartAsync()
    {
        if (IsDirty)
        {
            await ApplyAsync(close: false);
            if (IsDirty)
            {
                // Something was not applied (for example, the administrator prompt was declined): the status explains why.
                return;
            }
        }

        if (!AppRestart.Restart())
        {
            Report(Strings.Language_RestartFailed, error: true);
        }
    }

    private async Task StartSimulationAsync()
    {
        var started = await _forecasts.StartSimulationAsync();
        Report(started
            ? Strings.Settings_TestStarted
            : Strings.Settings_TestFailed, error: !started);
    }

    private void ChooseSound()
    {
        var dialog = new OpenFileDialog { Title = Strings.Sound_DialogTitle, Filter = AlertSound.FileFilter };
        if (dialog.ShowDialog() == true)
        {
            SoundFile = dialog.FileName;
        }
    }

    private void TestSound()
    {
        var error = _testSound(SoundFile);
        Report(error is null ? null : Display.Format(Strings.Sound_PlaybackFailed, error), error: error is not null);
    }

    private async Task ChangeServiceAsync(bool install)
    {
        IsBusy = true;
        Report(install ? Strings.Service_Installing : Strings.Service_Removing, error: false);
        var ok = install ? await ServiceSetup.InstallAsync() : await ServiceSetup.UninstallAsync();
        ServiceStatus = ServiceSetup.StatusText;
        OnPropertyChanged(nameof(ServiceInstalled));
        OnPropertyChanged(nameof(InstallServiceText));
        Report(ok ? (install ? Strings.Service_Installed : Strings.Service_Removed) : Strings.Settings_ElevationFailed, error: !ok);
        IsBusy = false;
    }

    private string PolicyStateText(Func<UpdatePolicyValues, string> describe)
    {
        var configured = describe(_policyState.Configured);
        if (_policyState.SeenByWindowsUpdate is not { } seen)
        {
            return Display.Format(Strings.Policy_StateNow, configured);
        }

        var applied = describe(seen);
        return configured == applied
            ? Display.Format(Strings.Policy_StateNow, configured)
            : Display.Format(Strings.Policy_StatePending, configured, applied);
    }

    private void ChangePolicies(UpdatePolicyValues value)
    {
        if (value == _policies)
        {
            return;
        }

        _policies = value;
        RefreshPolicies();
    }

    private void RefreshPolicies()
    {
        foreach (var name in new[]
                 {
                     nameof(PolicyNoAutoReboot), nameof(PolicyExcludeDrivers), nameof(PolicyAutoUpdate), nameof(NoAutoRebootState),
                     nameof(ExcludeDriversState), nameof(AutoUpdateState), nameof(ActiveHoursText), nameof(PoliciesDirty), nameof(IsDirty),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void Change<T>(T current, T value, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        apply(value);
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsDirty));
        CommandManager.InvalidateRequerySuggested();
    }

    private void FilterPages()
    {
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matching = AllPages.Where(p => words.All(w =>
            p.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase) || p.Keywords.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        Pages.Clear();
        foreach (var page in matching)
        {
            Pages.Add(page);
        }

        if (SelectedPage is null || !Pages.Contains(SelectedPage))
        {
            SelectedPage = Pages.FirstOrDefault() ?? SelectedPage;
        }
    }

    private void Report(string? message, bool error)
    {
        StatusMessage = message;
        StatusIsError = error;
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
