using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using RebootWhy.App.Infrastructure;
using RebootWhy.App.Monitoring;
using RebootWhy.App.Properties;
using RebootWhy.App.Settings;
using RebootWhy.Core;
using RebootWhy.Core.Analysis;
using RebootWhy.Core.Sources;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.ViewModels;

public sealed record PeriodOption(int Days, string Title);

public sealed class MainViewModel : ObservableObject
{
    private static string CopyText => Strings.Main_Copy;

    private readonly DispatcherTimer _uptimeTimer;
    private readonly DispatcherTimer _copyFeedbackTimer;
    private string _copyButtonText = CopyText;
    private readonly ForecastCoordinator _forecasts;
    private readonly AlertCoordinator _alerts;
    private readonly ViewState _view;
    private IReadOnlyList<RebootRecord> _records = [];
    private string? _filePath;
    private PeriodOption _selectedPeriod;
    private bool _onlyFullBoots;
    private RebootItemViewModel? _selectedRecord;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string _uptimeText = "—";
    private string _uptimeSinceText = string.Empty;
    private string _fullBootText = "—";
    private string _activeTimeText = "—";

    public MainViewModel(ForecastCoordinator forecasts, AlertCoordinator alerts, ViewState view, Action openForecast, Action openSettings)
    {
        _view = view;
        _forecasts = forecasts;
        _alerts = alerts;
        _forecasts.ForecastUpdated += _ => OnForecastChanged();
        _forecasts.SourceChanged += OnForecastChanged;
        _forecasts.CheckingChanged += OnForecastChanged;
        _alerts.ProtectionChanged += OnForecastChanged;
        Updates = new UpdatesViewModel(forecasts, view);
        OpenForecastCommand = new RelayCommand(openForecast, () => _forecasts.Latest is not null);
        AnalyzeUpdatesCommand = new RelayCommand(() => _ = _forecasts.AnalyzeNowAsync(), () => !_forecasts.IsChecking);
        OpenSettingsCommand = new RelayCommand(openSettings);

        _selectedPeriod = Periods.FirstOrDefault(p => p.Days == view.PeriodDays) ?? Periods[1];
        _onlyFullBoots = view.OnlyFullBoots;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync(), () => !IsBusy);
        OpenFileCommand = new RelayCommand(OpenFile, () => !IsBusy);
        UseLiveLogCommand = new RelayCommand(UseLiveLog, () => !IsBusy);
        CopySelectedCommand = new RelayCommand(CopySelected, () => SelectedRecord is not null);

        _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyFeedbackTimer.Tick += (_, _) =>
        {
            _copyFeedbackTimer.Stop();
            CopyButtonText = CopyText;
        };

