using System.Drawing;
using System.Windows.Forms;
using RebootWhy.App.Properties;
using RebootWhy.Core.Updates;

namespace RebootWhy.App.Tray;

/// <summary>Notification area icon: green — all calm, red — Windows is waiting for a restart.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int MaxTooltipLength = 127;

    private readonly NotifyIcon _icon;
    private readonly ToolStripItem _stopSound;
    private readonly ToolStripItem _exitAndStopService;
    private readonly Icon _calm = Load("tray-calm.ico");
    private readonly Icon _pending = Load("tray-pending.ico");

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        _stopSound = menu.Items.Add(Strings.Tray_StopSound, null, (_, _) => StopSoundRequested?.Invoke());
        _stopSound.Visible = false;
        menu.Items.Add(Strings.Tray_Open, null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(Strings.Tray_CheckNow, null, (_, _) => AnalyzeRequested?.Invoke());
        menu.Items.Add(Strings.Tray_TestReaction, null, (_, _) => SimulationRequested?.Invoke());
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Exit, null, (_, _) => ExitRequested?.Invoke());
        _exitAndStopService = menu.Items.Add(Strings.Tray_ExitAndStopService, null, (_, _) => ExitAndStopServiceRequested?.Invoke());

        // The service item is shown only if the service is installed and running (checked when the menu opens).
        menu.Opening += (_, _) => _exitAndStopService.Visible = Platform.ServiceSetup.IsRunning;

        _icon = new NotifyIcon { Icon = _calm, Text = "RebootWhy", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
        _icon.BalloonTipClicked += (_, _) => NotificationClicked?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? AnalyzeRequested;

    public event Action? SimulationRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public event Action? ExitAndStopServiceRequested;

    public event Action? StopSoundRequested;

    public void SetSoundPlaying(bool playing) => _stopSound.Visible = playing;

    public event Action? NotificationClicked;

    public void Update(RebootForecast? forecast, bool keepingUserActive)
    {
        var pending = forecast is { State: not ForecastState.None };
        _icon.Icon = pending ? _pending : _calm;
        var text = forecast is null
            ? Strings.Tray_TipChecking
            : pending
                ? (forecast!.IsSimulation ? Strings.Tray_TipPendingTest : Strings.Tray_TipPending) + (keepingUserActive ? Strings.Tray_TipHoldOn : string.Empty)
                : Strings.Tray_TipNone;
        _icon.Text = text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
    }

    /// <summary>On Windows 10/11 this is shown as a regular Windows notification.</summary>
    public void ShowNotification(string title, string text) =>
        _icon.ShowBalloonTip(15000, title, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _calm.Dispose();
        _pending.Dispose();
    }

    private static Icon Load(string name)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream($"RebootWhy.App.Assets.{name}")
            ?? throw new InvalidOperationException($"Embedded icon {name} not found");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
