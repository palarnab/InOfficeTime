using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InOfficeTime;

/// <summary>STA message loop + WTS notifications forwarded to the log writer.</summary>
public sealed class SessionMonitorWorker : BackgroundService
{
    private readonly WorkTimeLogWriter _writer;
    private readonly ILogger<SessionMonitorWorker> _logger;
    private Thread? _thread;
    private nint _hwnd;

    public SessionMonitorWorker(WorkTimeLogWriter writer, ILogger<SessionMonitorWorker> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _thread = new Thread(() =>
        {
            try
            {
                ApplicationConfiguration.Initialize();
                var window = new WtsMessageWindow(OnEvent);
                window.CreateMessageOnly();
                _hwnd = window.Handle;

                if (!WtsNative.WTSRegisterSessionNotification(window.Handle, WtsNative.NOTIFY_FOR_ALL_SESSIONS))
                {
                    var err = Marshal.GetLastWin32Error();
                    _logger.LogError(
                        "WTSRegisterSessionNotification failed (win32={Win32}). Run as Local System for NOTIFY_FOR_ALL_SESSIONS.",
                        err);
                }
                else
                {
                    _logger.LogInformation("Session notifications registered (NOTIFY_FOR_ALL_SESSIONS).");
                }

                using (stoppingToken.Register(() =>
                       {
                           if (_hwnd != nint.Zero)
                               WtsNative.PostMessage(_hwnd, WtsNative.WM_CLOSE, nint.Zero, nint.Zero);
                       }))
                {
                    Application.Run();
                }

                if (window.Handle != nint.Zero)
                {
                    WtsNative.WTSUnRegisterSessionNotification(window.Handle);
                    try
                    {
                        window.DestroyHandle();
                    }
                    catch (InvalidOperationException)
                    {
                        // handle may already be destroyed
                    }
                }

                _hwnd = nint.Zero;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session monitor thread failed.");
            }
            finally
            {
                done.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "InOfficeTime WTS"
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return done.Task;
    }

    private void OnEvent(string name, int sessionId) =>
        _writer.Append(name, sessionId);
}
