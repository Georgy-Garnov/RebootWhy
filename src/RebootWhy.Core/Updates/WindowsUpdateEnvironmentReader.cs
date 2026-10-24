using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace RebootWhy.Core.Updates;

/// <summary>Collects the Windows Update state. Each part is read separately, so one failing part does not affect the others.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsUpdateEnvironmentReader
{
    private const string PoliciesKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string UpdateOrchestratorFolder = @"\Microsoft\Windows\UpdateOrchestrator";

    private static readonly string[] OrchestratorTasks = ["Schedule Work", "Schedule Wake To Work", "Reboot_AC", "Reboot_Battery"];

    public static (UpdateEnvironment Environment, List<ForecastProblem> Problems) Read()
    {
        var problems = new List<ForecastProblem>();
        var environment = new UpdateEnvironment
        {
            ActiveHours = ReadActiveHours(),
            LastBootLocal = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64),
            WindowsUpdateRebootRequired = ReadWindowsUpdateRebootRequired(problems),
            ComponentServicingRebootPending = KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"),
            NoAutoRebootWithLoggedOnUsers = ReadDword(PoliciesKey + @"\AU", "NoAutoRebootWithLoggedOnUsers") == 1,
            ScheduledTasks = ReadScheduledTasks(),
        };

        return (environment, problems);
    }

    private static ActiveHours? ReadActiveHours()
    {
        // The policy takes precedence over the user setting.
        if (ReadDword(PoliciesKey, "SetActiveHours") == 1
            && ReadDword(PoliciesKey, "ActiveHoursStart") is int policyStart
            && ReadDword(PoliciesKey, "ActiveHoursEnd") is int policyEnd)
        {
            return new ActiveHours(policyStart, policyEnd);
        }

        const string settings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        return ReadDword(settings, "ActiveHoursStart") is int start && ReadDword(settings, "ActiveHoursEnd") is int end
            ? new ActiveHours(start, end)
            : null;
    }

    private static bool? ReadWindowsUpdateRebootRequired(List<ForecastProblem> problems)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.SystemInfo");
            if (type is null)
            {
                return null;
            }

            dynamic info = Activator.CreateInstance(type)!;
            return (bool)info.RebootRequired;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            problems.Add(new ForecastProblem(ForecastProblemKind.WindowsUpdateSilent, Detail: ex.Message));
            return null;
        }
    }

    /// <summary>UpdateOrchestrator tasks are visible to administrators only; without rights return null and take the schedule from the USO log.</summary>
    private static Dictionary<string, DateTime?>? ReadScheduledTasks()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
            {
                return null;
            }

            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            dynamic folder = service.GetFolder(UpdateOrchestratorFolder);
            var result = new Dictionary<string, DateTime?>();
            foreach (var name in OrchestratorTasks)
            {
                try
                {
                    DateTime next = folder.GetTask(name).NextRunTime;
                    result[name] = next.Year < 2000 ? null : next;
                }
                catch (Exception ex) when (ex is COMException or IOException)
                {
                    // The task may not exist on this Windows version.
                }
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static int? ReadDword(string keyPath, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static bool KeyExists(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key is not null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
