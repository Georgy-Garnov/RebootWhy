using System.Security;
using Microsoft.Win32;
using RebootWhy.App.Properties;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.Platform;

/// <summary>Windows Update policies RebootWhy can show and change.</summary>
/// <param name="AutoUpdateOption">AUOptions: null = not set (Windows decides), 2 = notify before downloading, 3 = download and notify before installing, 4 = install on schedule.</param>
public sealed record UpdatePolicyValues(bool NoAutoRebootWithLoggedOnUsers, bool ExcludeDrivers, int? AutoUpdateOption);

/// <param name="Configured">What is written in the policies.</param>
/// <param name="SeenByWindowsUpdate">What the Windows Update service actually uses (its policy cache); null if it could not be read.</param>
public sealed record UpdatePolicyState(UpdatePolicyValues Configured, UpdatePolicyValues? SeenByWindowsUpdate, ActiveHours? ActiveHours);

public static class UpdatePolicies
{
    private const string PoliciesKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string CacheKey = @"SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache";

    public static UpdatePolicyState Read() => new(
        ReadValues(PoliciesKey),
        ReadCache(),
        ReadActiveHours());

    /// <summary>
    /// Writes the policies and restarts the Windows Update services: without a restart the service keeps working
    /// with the old policy cache (verified on a real machine on 2026-10-24).
    /// </summary>
    public static Task<bool> ApplyAsync(UpdatePolicyValues values)
    {
        var autoUpdate = values.AutoUpdateOption is { } option
            ? $"""
              Set-ItemProperty -Path $au -Name NoAutoUpdate -Type DWord -Value 0
              Set-ItemProperty -Path $au -Name AUOptions -Type DWord -Value {option}
              """
            : """
              Remove-ItemProperty -Path $au -Name AUOptions -ErrorAction SilentlyContinue
              Remove-ItemProperty -Path $au -Name NoAutoUpdate -ErrorAction SilentlyContinue
              """;

        var script = $"""
            $ErrorActionPreference = 'Stop'
            $wu = 'HKLM:\{PoliciesKey}'
            $au = "$wu\AU"
            if (-not (Test-Path $au)) {"{"} New-Item -Path $au -Force | Out-Null {"}"}
            {Flag("$au", "NoAutoRebootWithLoggedOnUsers", values.NoAutoRebootWithLoggedOnUsers)}
            {Flag("$wu", "ExcludeWUDriversInQualityUpdate", values.ExcludeDrivers)}
            {autoUpdate}
            Restart-Service -Name UsoSvc, wuauserv -Force -ErrorAction SilentlyContinue
            exit 0
            """;
        return Elevation.RunPowerShellAsync(script);
    }

    public static string AutoUpdateText(int? option) => option switch
    {
        null => Strings.Policy_AutoUpdate_NotSet,
        2 => Strings.Policy_AutoUpdate_Notify,
        3 => Strings.Policy_AutoUpdate_Download,
        4 => Strings.Policy_AutoUpdate_Install,
        _ => Display.Format(Strings.Policy_AutoUpdate_Value, option),
    };

    private static string Flag(string path, string name, bool enabled) => enabled
        ? $"Set-ItemProperty -Path {path} -Name {name} -Type DWord -Value 1"
        : $"Remove-ItemProperty -Path {path} -Name {name} -ErrorAction SilentlyContinue";

    private static UpdatePolicyValues ReadValues(string root) => new(
        ReadDword(root + @"\AU", "NoAutoRebootWithLoggedOnUsers") == 1,
        ReadDword(root, "ExcludeWUDriversInQualityUpdate") == 1,
        ReadDword(root + @"\AU", "NoAutoUpdate") == 1 ? 1 : ReadDword(root + @"\AU", "AUOptions"));

    private static UpdatePolicyValues? ReadCache()
    {
        var active = ReadDword(CacheKey, "ActiveCache");
        return active is null ? null : ReadValues($@"{CacheKey}\CacheSet{active:D3}\WindowsUpdate");
    }

    private static ActiveHours? ReadActiveHours()
    {
        const string settings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        return ReadDword(PoliciesKey, "SetActiveHours") == 1
            && ReadDword(PoliciesKey, "ActiveHoursStart") is int policyStart
            && ReadDword(PoliciesKey, "ActiveHoursEnd") is int policyEnd
                ? new ActiveHours(policyStart, policyEnd)
                : ReadDword(settings, "ActiveHoursStart") is int start && ReadDword(settings, "ActiveHoursEnd") is int end
                    ? new ActiveHours(start, end)
                    : null;
    }

    private static int? ReadDword(string keyPath, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }
}
