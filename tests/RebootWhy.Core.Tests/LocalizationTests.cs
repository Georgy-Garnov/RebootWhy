using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace RebootWhy.Core.Tests;

public partial class LocalizationTests
{
    public static TheoryData<string> Languages => new() { "ru", "uk", "hy" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryStringIsTranslated_WithTheSamePlaceholders(string language)
    {
        var manager = Core.Properties.Strings.ResourceManager;
        var neutral = Read(manager, CultureInfo.InvariantCulture);
        var translated = Read(manager, CultureInfo.GetCultureInfo(language));

        Assert.Empty(neutral.Keys.Except(translated.Keys));
        foreach (var (key, english) in neutral)
        {
            var text = translated[key];
            Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: {key} is empty");
            Assert.True(Placeholders(english).SequenceEqual(Placeholders(text)), $"{language}: {key} has different placeholders");
        }
    }

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        // tryParents: false — only the strings of this language, without falling back to English.
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for {culture.Name}");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Order();

    [GeneratedRegex(@"\{\d+[^}]*\}")]
    private static partial Regex Placeholder();
}
