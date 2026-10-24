using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using RebootWhy.App.Infrastructure;
using RebootWhy.App.Monitoring;
using RebootWhy.App.Properties;
using RebootWhy.App.Settings;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.ViewModels;

/// <summary>The Updates tab: what Windows has installed and what is still waiting to be installed or for a restart.</summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private static string CopyText => Strings.Main_Copy;

    private readonly ForecastCoordinator _forecasts;
    private readonly ViewState _view;
    private readonly DispatcherTimer _copyFeedback = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<InstalledUpdate> _history = [];
    private List<AvailableUpdate> _available = [];
    private bool _onlyImportant;
    private bool _onlyReboot;
    private UpdateItemViewModel? _selected;
    private bool _isLoadingHistory;
    private bool _isSearching;
    private bool _loaded;
    private string _status = Strings.Updates_NotLoaded;
    private string _copyButtonText = CopyText;
    private string _searchText = string.Empty;
    private string[] _searchWords = [];
    private string _waitingSignature = string.Empty;
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ICollectionView _rows;

    public UpdatesViewModel(ForecastCoordinator forecasts, ViewState view)
    {
        _forecasts = forecasts;
        _view = view;
        _onlyImportant = view.OnlyImportantUpdates;
        _onlyReboot = view.OnlyRebootUpdates;
        _forecasts.ForecastUpdated += _ => OnForecastUpdated();
        _rows = CreateView([]);
        ShowCached();
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            _searchWords = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            ApplyViewFilter();
        };

        RefreshCommand = new RelayCommand(() => _ = LoadAsync(), () => !IsBusy);
        CopyCommand = new RelayCommand(Copy, () => Selected is not null);
        _copyFeedback.Tick += (_, _) =>
        {
            _copyFeedback.Stop();
            CopyButtonText = CopyText;
        };
    }

    /// <summary>The list is about to be rebuilt: the window remembers the scroll position to restore it.</summary>
    public event Action? Rebuilding;

    /// <summary>The list was rebuilt. true: by a user action (search, filter, Refresh), show the selected row;
    /// false: in the background, restore the scroll position.</summary>
    public event Action<bool>? Rebuilt;

    public string SearchText
    {
        get => _searchText;
        set
        {
            // Filter after a short typing pause so nothing slows down the input while the user types.
            if (Set(ref _searchText, value))
            {
                _searchDelay.Stop();
                _searchDelay.Start();
            }
        }
    }

    /// <summary>Show only updates whose installation will or may require a restart.</summary>
    public bool OnlyReboot
    {
        get => _onlyReboot;
        set
        {
            if (Set(ref _onlyReboot, value))
            {
                _view.OnlyRebootUpdates = value;
                ViewStateStore.Save(_view);
                ApplyViewFilter();
            }
        }
    }

    private void ApplyViewFilter()
    {
        View.Refresh();

        // If the selected row does not match the filter, show the first matching one.
        if (Selected is null || !View.Contains(Selected))
        {
            Selected = View.Cast<UpdateItemViewModel>().FirstOrDefault();
        }

        UpdateStatus();
        Rebuilt?.Invoke(true);
    }

    /// <summary>
    /// History rows. A rebuild creates a whole new view: adding hundreds of rows one by one
    /// would make WPF recompute the groups after each of them.
    /// </summary>
    public ICollectionView View => _rows;

    private List<UpdateItemViewModel> Items => (List<UpdateItemViewModel>)((ListCollectionView)_rows).SourceCollection;

    public UpdateItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>Hide housekeeping updates: antivirus definitions and Microsoft Store apps.</summary>
    public bool OnlyImportant
    {
        get => _onlyImportant;
        set
        {
            if (Set(ref _onlyImportant, value))
            {
                _view.OnlyImportantUpdates = value;
                ViewStateStore.Save(_view);
                Rebuild(userInitiated: true);
            }
        }
    }

    public bool IsBusy => _isLoadingHistory || _isSearching;

    public bool IsSearching => _isSearching;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string CopyButtonText
    {
        get => _copyButtonText;
        private set => Set(ref _copyButtonText, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand CopyCommand { get; }

    /// <summary>Refresh the data in the background (once per run; after that via the Refresh button). Saved data stays visible while loading.</summary>
    public void EnsureLoaded()
    {
        if (!_loaded)
        {
            _loaded = true;
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        SetBusy(history: true, searching: true);
        _refreshing = true;
        UpdateStatus();
        try
        {
            // Saved data stays on screen while loading; the lists are replaced when both are ready.
            var history = await Task.Run(() => WindowsUpdateCatalogReader.ReadHistory());
            if (_loadedAt is null)
            {
                _history = history;
                Rebuild(userInitiated: false);
            }

            SetBusy(history: false, searching: true);

            // Searching the local Windows Update database takes a few seconds.
            var available = await Task.Run(WindowsUpdateCatalogReader.SearchAvailable);
            _history = history;
            _available = available;
            _loadedAt = DateTime.Now;
            _fromCache = false;
            Rebuild(userInitiated: false);
            UpdatesCache.Save(new UpdatesSnapshot(_loadedAt.Value, _history, _available));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Status = Display.Format(Strings.Updates_WindowsUpdateFailed, ex.Message);
        }
        finally
        {
            _refreshing = false;
            SetBusy(history: false, searching: false);
            UpdateStatus();
        }
    }

    private DateTime? _loadedAt;
    private bool _fromCache;
    private bool _refreshing;

    /// <summary>Show the saved history right when the program opens.</summary>
    private void ShowCached()
    {
        if (UpdatesCache.Load() is not { } snapshot)
        {
            Status = Strings.Updates_NotLoaded;
            return;
        }

        _history = snapshot.History;
        _available = snapshot.Available;
        _loadedAt = snapshot.SavedAt;
        _fromCache = true;
        Rebuild(userInitiated: false);
    }

    private ICollectionView CreateView(List<UpdateItemViewModel> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UpdateItemViewModel.Group)));
        view.Filter = item => item is UpdateItemViewModel row
            && (!_onlyReboot || row.NeedsReboot)
            && (_searchWords.Length == 0 || row.Matches(_searchWords));
        return view;
    }

    /// <summary>
    /// The background forecast arrives every few minutes. The list is touched only if the set of updates waiting
    /// for a restart changed; otherwise the selected row and the scroll position would jump on every check.
    /// </summary>
    private void OnForecastUpdated()
    {
        var signature = string.Join(",", (_forecasts.Latest?.PendingUpdates ?? []).Select(u => u.Id).Order());
        if (signature != _waitingSignature)
        {
            Rebuild(userInitiated: false);
        }
    }

    private void Rebuild(bool userInitiated)
    {
        var waiting = _forecasts.Latest?.PendingUpdates ?? [];
        _waitingSignature = string.Join(",", waiting.Select(u => u.Id).Order());
        var waitingIds = waiting.Select(u => u.Id.ToLowerInvariant()).ToHashSet();
        var rows = new List<UpdateItemViewModel>();

        foreach (var update in _available.Where(u => !OnlyImportant || !u.IsRoutine))
        {
            rows.Add(UpdateItemViewModel.FromAvailable(update, waitingIds.Contains($"{update.UpdateId}:{update.Revision}".ToLowerInvariant())));
        }

        var known = _available.Select(u => $"{u.UpdateId}:{u.Revision}".ToLowerInvariant()).ToHashSet();
        rows.AddRange(waiting.Where(u => !known.Contains(u.Id.ToLowerInvariant())).Select(UpdateItemViewModel.FromForecast));
        rows.AddRange(_history
            .Where(u => !OnlyImportant || !u.IsRoutine)
            .OrderByDescending(u => u.TimeLocal)
            .Select(UpdateItemViewModel.FromHistory));

        // Nothing changed: leave the list alone.
        if (rows.Select(r => r.Key).SequenceEqual(Items.Select(i => i.Key)))
        {
            return;
        }

        var selectedKey = Selected?.Key;
        Rebuilding?.Invoke();
        _rows = CreateView(rows);
        OnPropertyChanged(nameof(View));

        // Keep the user's selection; on the first load show what is waiting to be installed.
        Selected = (selectedKey is null ? null : Items.FirstOrDefault(i => i.Key == selectedKey))
            ?? (selectedKey is null ? Items.FirstOrDefault(i => i.Group == UpdateItemViewModel.PendingGroup) : null)
            ?? View.Cast<UpdateItemViewModel>().FirstOrDefault();
        UpdateStatus();
        Rebuilt?.Invoke(userInitiated);
    }

    private void UpdateStatus()
    {
        if (_loadedAt is not { } loadedAt)
        {
            Status = _refreshing ? Strings.Updates_Reading : Status;
            return;
        }

        var parts = new List<string>();
        if (_searchWords.Length > 0 || _onlyReboot)
        {
            parts.Add(Display.Format(Strings.Updates_StatusFound, View.Cast<object>().Count()));
        }

        var needReboot = _available.Count(u => u.RebootBehavior != UpdateRebootBehavior.NeverReboots);
        var journal = Display.Format(Strings.Updates_StatusJournal, _history.Count, _available.Count);
        parts.Add(needReboot > 0 ? journal + " " + Display.Format(Strings.Updates_StatusNeedReboot, needReboot) : journal);
        parts.Add(_fromCache
            ? Display.Format(Strings.Updates_StatusCached, Display.ShortDateTime(loadedAt))
            : Display.Format(Strings.Updates_StatusRefreshedAt, Display.ShortTime(loadedAt)));
        if (_refreshing)
        {
            parts.Add(Strings.Updates_StatusRefreshing);
        }

        var status = string.Join(" · ", parts);
        Status = char.ToUpper(status[0], System.Globalization.CultureInfo.CurrentUICulture) + status[1..];
    }

    private void Copy()
    {
        if (Selected is null)
        {
            return;
        }

        try
        {
            Clipboard.SetDataObject(Selected.ToClipboardText(), copy: true);
            CopyButtonText = Strings.Main_Copied;
            _copyFeedback.Stop();
            _copyFeedback.Start();
        }
        catch (ExternalException ex)
        {
            Status = Display.Format(Strings.Main_CopyFailed, ex.Message);
        }
    }

    private void SetBusy(bool history, bool searching)
    {
        _isLoadingHistory = history;
        _isSearching = searching;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsSearching));
        CommandManager.InvalidateRequerySuggested();
    }
}
