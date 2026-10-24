using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using RebootWhy.App.Settings;
using RebootWhy.App.ViewModels;

namespace RebootWhy.App;

public partial class MainWindow : Window
{
    private const string RebootColumns = "reboots";
    private const string UpdateColumns = "updates";

    private readonly MainViewModel _viewModel;
    private readonly ViewState _view;

    public MainWindow(MainViewModel viewModel, ViewState view)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _view = view;
        DataContext = viewModel;
        Platform.WindowPlacement.Attach(this, view);
        RestoreColumns(RebootList, RebootColumns);
        RestoreColumns(UpdateList, UpdateColumns);

        // The column width is saved when the user releases the header gripper.
        RebootList.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => SaveColumns(RebootList, RebootColumns)));
        UpdateList.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => SaveColumns(UpdateList, UpdateColumns)));

        Loaded += async (_, _) =>
        {
            await viewModel.LoadAsync();

            // Refresh the update history in the background ahead of time: fresh data is usually ready by the time the tab opens.
            await Task.Delay(TimeSpan.FromSeconds(3));
            viewModel.Updates.EnsureLoaded();
        };
        double? updatesOffset = null;
        viewModel.Updates.Rebuilding += () => updatesOffset = Scroller(UpdateList)?.VerticalOffset;
        viewModel.Updates.Rebuilt += userInitiated => AfterRebuild(UpdateList, userInitiated ? null : updatesOffset);
        double? rebootsOffset = null;
        viewModel.RecordsRebuilding += () => rebootsOffset = Scroller(RebootList)?.VerticalOffset;
        viewModel.RecordsRebuilt += newSelection => AfterRebuild(RebootList, newSelection ? null : rebootsOffset);
    }

    /// <summary>
    /// After the list is rebuilt: for a background refresh the scroll position is restored;
    /// for a user action or a new selection the selected row is shown.
    /// Runs after the list has built its rows; with groups, scrolling earlier has no effect.
    /// </summary>
    private static void AfterRebuild(ListView list, double? keepOffset) => list.Dispatcher.InvokeAsync(
        () =>
        {
            if (keepOffset is { } offset && Scroller(list) is { } scroller)
            {
                scroller.ScrollToVerticalOffset(offset);
            }
            else if (list.SelectedItem is { } item)
            {
                list.ScrollIntoView(item);
            }
        },
        DispatcherPriority.Background);

    private static ScrollViewer? Scroller(DependencyObject element)
    {
        if (element is ScrollViewer scroller)
        {
            return scroller;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (Scroller(VisualTreeHelper.GetChild(element, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The title column takes all free space, so titles are fully visible in a maximized window.</summary>
    private void FitTitleColumn(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ListView { View: GridView view } list)
        {
            return;
        }

        // Until the user resizes columns, the title column takes the free space.
        if (_view.ColumnWidths.ContainsKey(list == UpdateList ? UpdateColumns : RebootColumns))
        {
            return;
        }

        var stretch = list == UpdateList ? UpdateTitleColumn : RebootTitleColumn;
        var others = view.Columns.Where(c => c != stretch).Sum(c => c.ActualWidth);
        stretch.Width = Math.Max(200, list.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 10);
    }

    private void RestoreColumns(ListView list, string key)
    {
        if (list.View is GridView view && _view.ColumnWidths.TryGetValue(key, out var widths) && widths.Length == view.Columns.Count)
        {
            for (var i = 0; i < widths.Length; i++)
            {
                view.Columns[i].Width = widths[i];
            }
        }
    }

    private void SaveColumns(ListView list, string key)
    {
        if (list.View is GridView view)
        {
            _view.ColumnWidths[key] = view.Columns.Select(c => Math.Round(c.ActualWidth)).ToArray();
            ViewStateStore.Save(_view);
        }
    }

    /// <summary>The update history is read when the tab is first opened; until then Windows Update is left alone.</summary>
    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && Tabs.SelectedIndex == 1)
        {
            _viewModel.Updates.EnsureLoaded();
        }
    }
}
