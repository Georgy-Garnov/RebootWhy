using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RebootWhy.App.Platform;

/// <summary>Runs a PowerShell script with administrator rights: Windows shows a UAC prompt.</summary>
public static class Elevation
{
    /// <returns>true if the script ran and exited with code 0; false if UAC was declined or an error occurred.</returns>
    public static async Task<bool> RunPowerShellAsync(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // The user declined the UAC prompt.
            return false;
        }
    }
}
