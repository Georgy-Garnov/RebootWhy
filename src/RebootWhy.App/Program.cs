using System.ServiceProcess;
using RebootWhy.App.ServiceHost;
using RebootWhy.App.Settings;

namespace RebootWhy.App;

/// <summary>
/// One exe, three modes: the window (default), the tray icon at Windows sign-in (--tray) and the service (--service).
/// </summary>
internal static class Program
{
    public const string TrayArgument = "--tray";
    public const string ServiceArgument = "--service";

    /// <summary>Started by the previous instance that is restarting to switch the language: wait until it exits.</summary>
    public const string RestartArgument = "--restart";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains(ServiceArgument, StringComparer.OrdinalIgnoreCase))
        {
            // The service sends language-neutral data; any text it produces itself (logs, errors) is English.
            Localization.UseEnglish();
            ServiceBase.Run(new RebootWhyService());
            return 0;
        }

        using var instance = new Mutex(initiallyOwned: true, @"Local\RebootWhy.App", out var isFirstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\RebootWhy.ShowWindow");
        if (!isFirstInstance && args.Contains(RestartArgument, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                isFirstInstance = instance.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true;
            }
        }

        if (!isFirstInstance)
        {
            showSignal.Set();
            return 0;
        }

        Localization.Apply(SettingsStore.Load().Language);
        var app = new App(startHidden: args.Contains(TrayArgument, StringComparer.OrdinalIgnoreCase), showSignal);
        app.InitializeComponent();
        return app.Run();
    }
}
