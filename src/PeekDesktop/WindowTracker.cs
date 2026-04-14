using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PeekDesktop;

/// <summary>
/// Captures the state of all visible top-level windows, minimizes them,
/// and restores them to their exact previous positions (including maximized state).
/// </summary>
public sealed class WindowTracker
{
    private readonly List<WindowInfo> _savedWindows = new();

    public bool HasWindows => _savedWindows.Count > 0;
    public int SavedWindowCount => _savedWindows.Count;

    /// <summary>
    /// Snapshot all visible, non-system top-level windows and their placements.
    /// </summary>
    public void CaptureWindows()
    {
        var stopwatch = Stopwatch.StartNew();
        _savedWindows.Clear();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (ShouldTrackWindow(hwnd))
            {
                var placement = new NativeMethods.WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
                if (NativeMethods.GetWindowPlacement(hwnd, ref placement))
                {
                    _savedWindows.Add(new WindowInfo(hwnd, placement));
                    AppDiagnostics.LogWindow("Captured window", hwnd);
                }
            }
            return true;
        }, IntPtr.Zero);

        AppDiagnostics.Log($"Capture complete: {_savedWindows.Count} window(s) saved");
        AppDiagnostics.Metric($"CaptureWindows: {_savedWindows.Count} window(s) in {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Minimize every captured window.
    /// </summary>
    public void MinimizeAll()
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var window in _savedWindows)
        {
            AppDiagnostics.LogWindow("Minimizing window", window.Handle);
            NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_MINIMIZE);
        }

        AppDiagnostics.Metric($"MinimizeAll: {_savedWindows.Count} window(s) in {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Restore every captured window to its saved placement.
    /// Restores bottom-to-top to preserve Z-order, and does NOT steal focus.
    /// </summary>
    public void RestoreAll()
    {
        var stopwatch = Stopwatch.StartNew();
        int restoredCount = 0;

        // Restore in reverse order (bottom windows first) to preserve Z-order
        for (int i = _savedWindows.Count - 1; i >= 0; i--)
        {
            var info = _savedWindows[i];

            // Skip windows that were destroyed while we were peeking
            if (!NativeMethods.IsWindow(info.Handle))
            {
                AppDiagnostics.LogWindow("Skipping destroyed window", info.Handle);
                continue;
            }

            var placement = info.Placement;
            AppDiagnostics.LogWindow("Restoring window", info.Handle);
            NativeMethods.SetWindowPlacement(info.Handle, ref placement);
            restoredCount++;
        }

        _savedWindows.Clear();
        AppDiagnostics.Log("Restore list cleared");
        AppDiagnostics.Metric($"RestoreAll: {restoredCount} window(s) in {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Determines whether a window should be captured for peek/restore.
    /// Filters out system chrome, invisible windows, tool windows, etc.
    /// </summary>
    private static bool ShouldTrackWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
            return false;

        if (NativeMethods.IsIconic(hwnd))
            return false;

        // Skip owned windows — they follow their owner
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
            return false;

        // Skip cloaked windows (other virtual desktops, hidden UWP apps)
        if (NativeMethods.IsWindowCloaked(hwnd))
            return false;

        string className = NativeMethods.GetWindowClassName(hwnd);
        if (string.IsNullOrEmpty(className))
            return false;

        // Skip shell and system windows
        if (IsExcludedClass(className))
            return false;

        // Skip tool windows (floating palettes, etc.)
        long exStyle = NativeMethods.GetWindowLongValue(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return false;
        if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
            return false;

        return true;
    }

    private static bool IsExcludedClass(string className)
    {
        return className switch
        {
            "Progman" => true,
            "WorkerW" => true,
            "Shell_TrayWnd" => true,
            "Shell_SecondaryTrayWnd" => true,
            "NotifyIconOverflowWindow" => true,
            "DV2ControlHost" => true,            // Start menu (Win10)
            "Windows.UI.Core.CoreWindow" => true, // Start, Action Center
            _ => false
        };
    }

    private record WindowInfo(IntPtr Handle, NativeMethods.WINDOWPLACEMENT Placement);
}
