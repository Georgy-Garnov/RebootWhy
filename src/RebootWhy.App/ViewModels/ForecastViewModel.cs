using System.Windows.Input;
using RebootWhy.App.Infrastructure;
using RebootWhy.App.Monitoring;
using RebootWhy.App.Properties;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.ViewModels;

public sealed record SnoozeOption(int Minutes, string Title);

/// <summary>
/// Windows Update forecast window. In alert mode it has an alarm header and "Remind me";
/// in sample mode (from the settings) it only previews the look, without actions.
/// </summary>
public sealed class ForecastViewModel : ObservableObject
{
    private readonly ForecastCoordinator? _forecasts;
    private readonly AlertCoordinator? _alerts;
    private readonly bool _sampleProtection;
    private RebootForecast _forecast;
    private SnoozeOption _selectedSnooze;

    public ForecastViewModel(
        RebootForecast forecast,
        bool isAlert,
        ForecastCoordinator? forecasts,
        AlertCoordinator? alerts,
        Action? openMain,
        Action? enableProtection,
        bool sampleProtection = false)
    {
        _forecast = forecast;
        _forecasts = forecasts;
        _alerts = alerts;
        _sampleProtection = sampleProtection;
        IsAlert = isAlert;
        _selectedSnooze = SnoozeOptions[1];

        CheckCommand = new RelayCommand(() => _ = _forecasts!.AnalyzeNowAsync(), () => _forecasts is { IsChecking: false } && !IsSample);
        SnoozeCommand = new RelayCommand(Snooze, () => CanSnooze);
        OpenMainCommand = new RelayCommand(() => openMain?.Invoke(), () => openMain is not null && !IsSample);
        EnableProtectionCommand = new RelayCommand(() => enableProtection?.Invoke(), () => enableProtection is not null);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
        StopSimulationCommand = new RelayCommand(() => _ = _forecasts!.StopSimulationAsync(), () => IsSimulation && _forecasts is not null);
        StopSoundCommand = new RelayCommand(() => _alerts?.StopSound());

        if (_forecasts is not null)
        {
            _forecasts.CheckingChanged += OnCheckingChanged;
        }

        if (_alerts is not null)
        {
            _alerts.ProtectionChanged += OnProtectionChanged;
            _alerts.SoundStateChanged += OnSoundChanged;
        }
    }

    public bool IsSoundPlaying => _alerts?.IsSoundPlaying == true;

    public ICommand StopSoundCommand { get; }

    public event Action? CloseRequested;

    public bool IsAlert { get; }

    public bool IsSample => _forecast.IsSample;

    public bool IsSimulation => _forecast.IsSimulation;

    /// <summary>Header badge: a sample or a reaction test.</summary>
    public string? BadgeText => IsSample ? Strings.ForecastWin_BadgeSample : IsSimulation ? Strings.ForecastWin_BadgeTest : null;

    public bool IsPending => _forecast.State != ForecastState.None;

    public string Header => IsSample
        ? Strings.ForecastWin_HeaderSample
        : !IsPending ? Strings.ForecastWin_HeaderNone
        : (IsSimulation ? Strings.ForecastWin_HeaderTestPrefix : string.Empty) + (IsAlert ? Strings.ForecastWin_HeaderAlert : Strings.ForecastWin_HeaderPending);

    /// <summary>Normal, Warning, Critical or Sample: the header color.</summary>
    public string Severity => IsSample ? "Sample" : _forecast.State switch
    {
        ForecastState.Imminent => "Critical",
        ForecastState.Planned => "Warning",
        _ => "Normal",
    };

    public string HeaderGlyph => IsSample ? "\uE946" : IsPending ? "\uE7BA" : "\uE73E";

    public string Summary => IsPending || IsSample
        ? ForecastText.Summary(_forecast)
        : Strings.ForecastWin_SummaryNone;

    /// <summary>When Windows Update next runs scheduled work (for the calm state).</summary>
    public string? NextWorkText => !IsPending && _forecast.Schedule.Count > 0
        ? Display.Format(Strings.ForecastWin_NextWork, Display.ShortDateTime(_forecast.Schedule[0].NextRun))
        : null;

    public string? WhenText => _forecast.EarliestReboot is not { } earliest
        ? null
        : earliest > DateTime.Now
            ? Display.Format(Strings.ForecastWin_NotBefore, Display.ShortDateTime(earliest), Display.Duration(earliest - DateTime.Now))
            : Display.Format(Strings.ForecastWin_PossibleNow, Display.ShortDateTime(earliest));

    public IReadOnlyList<string> Updates => _forecast.PendingUpdates
        .Select(u => u.PendingSince is { } since
            ? Display.Format(Strings.ForecastWin_UpdateLineSince, ForecastText.UpdateTitle(u), ForecastText.UpdateType(u), Display.ShortDateTime(since))
            : Display.Format(Strings.ForecastWin_UpdateLine, ForecastText.UpdateTitle(u), ForecastText.UpdateType(u)))
        .ToList();

