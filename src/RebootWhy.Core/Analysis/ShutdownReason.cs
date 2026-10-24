using RebootWhy.Core.Properties;
using RebootWhy.Core.Text;

namespace RebootWhy.Core.Analysis;

/// <summary>Shutdown reason code (SHTDN_REASON_*) from User32 event 1074.</summary>
public sealed record ShutdownReason(uint Code)
{
    private const uint PlannedFlag = 0x80000000;
    private const uint MajorOperatingSystem = 0x02;

    public bool Planned => (Code & PlannedFlag) != 0;

    public uint Major => (Code >> 16) & 0xFF;

    public uint Minor => Code & 0xFFFF;

    /// <summary>The reason is related to installing operating system updates.</summary>
    public bool IsServicing => Major == MajorOperatingSystem && Minor is 0x03 or 0x10 or 0x11 or 0x12;

    public string Describe() => Display.Format(
        Strings.Reason_Format,
        Planned ? Strings.Reason_Planned : Strings.Reason_Unplanned,
        Lookup($"ReasonMajor_{Major:X}") ?? Display.Format(Strings.ReasonMajor_Other, Major),
        Lookup($"ReasonMinor_{Minor:X2}") ?? Display.Format(Strings.ReasonMinor_Other, Minor),
        Code);

    private static string? Lookup(string key) => Strings.ResourceManager.GetString(key, Strings.Culture);
}
