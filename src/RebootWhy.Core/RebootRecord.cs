namespace RebootWhy.Core;

/// <summary>One boot of the computer and the conclusion about how the previous session ended.</summary>
public sealed record RebootRecord
{
    public required DateTimeOffset BootTimeUtc { get; init; }

    public required BootType BootType { get; init; }

    public required RebootCause Cause { get; init; }

    public required Confidence Confidence { get; init; }

    /// <summary>Short, localized conclusion for the user.</summary>
    public required string Title { get; init; }

    public ShutdownKind ShutdownKind { get; init; }

    /// <summary>When the previous session ended, if known (clean shutdowns only).</summary>
    public DateTimeOffset? ShutdownTimeUtc { get; init; }

    /// <summary>The process that requested the restart.</summary>
    public string? Initiator { get; init; }

    public string? User { get; init; }

    /// <summary>Decoded shutdown reason code or bugcheck code.</summary>
    public string? Reason { get; init; }

    public IReadOnlyList<string> Updates { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Log events the conclusion is based on.</summary>
    public IReadOnlyList<SystemEvent> Evidence { get; init; } = [];

    public bool IsFullBoot => BootType is not (BootType.FastStartup or BootType.HibernateResume);
}
