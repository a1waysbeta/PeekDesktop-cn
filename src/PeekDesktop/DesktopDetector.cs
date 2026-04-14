using System;

namespace PeekDesktop;

internal enum DesktopClickTarget
{
    NonDesktop,
    DesktopBackground,
    DesktopIcon
}

/// <summary>
/// Identifies whether a window handle belongs to the Windows desktop surface
/// (Progman, WorkerW with SHELLDLL_DefView, or transient desktop UI like menus).
/// </summary>
public static class DesktopDetector
{
    /// <summary>
    /// Classifies a click as hitting the desktop wallpaper, a desktop icon,
    /// or something unrelated to the desktop.
    /// </summary>
    internal static DesktopClickTarget GetClickTarget(IntPtr hwnd, NativeMethods.POINT point)
    {
        if (!IsDesktopRelatedWindow(hwnd))
            return DesktopClickTarget.NonDesktop;

        if (IsDesktopIconWindow(hwnd) && NativeMethods.TryGetAccessibleDetailsAtPoint(point, out int role, out string name))
        {
            AppDiagnostics.Log($"Desktop accessibility hit-test: role=0x{role:X} name=\"{name}\"");
            if (role == NativeMethods.ROLE_SYSTEM_LISTITEM)
                return DesktopClickTarget.DesktopIcon;
        }

        return DesktopClickTarget.DesktopBackground;
    }

    /// <summary>
    /// Checks whether the given window is part of the desktop surface
    /// by walking up its parent chain looking for Progman or the desktop WorkerW.
    /// </summary>
    public static bool IsDesktopRelatedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr current = hwnd;
        while (current != IntPtr.Zero)
        {
            string className = NativeMethods.GetWindowClassName(current);

            if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                // Only the WorkerW that hosts SHELLDLL_DefView is the actual desktop.
                // Other WorkerW windows are unrelated shell helpers.
                IntPtr shellView = NativeMethods.FindWindowEx(
                    current, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView != IntPtr.Zero)
                    return true;
            }

            current = NativeMethods.GetParent(current);
        }

        return false;
    }

    /// <summary>
    /// Returns true if the given foreground window should be treated as
    /// "the user is still on the desktop" — preventing a restore trigger.
    /// This includes the desktop itself plus transient UI (menus, tooltips).
    /// </summary>
    public static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return true; // No foreground window — stay peeking

        string className = NativeMethods.GetWindowClassName(hwnd);

        // Context menus and tooltips are transient — treat as "still on desktop"
        if (string.Equals(className, "#32768", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(className, "tooltips_class32", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsDesktopRelatedWindow(hwnd);
    }

    private static bool IsDesktopIconWindow(IntPtr hwnd)
    {
        IntPtr current = hwnd;
        while (current != IntPtr.Zero)
        {
            string className = NativeMethods.GetWindowClassName(current);
            if (string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase))
                return true;

            current = NativeMethods.GetParent(current);
        }

        return false;
    }
}
