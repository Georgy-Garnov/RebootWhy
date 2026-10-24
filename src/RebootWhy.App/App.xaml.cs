using System.Windows;

namespace RebootWhy.App;

public partial class App : Application
{
    private readonly bool _startHidden;
    private readonly EventWaitHandle _showSignal;
    private AppHost? _host;
    private RegisteredWaitHandle? _showWait;

    public App(bool startHidden, EventWaitHandle showSignal)
    {
        _startHidden = startHidden;
        _showSignal = showSignal;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = new AppHost(this);
        _host.Start(_startHidden);

        // Starting RebootWhy.exe again does not create a second instance; it asks this one to show the window.
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.InvokeAsync(() => _host.ShowMainWindow()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _host?.Dispose();
        base.OnExit(e);
    }
}
