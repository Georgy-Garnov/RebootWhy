using System.ServiceProcess;
using Microsoft.Win32;
using RebootWhy.App.Properties;
using RebootWhy.Core.Text;

namespace RebootWhy.App.Platform;

/// <summary>Installs and removes the RebootWhy service. Requires administrator rights: Windows shows a UAC prompt.</summary>
public static class ServiceSetup
{
    public const string ServiceName = "RebootWhy";

    /// <summary>
    /// Service permissions: the defaults plus the interactive user (IU) may start and stop it (RP, WP,
    /// and DT to enumerate dependent services: without it stopping via ServiceController and Stop-Service fails).
    /// This way the program starts the service at launch and stops it on "Exit and stop the service" without a UAC prompt.
    /// Only administrators can still change or delete the service.
    /// </summary>
    private const string Permissions = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    /// <summary>Shown in services.msc in the language chosen when the service was installed.</summary>
    private static string DisplayName => PowerShellLiteral(Strings.Service_DisplayName);

    private static string Description => PowerShellLiteral(Strings.Service_Description);

    public static string StatusText
    {
        get
        {
            using var service = Find();
            if (service is null)
            {
                return Strings.Service_State_NotInstalled;
            }

            var state = service.Status switch
            {
                ServiceControllerStatus.Running => Strings.Service_State_Running,
                ServiceControllerStatus.Stopped => Strings.Service_State_Stopped,
                ServiceControllerStatus.StartPending => Strings.Service_State_Starting,
                ServiceControllerStatus.StopPending => Strings.Service_State_Stopping,
                _ => service.Status.ToString(),
            };
            return InstalledPath is { } path && !path.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase)
                ? Display.Format(Strings.Service_State_OtherFolder, state, path)
                : state;
        }
    }

    public static bool IsInstalled
    {
        get
        {
            using var service = Find();
            return service is not null;
        }
    }

    private static string? InstalledPath
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            return key?.GetValue("ImagePath") as string;
        }
    }

    /// <summary>Installs (or reinstalls) the service from the current exe and starts it.</summary>
    public static Task<bool> InstallAsync() => RunElevatedAsync($"""
        $ErrorActionPreference = 'Stop'
        if (Get-Service -Name '{ServiceName}' -ErrorAction SilentlyContinue) {"{"}
            Stop-Service -Name '{ServiceName}' -Force -ErrorAction SilentlyContinue
            sc.exe delete '{ServiceName}' | Out-Null
            Start-Sleep -Seconds 2
        {"}"}
        New-Service -Name '{ServiceName}' -BinaryPathName '"{Environment.ProcessPath}" {Program.ServiceArgument}' -DisplayName '{DisplayName}' -Description '{Description}' -StartupType Automatic | Out-Null
        sc.exe failure '{ServiceName}' reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
        sc.exe sdset '{ServiceName}' '{Permissions}' | Out-Null
        Start-Service -Name '{ServiceName}'
        """);

    public static bool IsRunning
    {
        get
        {
            using var service = Find();
            return service?.Status == ServiceControllerStatus.Running;
        }
    }

    /// <summary>Starts the installed service if it is stopped. Without the right (installed by an older version) silently does nothing.</summary>
    public static void StartIfStopped()
    {
        using var service = Find();
        if (service is not { Status: ServiceControllerStatus.Stopped })
        {
            return;
        }

        try
        {
            service.Start();
        }
        catch (InvalidOperationException)
        {
            // No right to start the service: the program checks Windows Update itself.
        }
    }

    /// <summary>Stops the service: first without UAC (the right is granted at installation), otherwise with an administrator prompt.</summary>
    public static async Task<bool> StopAsync()
    {
        using (var service = Find())
        {
            if (service is null || service.Status == ServiceControllerStatus.Stopped)
            {
                return true;
            }

            try
            {
                // Like "sc stop": without enumerating dependent services, which the user may have no right to do
                // (the service may have been installed before that right was granted).
                using var stop = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", $"stop {ServiceName}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                stop?.WaitForExit(10000);
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
            {
                // The service was installed without the user's right to stop it: ask the administrator.
            }
        }

        return await RunElevatedAsync($"Stop-Service -Name '{ServiceName}' -Force");
    }

    public static Task<bool> UninstallAsync() => RunElevatedAsync($"""
        Stop-Service -Name '{ServiceName}' -Force -ErrorAction SilentlyContinue
        sc.exe delete '{ServiceName}' | Out-Null
        exit $LASTEXITCODE
        """);

    private static ServiceController? Find() =>
        ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase));

    private static Task<bool> RunElevatedAsync(string script) => Elevation.RunPowerShellAsync(script);

    /// <summary>The text goes inside single quotes in the PowerShell script.</summary>
    private static string PowerShellLiteral(string text) => text.Replace("'", "''");
}
