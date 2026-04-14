using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PeekDesktop;

/// <summary>
/// Installs a low-level mouse hook (WH_MOUSE_LL) and raises an event
/// when the user clicks on the desktop surface.
/// Must be installed on a thread with a message loop.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;

    // Must be stored as a field to prevent GC collection while the hook is active.
    private NativeMethods.LowLevelMouseProc? _hookProc;
    private SynchronizationContext? _syncContext;

    /// <summary>
    /// Raised (on the UI thread) when a left-click on empty desktop wallpaper is detected.
    /// </summary>
    public event EventHandler? DesktopClicked;

    /// <summary>
    /// Raised (on the UI thread) when a left-click lands on a desktop icon.
    /// </summary>
    public event EventHandler? DesktopIconClicked;

    /// <summary>
    /// Raised (on the UI thread) when a left-click lands on something other than the desktop.
    /// </summary>
    public event EventHandler? NonDesktopClicked;

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _syncContext = SynchronizationContext.Current;
        _hookProc = HookCallback;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);
        AppDiagnostics.Log($"Mouse hook installed: 0x{_hookId.ToInt64():X}");
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            AppDiagnostics.Log($"Mouse hook uninstalled: 0x{_hookId.ToInt64():X}");
            _hookId = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Hook callback — must return FAST to avoid Windows unhooking us.
    /// It captures the click point and posts the heavier classification
    /// work to the UI thread.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var clickPoint = hookStruct.pt;
            IntPtr windowUnderCursor = NativeMethods.WindowFromPoint(clickPoint);

            if (_syncContext is not null)
                _syncContext.Post(_ => HandleMouseClick(windowUnderCursor, clickPoint), null);
            else
                HandleMouseClick(windowUnderCursor, clickPoint);
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleMouseClick(IntPtr windowUnderCursor, NativeMethods.POINT clickPoint)
    {
        AppDiagnostics.LogWindow("Mouse click target", windowUnderCursor);
        DesktopClickTarget clickTarget = DesktopDetector.GetClickTarget(windowUnderCursor, clickPoint);
        AppDiagnostics.Log($"Mouse click classification: {clickTarget}");

        switch (clickTarget)
        {
            case DesktopClickTarget.DesktopBackground:
                DesktopClicked?.Invoke(this, EventArgs.Empty);
                break;

            case DesktopClickTarget.DesktopIcon:
                DesktopIconClicked?.Invoke(this, EventArgs.Empty);
                break;

            default:
                NonDesktopClicked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Dispose()
    {
        Uninstall();
    }
}
