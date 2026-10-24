namespace RebootWhy.Core.Analysis;

public sealed record UptimeSnapshot(
    DateTimeOffset LastStartUtc,
    BootType LastStartType,
    TimeSpan SinceLastStart,
    DateTimeOffset LastFullBootUtc,
    TimeSpan SinceLastFullBoot,
    TimeSpan? ActiveSinceFullBoot);

public static class UptimeCalculator
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(10);

    /// <param name="records">Boot history (any order).</param>
    /// <param name="sinceKernelStart">Environment.TickCount64: time since the kernel started, including sleep and fast-startup shutdowns.</param>
    /// <param name="activeSinceKernelStart">The same without sleep and hibernation, if known.</param>
    public static UptimeSnapshot Calculate(
        IReadOnlyList<RebootRecord> records,
        DateTimeOffset nowUtc,
        TimeSpan sinceKernelStart,
        TimeSpan? activeSinceKernelStart)
    {
        var kernelStart = nowUtc - sinceKernelStart;

        // If the latest boot is not in the log (narrow period, cleared log), rely on the kernel counter.
        var latest = records.MaxBy(r => r.BootTimeUtc);
        var (lastStart, lastType) = latest is not null && latest.BootTimeUtc >= kernelStart - Tolerance
            ? (latest.BootTimeUtc, latest.BootType)
            : (kernelStart, BootType.Cold);

        var lastFullBoot = records.Where(r => r.BootType == BootType.Cold).MaxBy(r => r.BootTimeUtc)?.BootTimeUtc;
        var fullBoot = lastFullBoot is { } logged && logged >= kernelStart - Tolerance ? logged : kernelStart;

        return new UptimeSnapshot(
            lastStart,
            lastType,
            NonNegative(nowUtc - lastStart),
            fullBoot,
            NonNegative(nowUtc - fullBoot),
            activeSinceKernelStart);
    }

    private static TimeSpan NonNegative(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;
}
