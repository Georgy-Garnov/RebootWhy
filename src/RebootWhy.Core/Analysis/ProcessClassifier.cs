namespace RebootWhy.Core.Analysis;

public enum InitiatorCategory
{
    Application,
    WindowsUpdate,
    User,
    Command,
    Installer,
}

/// <summary>Determines who is behind the process from User32 event 1074.</summary>
public static class ProcessClassifier
{
    private static readonly HashSet<string> WindowsUpdateProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "MoUsoCoreWorker.exe", "UsoCoreWorker.exe", "usoclient.exe", "TrustedInstaller.exe", "TiWorker.exe",
        "wuauclt.exe", "MusNotification.exe", "MusNotificationUx.exe", "MoNotificationUx.exe", "sihclient.exe",
    };

    private static readonly HashSet<string> UserShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "winlogon.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe",
        "RuntimeBroker.exe", "LogonUI.exe",
    };

    private static readonly HashSet<string> CommandProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "shutdown.exe", "powershell.exe", "pwsh.exe", "cmd.exe", "wmiprvse.exe",
    };

    public static InitiatorCategory Classify(string fileName)
    {
        if (WindowsUpdateProcesses.Contains(fileName))
        {
            return InitiatorCategory.WindowsUpdate;
        }

        if (UserShellProcesses.Contains(fileName))
        {
            return InitiatorCategory.User;
        }

        if (CommandProcesses.Contains(fileName))
        {
            return InitiatorCategory.Command;
        }

        if (fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("install", StringComparison.OrdinalIgnoreCase))
        {
            return InitiatorCategory.Installer;
        }

        return InitiatorCategory.Application;
    }

    /// <summary>User32 records the process as "C:\path\app.exe (PC-NAME)"; strip the computer name.</summary>
    public static string StripComputerName(string raw)
    {
        var value = raw.Trim();
        var suffix = value.LastIndexOf(" (", StringComparison.Ordinal);
        return value.EndsWith(')') && suffix > 0 ? value[..suffix] : value;
    }

    /// <summary>File name from a Windows path, independent of the current OS separators.</summary>
    public static string FileName(string path)
    {
        var separator = path.LastIndexOfAny(['\\', '/']);
        return separator >= 0 ? path[(separator + 1)..] : path;
    }
}