    public bool HasUpdates => _forecast.PendingUpdates.Count > 0;

    public IReadOnlyList<string> Notes => ForecastText.Notes(_forecast);

    public bool HasNotes => _forecast.Notes.Count > 0;

    public IReadOnlyList<string> Evidence => ForecastText.Evidence(_forecast);

    public bool HasEvidence => Evidence.Count > 0;

    public IReadOnlyList<string> Problems => ForecastText.Problems(_forecast);

    public bool HasProblems => _forecast.Problems.Count > 0;

    // Hold against an automatic restart

    public bool ShowProtection => IsPending;

    public bool ProtectionActive => IsSample ? _sampleProtection : _alerts?.IsKeepingUserActive == true;

    public string ProtectionTitle => ProtectionActive
        ? Strings.ForecastWin_ProtectionOnTitle
        : Strings.ForecastWin_ProtectionOffTitle;

    public string ProtectionText => ProtectionActive
        ? Strings.ForecastWin_ProtectionOnText
        : Strings.ForecastWin_ProtectionOffText;

    public bool CanEnableProtection => !ProtectionActive && !IsSample && EnableProtectionCommand.CanExecute(null);

    // Checking again

    public bool IsChecking => _forecasts?.IsChecking == true;

    public string CheckedText => Display.Format(Strings.ForecastWin_CheckedAt, Display.TimeWithSeconds(_forecast.AnalyzedAt));

    public string SourceText =>
        (_forecasts?.FromService == true ? Strings.ForecastWin_SourceService : Strings.ForecastWin_SourceLocal)
        + (_forecast.Schedule.Count == 0 ? string.Empty : _forecast.ScheduleFromTaskScheduler ? Strings.ForecastWin_ScheduleFromScheduler : Strings.ForecastWin_ScheduleFromLog);

    // Remind me later

    public IReadOnlyList<SnoozeOption> SnoozeOptions { get; } =
    [
        new(5, Display.Format(Strings.ForecastWin_SnoozeMinutes, 5)),
        new(15, Display.Format(Strings.ForecastWin_SnoozeMinutes, 15)),
        new(30, Display.Format(Strings.ForecastWin_SnoozeMinutes, 30)),
        new(60, Strings.ForecastWin_SnoozeHour),
        new(120, Strings.ForecastWin_SnoozeTwoHours),
    ];

    public SnoozeOption SelectedSnooze
    {
        get => _selectedSnooze;
        set
        {
            if (Set(ref _selectedSnooze, value))
            {
                OnPropertyChanged(nameof(SnoozeWarning));
            }
        }
    }

    /// <summary>In the sample "Remind me" is visible but disabled, so the alert is shown in full.</summary>
    public bool ShowSnooze => IsAlert && IsPending;

    public bool CanSnooze => ShowSnooze && !IsSample && _alerts is not null;

    public string? SnoozeWarning => _forecast.EarliestReboot is { } earliest && DateTime.Now.AddMinutes(SelectedSnooze.Minutes) > earliest
        ? Display.Format(Strings.ForecastWin_SnoozeWarning, Display.ShortDateTime(earliest))
        : null;

    public ICommand CheckCommand { get; }

    public ICommand SnoozeCommand { get; }

    public ICommand OpenMainCommand { get; }

    public ICommand EnableProtectionCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand StopSimulationCommand { get; }

    public void SetForecast(RebootForecast forecast)
    {
        _forecast = forecast;
        OnPropertyChanged(string.Empty);
    }

    public void Detach()
    {
        if (_forecasts is not null)
        {
            _forecasts.CheckingChanged -= OnCheckingChanged;
        }

        if (_alerts is not null)
        {
            _alerts.ProtectionChanged -= OnProtectionChanged;
            _alerts.SoundStateChanged -= OnSoundChanged;

            // The alert was closed, so its sound is no longer needed.
            if (IsAlert)
            {
                _alerts.StopSound();
            }
        }
    }

    private void OnSoundChanged()
    {
        OnPropertyChanged(nameof(IsSoundPlaying));
    }

    private void Snooze()
    {
        _alerts!.StopSound();
        _alerts.Snooze(TimeSpan.FromMinutes(SelectedSnooze.Minutes));
        CloseRequested?.Invoke();
    }

    private void OnCheckingChanged()
    {
        OnPropertyChanged(nameof(IsChecking));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnProtectionChanged()
    {
        OnPropertyChanged(nameof(ProtectionActive));
        OnPropertyChanged(nameof(ProtectionTitle));
        OnPropertyChanged(nameof(ProtectionText));
        OnPropertyChanged(nameof(CanEnableProtection));
    }
}
