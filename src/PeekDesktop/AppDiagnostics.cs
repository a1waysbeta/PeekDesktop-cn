using System;
using System.Diagnostics;

namespace PeekDesktop;

internal static class AppDiagnostics
{
    public static void Log(string message)
    {
        Debug.WriteLine($"[PeekDesktop {DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public static void LogWindow(string prefix, IntPtr hwnd)
    {
        Log($"{prefix}: {NativeMethods.DescribeWindow(hwnd)}");
    }
}
