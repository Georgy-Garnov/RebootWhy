using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace RebootWhy.Core.Sources;

[SupportedOSPlatform("windows")]
public static class WindowsSystemInfo
{
    /// <summary>Time since the kernel started, excluding sleep and hibernation.</summary>
    public static TimeSpan? GetActiveTimeSinceKernelStart() =>
        QueryUnbiasedInterruptTime(out var hundredNanoseconds) ? TimeSpan.FromTicks((long)hundredNanoseconds) : null;

    /// <summary>Whether Fast Startup is enabled; null if it could not be determined.</summary>
    public static bool? IsFastStartupEnabled()
    {
        try
        {
            using var power = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            if (power?.GetValue("HibernateEnabled") is int hibernate && hibernate == 0)
            {
                return false;
            }

            using var sessionPower = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return sessionPower?.GetValue("HiberbootEnabled") is int hiberboot ? hiberboot != 0 : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
