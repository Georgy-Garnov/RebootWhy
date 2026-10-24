using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace RebootWhy.App.Infrastructure;

/// <summary>Restarts the program, for example to switch the UI language.</summary>
internal static class AppRestart
{
    /// <summary>
    /// Starts a new instance and shuts this one down. The new instance waits until this one releases
    /// the single-instance mutex (see <see cref="Program.RestartArgument"/>).
    /// </summary>
    /// <returns>false if the new instance could not be started; this one keeps running then.</returns>
    public static bool Restart()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, Program.RestartArgument) { UseShellExecute = false });
            if (process is null)
            {
                return false;
            }
        }
        catch (Win32Exception)
        {
            return false;
        }

        Application.Current.Shutdown();
        return true;
    }
}
