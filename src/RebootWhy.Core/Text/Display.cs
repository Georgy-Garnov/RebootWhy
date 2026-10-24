using System.Globalization;
using RebootWhy.Core.Properties;

namespace RebootWhy.Core.Text;

/// <summary>
/// Localized display texts for model values. The language comes from <see cref="CultureInfo.CurrentUICulture"/>;
/// date patterns come from the resources and are applied with the invariant culture, so a user's custom
/// time separator does not leak into the program.
/// </summary>
public static class Display
{
    public static string BootType(BootType type) => type switch
    {
        Core.BootType.Cold => Strings.BootType_Cold,
        Core.BootType.FastStartup => Strings.BootType_FastStartup,
        Core.BootType.HibernateResume => Strings.BootType_HibernateResume,
        _ => Strings.BootType_Unknown,
    };

    public static string Confidence(Confidence confidence) => confidence switch
    {
        Core.Confidence.High => Strings.Confidence_High,
        Core.Confidence.Medium => Strings.Confidence_Medium,
        _ => Strings.Confidence_Low,
    };

    /// <summary>For titles: "Restart by the user".</summary>
    public static string KindVerb(ShutdownKind kind) => kind switch
    {
        ShutdownKind.Restart => Strings.Kind_Restart,
        ShutdownKind.PowerOff => Strings.Kind_PowerOff,
        _ => Strings.Kind_Unknown,
    };

    /// <summary>For the middle of a sentence: "X requested a restart".</summary>
    public static string KindNoun(ShutdownKind kind) => kind switch
    {
        ShutdownKind.Restart => Strings.KindObject_Restart,
        ShutdownKind.PowerOff => Strings.KindObject_PowerOff,
        _ => Strings.KindObject_Unknown,
    };

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return Strings.Duration_LessThanMinute;
        }

        var parts = new List<string>(3);
        if (span.Days > 0)
        {
            parts.Add(Format(Strings.Duration_Days, span.Days));
        }

        if (span.Hours > 0)
        {
            parts.Add(Format(Strings.Duration_Hours, span.Hours));
        }

        if (span.Minutes > 0)
        {
            parts.Add(Format(Strings.Duration_Minutes, span.Minutes));
        }

        return string.Join(" ", parts);
    }

    /// <summary>Short local date and time for Windows Update forecasts, which are computed in local time.</summary>
    public static string ShortDateTime(DateTime local) => local.ToString(Strings.Format_ShortDateTime, Culture);

    public static string ShortTime(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string LocalTime(DateTimeOffset utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string TimeWithSeconds(DateTime local) => local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Date(DateTime local) => local.ToString(Strings.Format_Date, CultureInfo.InvariantCulture);

    public static string LocalDateTime(DateTimeOffset utc, bool withSeconds = false) =>
        utc.ToLocalTime().ToString(withSeconds ? Strings.Format_DateTimeSeconds : Strings.Format_DateTime, CultureInfo.InvariantCulture);

    /// <summary>Formats a resource string with the current UI culture.</summary>
    public static string Format(string pattern, params object?[] args) => string.Format(CultureInfo.CurrentUICulture, pattern, args);

    /// <summary>Month names in short dates must follow the UI language, everything else stays invariant.</summary>
    private static CultureInfo Culture => CultureInfo.CurrentUICulture.IsNeutralCulture
        ? CultureInfo.CreateSpecificCulture(CultureInfo.CurrentUICulture.Name)
        : CultureInfo.CurrentUICulture;
}
