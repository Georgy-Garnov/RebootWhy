using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RebootWhy.App.Settings;

namespace RebootWhy.App.Platform;

/// <summary>
/// Remembers the window size, position, monitor and maximized state and restores them on the next start.
/// Coordinates are screen pixels (as Windows sees them), so they work with several monitors of different scale.
/// Before restoring, it checks that the monitor still exists and the window fits into its work area.
/// </summary>
public sealed class WindowPlacement
{
    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly Window _window;
    private readonly ViewState _state;
    private readonly DispatcherTimer _saveDelay = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// The window reports a move already while being created, to the default Windows position. Until the saved position
    /// is applied, such messages are ignored; otherwise they would overwrite it.
    /// </summary>
    private bool _restored;

    private WindowPlacement(Window window, ViewState state)
    {
        _window = window;
        _state = state;
        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            ViewStateStore.Save(_state);
        };
    }

    /// <summary>Attach to a window before it is first shown.</summary>
    public static void Attach(Window window, ViewState state)
    {
        var placement = new WindowPlacement(window, state);
        if (state.WindowWidth is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        window.SourceInitialized += (_, _) =>
        {
            placement.Restore();
            placement._restored = true;
        };
        window.LocationChanged += (_, _) => placement.Remember();
        window.SizeChanged += (_, _) => placement.Remember();
        window.StateChanged += (_, _) => placement.Remember();
        window.IsVisibleChanged += (_, _) => placement.SaveNow();
        window.Closing += (_, _) => placement.SaveNow();
    }

    private nint Handle => new WindowInteropHelper(_window).Handle;

    private void Restore()
    {
        if (_state.WindowWidth is not { } width || _state.WindowHeight is not { } height
            || _state.WindowX is not { } x || _state.WindowY is not { } y || Handle == 0)
        {
            return;
        }

        var saved = new Rect32 { Left = x, Top = y, Right = x + width, Bottom = y + height };

        // The monitor the window was on may be gone; center it on the primary screen then.
        var monitor = MonitorFromRect(ref saved, MonitorDefaultToNull);
        var keepPosition = monitor != 0;
        if (!keepPosition)
        {
            monitor = MonitorFromRect(ref saved, MonitorDefaultToPrimary);
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var area = info.Work;
        width = Math.Min(width, area.Right - area.Left);
        height = Math.Min(height, area.Bottom - area.Top);
        if (keepPosition)
        {
            // The whole window inside the work area: not off the screen edge and not under the taskbar.
            x = Math.Clamp(x, area.Left, area.Right - width);
            y = Math.Clamp(y, area.Top, area.Bottom - height);
        }
        else
        {
            x = area.Left + ((area.Right - area.Left - width) / 2);
            y = area.Top + ((area.Bottom - area.Top - height) / 2);
        }

        // On first show WPF applies its own Left/Top/Width/Height, so set them,
        // converting screen pixels to WPF units with the screen scale.
        if (PresentationSource.FromVisual(_window)?.CompositionTarget is { } target)
        {
            var toWpf = target.TransformFromDevice;
            var topLeft = toWpf.Transform(new Point(x, y));
            var size = toWpf.Transform(new Vector(width, height));
            _window.Left = topLeft.X;
            _window.Top = topLeft.Y;
            _window.Width = size.X;
            _window.Height = size.Y;
        }
        else
        {
            SetWindowPos(Handle, 0, x, y, width, height, SwpNoZOrder | SwpNoActivate);
        }

        if (_state.WindowMaximized)
        {
            // The window is already on the right monitor and will be maximized there.
            _window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Size and position are remembered only in the normal state: a maximized window's bounds are not its own.</summary>
    private void Remember()
    {
        if (!_restored || Handle == 0 || !_window.IsVisible)
        {
            return;
        }

        _state.WindowMaximized = _window.WindowState == WindowState.Maximized;
        if (_window.WindowState == WindowState.Normal && GetWindowRect(Handle, out var rect))
        {
            _state.WindowX = rect.Left;
            _state.WindowY = rect.Top;
            _state.WindowWidth = rect.Right - rect.Left;
            _state.WindowHeight = rect.Bottom - rect.Top;
            _state.WindowMonitor = MonitorName(rect);
        }

        _saveDelay.Stop();
        _saveDelay.Start();
    }

    private void SaveNow()
    {
        if (!_restored)
        {
            return;
        }

        Remember();
        _saveDelay.Stop();
        ViewStateStore.Save(_state);
    }

    private static string? MonitorName(Rect32 rect)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromRect(ref rect, MonitorDefaultToPrimary), ref info) ? info.Device : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref Rect32 rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect32 rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
