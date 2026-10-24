using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace RebootWhy.App.Infrastructure;

/// <summary>
/// Text in which web addresses become links: <c>&lt;TextBlock infra:LinkText.Text="{Binding Value}" /&gt;</c>.
/// A link opens in the default browser.
/// </summary>
public static partial class LinkText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(LinkText), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block)
        {
            return;
        }

        block.Inlines.Clear();
        var text = e.NewValue as string ?? string.Empty;
        var position = 0;
        foreach (Match match in Url().Matches(text))
        {
            if (match.Index > position)
            {
                block.Inlines.Add(new Run(text[position..match.Index]));
            }

            // A period or parenthesis at the end of a sentence is not part of the address.
            var address = match.Value.TrimEnd('.', ',', ';', ':', ')', '»');
            var link = new Hyperlink(new Run(address)) { NavigateUri = new Uri(address), ToolTip = address };
            link.RequestNavigate += Open;
            block.Inlines.Add(link);
            if (address.Length < match.Value.Length)
            {
                block.Inlines.Add(new Run(match.Value[address.Length..]));
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            block.Inlines.Add(new Run(text[position..]));
        }
    }

    private static void Open(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    [GeneratedRegex(@"https?://[^\s<>""«»]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
}
