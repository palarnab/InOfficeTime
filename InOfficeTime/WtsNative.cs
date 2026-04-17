using System.Runtime.InteropServices;

namespace InOfficeTime;

internal static class WtsNative
{
    public const int NOTIFY_FOR_THIS_SESSION = 0;
    public const int NOTIFY_FOR_ALL_SESSIONS = 1;

    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_CLOSE = 0x0010;

    public const int WTS_CONSOLE_CONNECT = 0x1;
    public const int WTS_CONSOLE_DISCONNECT = 0x2;
    public const int WTS_REMOTE_CONNECT = 0x3;
    public const int WTS_REMOTE_DISCONNECT = 0x4;
    public const int WTS_SESSION_LOGON = 0x5;
    public const int WTS_SESSION_LOGOFF = 0x6;
    public const int WTS_SESSION_LOCK = 0x7;
    public const int WTS_SESSION_UNLOCK = 0x8;
    public const int WTS_SESSION_REMOTE_CONTROL = 0x9;

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    public static string ReasonToName(int wParam) => wParam switch
    {
        WTS_CONSOLE_CONNECT => "ConsoleConnect",
        WTS_CONSOLE_DISCONNECT => "ConsoleDisconnect",
        WTS_REMOTE_CONNECT => "RemoteConnect",
        WTS_REMOTE_DISCONNECT => "RemoteDisconnect",
        WTS_SESSION_LOGON => "SessionLogon",
        WTS_SESSION_LOGOFF => "SessionLogoff",
        WTS_SESSION_LOCK => "SessionLock",
        WTS_SESSION_UNLOCK => "SessionUnlock",
        WTS_SESSION_REMOTE_CONTROL => "SessionRemoteControl",
        _ => $"Unknown({wParam})"
    };
}
