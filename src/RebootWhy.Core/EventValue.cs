using System.Globalization;
using System.Text.RegularExpressions;

namespace RebootWhy.Core;

/// <summary>Parses EventData values: numbers come as decimal or hex, timestamps with nanoseconds.</summary>
public static partial class EventValue
{
    private static readonly char[] TrimChars = [' ', '\t', '\u200E', '\u200F'];

    public static long? ParseInt64(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim(TrimChars);
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        }

        if (bool.TryParse(value, out var flag))
        {
            return flag ? 1 : 0;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public static DateTimeOffset? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Kernel-General writes 9 fractional digits, while .NET parses at most 7.
        var value = ExcessFraction().Replace(raw.Trim(TrimChars), "$1");
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var time)
            ? time
            : null;
    }

    [GeneratedRegex(@"(\.\d{7})\d+")]
    private static partial Regex ExcessFraction();
}
