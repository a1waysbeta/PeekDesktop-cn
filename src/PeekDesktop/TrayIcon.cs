using System;

namespace PeekDesktop;

/// <summary>
/// Manages the system tray (notification area) icon and its context menu
/// using raw Win32 APIs (no WinForms dependency).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint TrayRetryDelayMs = 1000;

    // Menu item IDs
    private const uint ID_ENABLED = 1;
    private const uint ID_STARTUP = 2;
    private const uint ID_DOUBLECLICK = 3;
    private const uint ID_GAME_GUARD = 4;
    private const uint ID_TASKBAR_CLICK = 5;
    private const uint ID_RESTORE_ON_APP_OPEN = 6;
    private const uint ID_MODE_MINIMIZE = 10;
    private const uint ID_MODE_FLYAWAY = 11;
    private const uint ID_MODE_NATIVE = 12;
    private const uint ID_ABOUT = 20;
    private const uint ID_UPDATES = 21;
    private const uint ID_EXIT = 30;

    private readonly Win32TrayIcon _trayIcon;
    private readonly Win32MessageLoop _messageLoop;
    private readonly DesktopPeek _desktopPeek;
    private readonly AppUpdater _appUpdater;
    private readonly Settings _settings;
    private readonly Action _exitAction;

    public TrayIcon(Win32MessageLoop messageLoop, DesktopPeek desktopPeek, AppUpdater appUpdater, Settings settings, Action exitAction)
    {
        _messageLoop = messageLoop;
        _desktopPeek = desktopPeek;
        _appUpdater = appUpdater;
        _settings = settings;
        _exitAction = exitAction;

        _trayIcon = new Win32TrayIcon(messageLoop.Handle);
        TryAddTrayIcon(scheduleRetryOnFailure: true);

        _messageLoop.MessageReceived += OnMessage;
        _messageLoop.TaskbarCreated += OnTaskbarCreated;

        _appUpdater.UpdateAvailable += (_, e) =>
        {
            _trayIcon.ShowBalloon(
                "PeekDesktop 有更新可用",
                $"发现新版本 {e.Version}，点击此处打开下载页面");
        };
    }

    private void OnTaskbarCreated()
    {
        AppDiagnostics.Log("Re-adding tray icon after Explorer restart");
        TryAddTrayIcon(scheduleRetryOnFailure: true);
    }

    private void TryAddTrayIcon(bool scheduleRetryOnFailure)
    {
        IntPtr hIcon = Win32Icon.CreateTrayIcon();
        if (_trayIcon.Add(hIcon, "PeekDesktop \u2014 点击桌面，显示桌面"))
            return;

        if (!scheduleRetryOnFailure)
            return;

        AppDiagnostics.Log("Tray icon add failed; scheduling one retry");
        _messageLoop.PostDeferredAction(TrayRetryDelayMs, () => TryAddTrayIcon(scheduleRetryOnFailure: false));
    }

    private (bool handled, IntPtr result) OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32TrayIcon.WM_TRAYICON)
        {
            if (Win32TrayIcon.IsRightClick(lParam))
            {
                ShowContextMenu();
                return (true, IntPtr.Zero);
            }

            if (Win32TrayIcon.IsBalloonClick(lParam))
            {
                _appUpdater.OpenLatestReleasePage();
                return (true, IntPtr.Zero);
            }
        }

        return (false, IntPtr.Zero);
    }

    private void ShowContextMenu()
    {
        using var menu = new Win32Menu();

        menu.AddItem(ID_ENABLED, "启用", ToggleEnabled, _settings.Enabled);
        menu.AddItem(ID_STARTUP, "开机启动", ToggleStartup, _settings.StartWithWindows);
        menu.AddItem(ID_DOUBLECLICK, "双击触发", ToggleDoubleClick, _settings.RequireDoubleClick);
        menu.AddItem(ID_TASKBAR_CLICK, "点击任务栏触发显示桌面", ToggleTaskbarClick, _settings.PeekOnTaskbarClick);
        menu.AddItem(ID_RESTORE_ON_APP_OPEN, "切换应用时恢复所有窗口", ToggleRestoreOnAppOpen, _settings.RestoreHiddenWindowsOnAppOpen);
        menu.AddItem(ID_GAME_GUARD, "游戏/全屏时暂停", ToggleGameGuard, _settings.PauseWhileFullscreenAppActive);
        menu.AddSeparator();
        menu.AddItem(ID_MODE_NATIVE, "显示桌面（资源管理器）", () => SetPeekMode(PeekMode.NativeShowDesktop), _settings.PeekMode == PeekMode.NativeShowDesktop);
        menu.AddItem(ID_MODE_FLYAWAY, "飞离效果（实验性）", () => SetPeekMode(PeekMode.FlyAway), _settings.PeekMode == PeekMode.FlyAway);
        menu.AddSeparator();
        menu.AddItem(ID_ABOUT, "关于 PeekDesktop", ShowAbout);
        menu.AddItem(ID_UPDATES, "检查更新", CheckForUpdates);
        menu.AddSeparator();
        menu.AddItem(ID_EXIT, "退出", DoExit);

        menu.Show(_messageLoop.Handle);
    }

    private void ToggleEnabled()
    {
        _settings.Enabled = !_settings.Enabled;
        _desktopPeek.IsEnabled = _settings.Enabled;

        if (_settings.Enabled)
            _desktopPeek.Start();
        else
            _desktopPeek.Stop();

        _settings.Save();
    }

    private void ToggleStartup()
    {
        _settings.StartWithWindows = !_settings.StartWithWindows;
        _settings.Save();
        Settings.SetAutoStart(_settings.StartWithWindows);
    }

    private void ToggleDoubleClick()
    {
        _settings.RequireDoubleClick = !_settings.RequireDoubleClick;
        _desktopPeek.SetRequireDoubleClick(_settings.RequireDoubleClick);
        _settings.Save();
    }

    private void ToggleGameGuard()
    {
        _settings.PauseWhileFullscreenAppActive = !_settings.PauseWhileFullscreenAppActive;
        _desktopPeek.SetPauseWhileFullscreenAppActive(_settings.PauseWhileFullscreenAppActive);
        _settings.Save();
    }

    private void ToggleTaskbarClick()
    {
        _settings.PeekOnTaskbarClick = !_settings.PeekOnTaskbarClick;
        _desktopPeek.SetPeekOnTaskbarClick(_settings.PeekOnTaskbarClick);
        _settings.Save();
    }

    private void ToggleRestoreOnAppOpen()
    {
        _settings.RestoreHiddenWindowsOnAppOpen = !_settings.RestoreHiddenWindowsOnAppOpen;
        _desktopPeek.SetRestoreHiddenWindowsOnAppOpen(_settings.RestoreHiddenWindowsOnAppOpen);
        _settings.Save();
    }

    private void SetPeekMode(PeekMode peekMode)
    {
        _settings.PeekMode = peekMode;
        _desktopPeek.SetPeekMode(peekMode);
        _trayIcon.UpdateTooltip($"PeekDesktop - {GetPeekModeDisplayName(peekMode)}");
        _settings.Save();
    }

    private void ShowAbout()
    {
        string version = GetDisplayVersion();
        NativeMethods.MessageBoxW(
            IntPtr.Zero,
            $"PeekDesktop v{version}\n\n" +
            "点击桌面壁纸即可显示桌面，\n" +
            "就像 macOS Sonoma 一样。\n\n" +
            "点击桌面或任务栏任意空白区域即可恢复。\n" +
            "“显示桌面”动画效果让你可以在资源管理器模式\n" +
            "与飞离模式之间切换。\n\n" +
            "更新来自 GitHub 发布页。\n\n" +
            "中文版：github.com/a1waysbeta/PeekDesktop-cn\n" +
            "英文版：github.com/shanselman/PeekDesktop\n\n" +
            "汉化：alwaysbeta",
            "关于 PeekDesktop",
            NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
    }

    private async void CheckForUpdates()
    {
        await _appUpdater.CheckForUpdatesAsync(interactive: true);
    }

    private void DoExit()
    {
        _trayIcon.Remove();
        _exitAction();
    }

    internal static string GetDisplayVersion()
    {
        var (productVersion, fileVersion) = NativeMethods.GetExeVersionInfo();
        string? version = productVersion ?? fileVersion?.ToString();

        if (string.IsNullOrWhiteSpace(version))
            return "未知";

        int plusIndex = version.IndexOf('+');
        version = plusIndex >= 0 ? version[..plusIndex] : version;

        if (Version.TryParse(version, out var parsed) && parsed.Build >= 0 && parsed.Revision == 0)
            return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";

        return version switch
        {
            "1.0.0.0" => "dev build",
            "1.0.0" => "dev build",
            _ => version
        };
    }

    private static string GetPeekModeDisplayName(PeekMode peekMode)
    {
        return peekMode switch
        {
            PeekMode.Minimize => "经典最小化",
            PeekMode.FlyAway => "飞离效果",
            PeekMode.NativeShowDesktop => "原生显示桌面",
            _ => "显示桌面"
        };
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
    }
}
