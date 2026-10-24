using RebootWhy.Core.Properties;

namespace RebootWhy.Core.Analysis;

/// <summary>Parsed User32 event 1074: who requested the restart and how.</summary>
public sealed record ShutdownRequest(
    string ProcessPath,
    string ProcessName,
    string? User,
    ShutdownReason? Reason,
    ShutdownKind Kind,
    string? Comment)
{
    /// <summary>The request was made on behalf of a real user, not a system account (NT AUTHORITY\SYSTEM and the like).</summary>
    public bool IsOnBehalfOfUser => User is not null && !User.StartsWith(@"NT AUTHORITY\", StringComparison.OrdinalIgnoreCase);

    public static ShutdownRequest Parse(SystemEvent e)
    {
        // EventData: param1 — process, param4 — reason code, param5 — shutdown type (localized), param6 — comment, param7 — user.
        var path = ProcessClassifier.StripComputerName(e.Get("param1") ?? string.Empty);
        var name = ProcessClassifier.FileName(path);
        var code = e.GetInt64("param4");

        return new ShutdownRequest(
            path.Length > 0 ? path : Strings.UnknownProcess,
            name.Length > 0 ? name : Strings.UnknownProcess,
            NullIfBlank(e.Get("param7")),
            code is long value ? new ShutdownReason((uint)value) : null,
            ParseKind(e.Get("param5")),
            NullIfBlank(e.Get("param6")));
    }

    /// <summary>
    /// Windows writes the shutdown type in its display language. Only the English values are recognized here;
    /// for other languages the classifier infers the type from how long the computer stayed off.
    /// </summary>
    public static ShutdownKind ParseKind(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Contains("restart"))
        {
            return ShutdownKind.Restart;
        }

        if (value.Contains("power off") || value.Contains("shutdown"))
        {
            return ShutdownKind.PowerOff;
        }

        return ShutdownKind.Unknown;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
