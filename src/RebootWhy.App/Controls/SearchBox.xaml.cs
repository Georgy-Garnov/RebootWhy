using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RebootWhy.App.Controls;

public partial class SearchBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata(Properties.Strings.Search_Placeholder));

    public SearchBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var box = (SearchBox)element;
        var empty = string.IsNullOrEmpty(e.NewValue as string);
        box.Hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        box.ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Text = string.Empty;
        Input.Focus();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text))
        {
            Text = string.Empty;
            e.Handled = true;
        }
    }
}