        var fastStartup = WindowsSystemInfo.IsFastStartupEnabled();
        FastStartupHint = fastStartup == true
            ? Strings.Main_FastStartupHint
            : null;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
        _uptimeTimer.Start();
    }

    public IReadOnlyList<PeriodOption> Periods { get; } =
    [
        new(7, Strings.Main_Period7Days),
        new(30, Strings.Main_Period30Days),
        new(90, Strings.Main_Period90Days),
        new(365, Strings.Main_Period1Year),
    ];

    public PeriodOption SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (Set(ref _selectedPeriod, value))
            {
                _view.PeriodDays = value.Days;
                ViewStateStore.Save(_view);
                if (IsLive)
                {
                    _ = LoadAsync();
                }
            }
        }
    }

    public bool OnlyFullBoots
    {
        get => _onlyFullBoots;
        set
        {
            if (Set(ref _onlyFullBoots, value))
            {
                _view.OnlyFullBoots = value;
                ViewStateStore.Save(_view);
                ApplyFilter();
            }
        }
    }

    public ObservableCollection<RebootItemViewModel> Records { get; } = [];

    public RebootItemViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (Set(ref _selectedRecord, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
            }
        }
    }

    public bool HasSelection => SelectedRecord is not null;

    public bool HasNoSelection => SelectedRecord is null;

    public bool IsLive => _filePath is null;

    public bool IsFile => _filePath is not null;

    public string SourceText => _filePath is null ? Strings.Main_SourceLive : Display.Format(Strings.Main_SourceFile, _filePath);

    public string? FastStartupHint { get; }

    public bool HasFastStartupHint => FastStartupHint is not null && IsLive;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        private set => Set(ref _uptimeText, value);
    }

    public string UptimeSinceText
    {
        get => _uptimeSinceText;
        private set => Set(ref _uptimeSinceText, value);
    }

    public string FullBootText
    {
        get => _fullBootText;
        private set => Set(ref _fullBootText, value);
    }

    public string ActiveTimeText
    {
        get => _activeTimeText;
        private set => Set(ref _activeTimeText, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand OpenFileCommand { get; }

    public ICommand UseLiveLogCommand { get; }

    public ICommand CopySelectedCommand { get; }

    public ICommand OpenForecastCommand { get; }

    public ICommand AnalyzeUpdatesCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public string UpdateSummary => _forecasts.Latest is { } latest ? ForecastText.Summary(latest) : Strings.Main_CheckingUpdates;

    /// <summary>Normal, Warning or Critical: the color of the updates banner.</summary>
    public string UpdateSeverity => _forecasts.Latest?.State switch
    {
        ForecastState.Imminent => "Critical",
        ForecastState.Planned => "Warning",
        _ => "Normal",
    };

    public string UpdateSourceText =>
        string.Join(" · ", new[]
        {
            _forecasts.Latest is { } latest ? Display.Format(Strings.Main_CheckedAt, Display.TimeWithSeconds(latest.AnalyzedAt)) : null,
            _forecasts.FromService ? Strings.Main_ServiceWatching : Strings.Main_ServiceNotConnected,
            _alerts.IsKeepingUserActive ? Strings.Main_KeepingActive : null,
        }.OfType<string>());

    public bool IsCheckingUpdates => _forecasts.IsChecking;

    public bool IsSimulating => _forecasts.IsSimulating;

    public ICommand StopSimulationCommand => new RelayCommand(() => _ = _forecasts.StopSimulationAsync());

    public UpdatesViewModel Updates { get; }

    public string CopyButtonText
    {
        get => _copyButtonText;
        private set => Set(ref _copyButtonText, value);
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = Strings.Main_ReadingLog;
        try
        {
            var source = _filePath is null ? WindowsEventLogSource.ForLiveSystemLog() : WindowsEventLogSource.ForFile(_filePath);
            DateTimeOffset? since = _filePath is null ? DateTimeOffset.UtcNow.AddDays(-SelectedPeriod.Days) : null;
            var report = await Task.Run(() => RebootHistory.Load(source, since));

            _records = report.Records;
            ApplyFilter();
            UpdateUptime();
            StatusText = string.Join(" · ",
                SourceText,
                Display.Format(Strings.Main_StatusBoots, report.Records.Count),
                Display.Format(Strings.Main_StatusEvents, report.EventCount),
                Display.Format(Strings.Main_StatusRefreshedAt, Display.LocalTime(DateTimeOffset.UtcNow)));
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or IOException)
        {
            StatusText = Display.Format(Strings.Main_ReadLogFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Rebuilds the list keeping the selection (by boot time: after rereading the log these are new objects)
    /// and the scroll position: the list must not jump while the user is examining it.
    /// </summary>
    private void ApplyFilter()
    {
        var selectedBoot = SelectedRecord?.Record.BootTimeUtc;
        var rows = _records.Where(r => !OnlyFullBoots || r.IsFullBoot).ToList();
        if (rows.Select(r => (r.BootTimeUtc, r.Title)).SequenceEqual(Records.Select(r => (r.Record.BootTimeUtc, r.Record.Title))))
        {
            return;
        }

        RecordsRebuilding?.Invoke();
        Records.Clear();
        foreach (var record in rows)
        {
            Records.Add(new RebootItemViewModel(record));
        }

        SelectedRecord = Records.FirstOrDefault(r => r.Record.BootTimeUtc == selectedBoot) ?? Records.FirstOrDefault();
        RecordsRebuilt?.Invoke(selectedBoot is null);
    }

    public event Action? RecordsRebuilding;

    /// <summary>true: the selection is new (first load or the previous row is gone), show it; false: restore the scroll position.</summary>
    public event Action<bool>? RecordsRebuilt;

    private void UpdateUptime()
    {
        if (!IsLive)
        {
            return;
        }

        var uptime = UptimeCalculator.Calculate(
            _records,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            WindowsSystemInfo.GetActiveTimeSinceKernelStart());

        UptimeText = Display.Duration(uptime.SinceLastStart);
        UptimeSinceText = Display.Format(Strings.Main_UptimeSince, Display.LocalDateTime(uptime.LastStartUtc), Display.BootType(uptime.LastStartType).ToLowerInvariant());
        FullBootText = Display.Format(Strings.Main_FullBootAgo, Display.LocalDateTime(uptime.LastFullBootUtc), Display.Duration(uptime.SinceLastFullBoot));
        ActiveTimeText = uptime.ActiveSinceFullBoot is { } active ? Display.Duration(active) : Strings.Main_NoData;
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Main_OpenLogTitle,
            Filter = Strings.Main_OpenLogFilter + " (*.evtx)|*.evtx",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SetSource(dialog.FileName);
    }

    private void UseLiveLog() => SetSource(null);

    private void OnForecastChanged()
    {
        OnPropertyChanged(nameof(UpdateSummary));
        OnPropertyChanged(nameof(UpdateSeverity));
        OnPropertyChanged(nameof(UpdateSourceText));
        OnPropertyChanged(nameof(IsCheckingUpdates));
        OnPropertyChanged(nameof(IsSimulating));
        CommandManager.InvalidateRequerySuggested();
    }

    private void CopySelected()
    {
        if (SelectedRecord is null)
        {
            return;
        }

        try
        {
            // copy: true keeps the text on the clipboard after the app closes.
            Clipboard.SetDataObject(SelectedRecord.ToClipboardText(), copy: true);
            CopyButtonText = Strings.Main_Copied;
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
        catch (ExternalException ex)
        {
            StatusText = Display.Format(Strings.Main_CopyFailed, ex.Message);
        }
    }

    private void SetSource(string? filePath)
    {
        _filePath = filePath;
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsFile));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(HasFastStartupHint));
        _ = LoadAsync();
    }
}
