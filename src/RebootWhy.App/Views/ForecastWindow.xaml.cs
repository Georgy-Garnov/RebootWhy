using System.Windows;
using RebootWhy.App.ViewModels;

namespace RebootWhy.App.Views;

public partial class ForecastWindow : Window
{
    public ForecastWindow(ForecastViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        Closed += (_, _) => viewModel.Detach();

        // An alert must stay visible above all windows; regular viewing behaves like a normal window.
        Topmost = viewModel.IsAlert && !viewModel.IsSample;
    }

    public ForecastViewModel ViewModel { get; }
}
