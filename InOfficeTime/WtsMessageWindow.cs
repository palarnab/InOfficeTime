using System.Windows.Forms;

namespace InOfficeTime;

/// <summary>Message-only window to receive WM_WTSSESSION_CHANGE (works from a Windows service as LocalSystem).</summary>
internal sealed class WtsMessageWindow : NativeWindow
{
    private readonly Action<string, int> _onSessionEvent;

    public WtsMessageWindow(Action<string, int> onSessionEvent) =>
        _onSessionEvent = onSessionEvent;

    public void CreateMessageOnly()
    {
        const nint HWND_MESSAGE = -3;
        var cp = new CreateParams
        {
            Caption = string.Empty,
            Parent = HWND_MESSAGE,
            Style = 0,
            ExStyle = 0
        };
        CreateHandle(cp);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WtsNative.WM_CLOSE)
        {
            WtsNative.PostQuitMessage(0);
            return;
        }

        if (m.Msg == WtsNative.WM_WTSSESSION_CHANGE && Handle != nint.Zero)
        {
            var reason = (int)m.WParam;
            var sessionId = m.LParam.ToInt32();
            var name = WtsNative.ReasonToName(reason);
            _onSessionEvent(name, sessionId);
        }

        base.WndProc(ref m);
    }
}
