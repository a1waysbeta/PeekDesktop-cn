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
    /// Raised (on the UI thread) when a left-click on the desktop is detected.
    /// </summary>
    public event EventHandler? DesktopClicked;

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
    /// Only performs a lightweight WindowFromPoint check, then posts
    /// the real work to the message loop.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            IntPtr windowUnderCursor = NativeMethods.WindowFromPoint(hookStruct.pt);
            AppDiagnostics.LogWindow("Mouse click target", windowUnderCursor);

            if (DesktopDetector.IsDesktopRelatedWindow(windowUnderCursor))
            {
                // Post to message loop — don't block the hook callback
                _syncContext?.Post(_ => DesktopClicked?.Invoke(this, EventArgs.Empty), null);
            }
            else
            {
                _syncContext?.Post(_ => NonDesktopClicked?.Invoke(this, EventArgs.Empty), null);
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
    }
}
