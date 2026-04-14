using System;

namespace PeekDesktop;

public sealed class FocusChangedEventArgs : EventArgs
{
    public IntPtr ForegroundWindow { get; }

    public FocusChangedEventArgs(IntPtr foregroundWindow)
    {
        ForegroundWindow = foregroundWindow;
    }
}

/// <summary>
/// Monitors system-wide foreground window changes using SetWinEventHook.
/// Must be started on a thread with a message loop.
/// </summary>
public sealed class FocusWatcher : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;

    // Must be stored as a field to prevent GC collection.
    private NativeMethods.WinEventDelegate? _winEventProc;

    /// <summary>
    /// Raised when a different window becomes the foreground window.
    /// </summary>
    public event EventHandler<FocusChangedEventArgs>? FocusChanged;

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _winEventProc = WinEventCallback;
        _hookId = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
        AppDiagnostics.Log($"Focus watcher started: 0x{_hookId.ToInt64():X}");
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookId);
            AppDiagnostics.Log($"Focus watcher stopped: 0x{_hookId.ToInt64():X}");
            _hookId = IntPtr.Zero;
        }
    }

    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            AppDiagnostics.LogWindow("Foreground event", hwnd);
            FocusChanged?.Invoke(this, new FocusChangedEventArgs(hwnd));
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
