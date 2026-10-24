using System.Runtime.InteropServices;

namespace RebootWhy.App.Platform;

/// <summary>
/// A "display required" power request (like a video player's). The USO log shows it as "System display requested":
/// Windows Update considers the user present and postpones the restart. The request is visible in powercfg /requests.
/// </summary>
public sealed class UserPresenceKeeper : IDisposable
{
    private nint _request;

    public bool IsActive => _request != 0;

    public void Activate(string reason)
    {
        if (IsActive)
        {
            return;
        }

        var context = new ReasonContext { Version = 0, Flags = PowerRequestContextSimpleString, SimpleReasonString = reason };
        var request = PowerCreateRequest(ref context);
        if (request == 0 || request == -1)
        {
            return;
        }

        if (PowerSetRequest(request, PowerRequestType.DisplayRequired) && PowerSetRequest(request, PowerRequestType.SystemRequired))
        {
            _request = request;
        }
        else
        {
            CloseHandle(request);
        }
    }

    public void Release()
    {
        if (!IsActive)
        {
            return;
        }

        PowerClearRequest(_request, PowerRequestType.DisplayRequired);
        PowerClearRequest(_request, PowerRequestType.SystemRequired);
        CloseHandle(_request);
        _request = 0;
    }

    public void Dispose() => Release();

    private const uint PowerRequestContextSimpleString = 0x1;

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(nint request, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(nint request, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
