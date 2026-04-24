using System.Diagnostics;
using PeekDesktop;

internal static class Program
{
    private static int Main(string[] args)
    {
        int iterations = 10_000;
        if (args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0)
            iterations = parsed;

        var failures = new List<string>();

        RunTest("Invalid handle smoke", failures, InvalidHandleSmoke);
        RunTest("Process-name edge cases", failures, ProcessNameEdgeCases);
        RunTest("Version info smoke", failures, VersionInfoSmoke);
        RunTest("Notification state stress", failures, NotificationStateStress);
        RunTest($"Leak probe ({iterations} iterations)", failures, () => LeakProbe(iterations));

        if (failures.Count == 0)
        {
            Console.WriteLine("Interop harness passed.");
            return 0;
        }

        Console.Error.WriteLine("Interop harness failures:");
        foreach (string failure in failures)
            Console.Error.WriteLine($"- {failure}");

        return 1;
    }

    private static void RunTest(string name, List<string> failures, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.GetType().Name} {ex.Message}");
            Console.WriteLine($"[FAIL] {name}");
        }
    }

    private static void InvalidHandleSmoke()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        IntPtr[] handles =
        [
            IntPtr.Zero,
            new IntPtr(1),
            new IntPtr(-1),
            new IntPtr(0x1234),
            new IntPtr(0x12345678),
            foreground
        ];

        foreach (IntPtr hwnd in handles)
        {
            _ = NativeMethods.IsWindow(hwnd);
            _ = NativeMethods.IsWindowVisible(hwnd);
            _ = NativeMethods.IsIconic(hwnd);
            _ = NativeMethods.IsWindowCloaked(hwnd);
            _ = NativeMethods.GetWindowClassName(hwnd);
            _ = NativeMethods.GetWindowTitle(hwnd);
            _ = NativeMethods.DescribeWindow(hwnd);
            _ = NativeMethods.DescribeWindowHierarchy(hwnd, maxDepth: 4);
            _ = NativeMethods.GetWindowLongValue(hwnd, NativeMethods.GWL_EXSTYLE);
        }

        bool listViewHit = NativeMethods.TryIsDesktopListViewItemAtPoint(
            IntPtr.Zero,
            new NativeMethods.POINT { x = int.MaxValue, y = int.MinValue },
            out bool onItem);
        if (listViewHit && onItem)
            throw new InvalidOperationException("Unexpected desktop list-view hit on invalid hwnd.");
    }

    private static void ProcessNameEdgeCases()
    {
        if (NativeMethods.TryGetProcessName(0, out _))
            throw new InvalidOperationException("processId=0 should not resolve.");

        _ = NativeMethods.TryGetProcessName(uint.MaxValue, out _);
        _ = NativeMethods.TryGetProcessName((uint)Environment.ProcessId, out string processName);
        if (string.IsNullOrWhiteSpace(processName))
            throw new InvalidOperationException("Current process name was empty.");
    }

    private static void VersionInfoSmoke()
    {
        var version = NativeMethods.GetExeVersionInfo();
        if (version.ProductVersion is null && version.FileVersion is null)
            throw new InvalidOperationException("Both ProductVersion and FileVersion were null.");
    }

    private static void NotificationStateStress()
    {
        for (int i = 0; i < 2_000; i++)
            _ = NativeMethods.TryGetUserNotificationState(out _);
    }

    private static void LeakProbe(int iterations)
    {
        Process proc = Process.GetCurrentProcess();
        proc.Refresh();
        long privateBefore = proc.PrivateMemorySize64;
        int handlesBefore = proc.HandleCount;

        var rand = new Random(1337);
        for (int i = 0; i < iterations; i++)
        {
            IntPtr hwnd = i % 2 == 0 ? IntPtr.Zero : new IntPtr(rand.Next());
            _ = NativeMethods.GetWindowClassName(hwnd);
            _ = NativeMethods.GetWindowTitle(hwnd);
            _ = NativeMethods.IsWindowCloaked(hwnd);
            _ = NativeMethods.DescribeWindow(hwnd);
            _ = NativeMethods.TryGetProcessName((uint)rand.Next(1, int.MaxValue), out _);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        proc.Refresh();
        long privateAfter = proc.PrivateMemorySize64;
        int handlesAfter = proc.HandleCount;

        long privateGrowth = privateAfter - privateBefore;
        int handleGrowth = handlesAfter - handlesBefore;

        const long maxPrivateGrowthBytes = 64L * 1024 * 1024;
        const int maxHandleGrowth = 16;

        if (privateGrowth > maxPrivateGrowthBytes)
        {
            throw new InvalidOperationException(
                $"Private bytes grew by {privateGrowth:N0} (> {maxPrivateGrowthBytes:N0}).");
        }

        if (handleGrowth > maxHandleGrowth)
        {
            throw new InvalidOperationException(
                $"Handle count grew by {handleGrowth} (> {maxHandleGrowth}).");
        }
    }
}
