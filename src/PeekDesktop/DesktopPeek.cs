using System;

namespace PeekDesktop;

/// <summary>
/// Core orchestrator for the peek-desktop feature.
/// State machine with two states: Idle and Peeking.
///
///   Idle → Peeking:  user clicks empty desktop wallpaper
///   Peeking → Idle:  a non-desktop window gains foreground focus
/// </summary>
public sealed class DesktopPeek : IDisposable
{
    private const int PostPeekFocusGracePeriodMs = 750;

    private readonly MouseHook _mouseHook = new();
    private readonly FocusWatcher _focusWatcher = new();
    private readonly WindowTracker _windowTracker = new();

    private bool _isPeeking;
    private bool _isTransitioning; // suppresses events during minimize/restore
    private long _ignoreFocusUntil;

    public bool IsEnabled { get; set; } = true;
    public bool IsPeeking => _isPeeking;
    public PeekMode PeekMode { get; set; }

    public DesktopPeek(Settings settings)
    {
        PeekMode = settings.PeekMode;
        AppDiagnostics.Log("DesktopPeek created");
        _mouseHook.DesktopClicked += OnDesktopClicked;
        _mouseHook.DesktopIconClicked += OnDesktopIconClicked;
        _mouseHook.NonDesktopClicked += OnNonDesktopClicked;
        _focusWatcher.FocusChanged += OnFocusChanged;
    }

    public void Start()
    {
        AppDiagnostics.Log($"Start requested. Enabled={IsEnabled}");
        _mouseHook.Install();
        _focusWatcher.Start();
    }

    public void Stop()
    {
        AppDiagnostics.Log($"Stop requested. IsPeeking={_isPeeking}");
        _mouseHook.Uninstall();
        _focusWatcher.Stop();

        if (_isPeeking)
            RestoreWindows();
    }

    private void OnDesktopClicked(object? sender, EventArgs e)
    {
        if (!IsEnabled || _isTransitioning)
        {
            AppDiagnostics.Log($"Desktop click ignored. Enabled={IsEnabled} IsPeeking={_isPeeking} Transitioning={_isTransitioning}");
            return;
        }

        if (_isPeeking)
        {
            AppDiagnostics.Log("Desktop clicked again while peeking; restoring windows");
            RestoreWindows();
            return;
        }

        AppDiagnostics.Log("Desktop click accepted; entering peek mode");
        PeekDesktopNow();
    }

    private void OnDesktopIconClicked(object? sender, EventArgs e)
    {
        if (_isTransitioning)
        {
            AppDiagnostics.Log("Desktop icon click ignored during transition");
            return;
        }

        if (_isPeeking)
        {
            AppDiagnostics.Log("Desktop icon clicked while peeking; staying in peek mode");
            return;
        }

        AppDiagnostics.Log("Desktop icon clicked; not entering peek mode");
    }

    private void OnNonDesktopClicked(object? sender, EventArgs e)
    {
        if (!_isPeeking || _isTransitioning)
        {
            AppDiagnostics.Log($"Non-desktop click ignored. IsPeeking={_isPeeking} Transitioning={_isTransitioning}");
            return;
        }

        AppDiagnostics.Log("Non-desktop click detected while peeking; restoring windows");
        RestoreWindows();
    }

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        if (!_isPeeking || _isTransitioning)
            return;

        AppDiagnostics.LogWindow("Focus changed while peeking", e.ForegroundWindow);

        // If the new foreground is still the desktop or transient desktop UI, stay peeking
        if (DesktopDetector.IsDesktopWindow(e.ForegroundWindow))
        {
            AppDiagnostics.Log("Foreground is still desktop-related; staying in peek mode");
            return;
        }

        // Focus can churn briefly while windows are being minimized. Ignore those
        // immediate foreground changes so the initial desktop click stays in peek mode.
        if (Environment.TickCount64 < _ignoreFocusUntil)
        {
            AppDiagnostics.Log("Foreground change fell inside grace period; ignoring");
            return;
        }

        AppDiagnostics.Log("Foreground moved away from desktop; restoring windows");
        RestoreWindows();
    }

    private void PeekDesktopNow()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _isTransitioning = true;
        AppDiagnostics.Log("Beginning peek transition");
        try
        {
            _windowTracker.CaptureWindows();
            if (_windowTracker.HasWindows)
            {
                AppDiagnostics.Log($"Captured {_windowTracker.SavedWindowCount} window(s); applying {PeekMode} effect");

                if (PeekMode == PeekMode.FlyAway)
                    _windowTracker.FlyAwayAll();
                else
                    _windowTracker.MinimizeAll();

                _isPeeking = true;
                _ignoreFocusUntil = Environment.TickCount64 + PostPeekFocusGracePeriodMs;
                AppDiagnostics.Log($"Peek mode active; ignoring focus churn for {PostPeekFocusGracePeriodMs}ms");
            }
            else
            {
                AppDiagnostics.Log("No restoreable windows were captured");
            }
        }
        finally
        {
            _isTransitioning = false;
            AppDiagnostics.Log("Peek transition complete");
            AppDiagnostics.Metric($"PeekDesktopNow total: {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    private void RestoreWindows()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _isTransitioning = true;
        AppDiagnostics.Log($"Beginning restore transition for {_windowTracker.SavedWindowCount} window(s)");
        try
        {
            _ignoreFocusUntil = 0;
            _windowTracker.RestoreAll(PeekMode);
            _isPeeking = false;
            AppDiagnostics.Log("Restore complete; returned to idle");
        }
        finally
        {
            _isTransitioning = false;
            AppDiagnostics.Metric($"RestoreWindows total: {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    public void Dispose()
    {
        AppDiagnostics.Log("DesktopPeek disposing");
        Stop();
        _mouseHook.Dispose();
        _focusWatcher.Dispose();
    }
}
