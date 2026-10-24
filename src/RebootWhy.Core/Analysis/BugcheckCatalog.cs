using RebootWhy.Core.Properties;
using RebootWhy.Core.Text;

namespace RebootWhy.Core.Analysis;

public sealed record BugcheckInfo(uint Code, string Name, string? Hint)
{
    /// <summary>The user held the power button while the system was not responding (Windows records this as a bugcheck).</summary>
    public bool IsPowerButtonHold => Code == BugcheckCatalog.ManuallyInitiatedPowerButtonHold;

    public string Describe() => Hint is null
        ? Display.Format(Strings.Bugcheck_CodeOnly, Code)
        : Display.Format(Strings.Bugcheck_CodeWithHint, Code, Hint);
}

/// <summary>
/// Names of common bugcheck (blue screen) codes, per Microsoft's "Bug Check Code Reference".
/// Explanations are localized resources named Bugcheck_{code in hex}.
/// </summary>
public static class BugcheckCatalog
{
    public const uint ManuallyInitiatedPowerButtonHold = 0x1C8;

    /// <summary>Codes like 0x1000007E are the same errors with extended parameters.</summary>
    private const uint ExtendedFlag = 0x10000000;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [0x0A] = "IRQL_NOT_LESS_OR_EQUAL",
        [0x19] = "BAD_POOL_HEADER",
        [0x1A] = "MEMORY_MANAGEMENT",
        [0x1E] = "KMODE_EXCEPTION_NOT_HANDLED",
        [0x24] = "NTFS_FILE_SYSTEM",
        [0x3B] = "SYSTEM_SERVICE_EXCEPTION",
        [0x4E] = "PFN_LIST_CORRUPT",
        [0x50] = "PAGE_FAULT_IN_NONPAGED_AREA",
        [0x77] = "KERNEL_STACK_INPAGE_ERROR",
        [0x7A] = "KERNEL_DATA_INPAGE_ERROR",
        [0x7B] = "INACCESSIBLE_BOOT_DEVICE",
        [0x7E] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
        [0x7F] = "UNEXPECTED_KERNEL_MODE_TRAP",
        [0x9C] = "MACHINE_CHECK_EXCEPTION",
        [0x9F] = "DRIVER_POWER_STATE_FAILURE",
        [0xA0] = "INTERNAL_POWER_ERROR",
        [0xBE] = "ATTEMPTED_WRITE_TO_READONLY_MEMORY",
        [0xC2] = "BAD_POOL_CALLER",
        [0xC5] = "DRIVER_CORRUPTED_EXPOOL",
        [0xD1] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
        [0xE2] = "MANUALLY_INITIATED_CRASH",
        [0xEA] = "THREAD_STUCK_IN_DEVICE_DRIVER",
        [0xEF] = "CRITICAL_PROCESS_DIED",
        [0xF4] = "CRITICAL_OBJECT_TERMINATION",
        [0xFC] = "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY",
        [0x101] = "CLOCK_WATCHDOG_TIMEOUT",
        [0x109] = "CRITICAL_STRUCTURE_CORRUPTION",
        [0x10E] = "VIDEO_MEMORY_MANAGEMENT_INTERNAL",
        [0x116] = "VIDEO_TDR_FAILURE",
        [0x117] = "VIDEO_TDR_TIMEOUT_DETECTED",
        [0x119] = "VIDEO_SCHEDULER_INTERNAL_ERROR",
        [0x124] = "WHEA_UNCORRECTABLE_ERROR",
        [0x133] = "DPC_WATCHDOG_VIOLATION",
        [0x139] = "KERNEL_SECURITY_CHECK_FAILURE",
        [0x13A] = "KERNEL_MODE_HEAP_CORRUPTION",
        [0x154] = "UNEXPECTED_STORE_EXCEPTION",
        [ManuallyInitiatedPowerButtonHold] = "MANUALLY_INITIATED_POWER_BUTTON_HOLD",
    };

    public static BugcheckInfo Describe(uint code)
    {
        var known = Names.ContainsKey(code) ? code
            : (code & ExtendedFlag) != 0 && Names.ContainsKey(code & ~ExtendedFlag) ? code & ~ExtendedFlag
            : (uint?)null;
        if (known is { } baseCode)
        {
            return new BugcheckInfo(code, Names[baseCode], Strings.ResourceManager.GetString($"Bugcheck_{baseCode:X}", Strings.Culture));
        }

        return new BugcheckInfo(code, code == 0 ? Strings.Bugcheck_NotRecorded : $"0x{code:X}", null);
    }
}
