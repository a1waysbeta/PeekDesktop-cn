# PeekDesktop 👀

**点击桌面空白壁纸（或任务栏空白区域）即可显示桌面 —— 就像 macOS Sonoma 一样。**

PeekDesktop 将 macOS Sonoma 的「点击壁纸显示桌面」功能带到 Windows 10 和 11。默认情况下，它使用资源管理器的原生「显示桌面」行为，同时还包含可选的「飞离」实验性效果，以及托盘菜单中的「需要双击」和「点击任务栏触发显示桌面」开关。你可以正常点击或拖拽桌面图标，不会意外触发显示桌面。显示桌面后，再次点击任务栏空白区域或桌面壁纸，所有窗口就会恢复到原来的位置。

<p align="center">
  <img src="img/demo.gif" alt="PeekDesktop demo showing windows minimizing when you click the wallpaper" width="900" />
</p>

## 下载

📥 **[下载最新版本](https://github.com/a1waysbeta/PeekDesktop-cn/releases/latest)**

| 文件 | 适用平台 |
|------|----------|
| `PeekDesktop-vX.Y-win-x64.zip` | Intel/AMD（大多数电脑） |
| `PeekDesktop-vX.Y-win-arm64.zip` | ARM64（Surface Pro X、Snapdragon 等） |

无需安装程序。下载 zip 压缩包，解压后运行 `PeekDesktop.exe` 即可。发布版本为**自包含**单文件，无需额外安装 .NET。程序会驻留在系统托盘中，并在 GitHub 有新版本时通知你。

## 文档

- **[工程深度解析](Docs/PeekDesktop-Engineering-Deep-Dive.md)** —— 架构、Shell 内部机制、实验、调试流程、未公开 API 说明及发布权衡

## 工作原理

1. **点击桌面空白壁纸或任务栏空白区域**（不是图标或任务栏按钮）-> 显示桌面
2. **停留在桌面上** -> 可以点击或拖拽图标、右键菜单、整理文件，窗口保持隐藏
3. **再次点击任务栏或空白壁纸** -> 所有窗口恢复到原来的位置

## 显示桌面动画效果

- **显示桌面（资源管理器）** — 默认且推荐模式。使用资源管理器的原生显示桌面行为。
- **飞离（实验性）** — 窗口飞出屏幕的动画效果。有趣但存在与外部窗口管理（Win+D、任务栏）的已知问题。追求视觉效果可使用，但要知道当 Shell 在背后改变窗口状态时可能会出现混乱。

### Under the Hood

PeekDesktop uses lightweight Windows APIs:

- **`SetWindowsHookEx(WH_MOUSE_LL)`** — low-level mouse hook to detect desktop clicks
- **`WindowFromPoint`** — identifies the window under your cursor
- **MSAA hit-testing (`AccessibleObjectFromPoint`)** — distinguishes empty wallpaper from desktop icons
- **UI Automation hit-testing** — classifies empty taskbar space without firing on Start, pinned apps, or tray buttons
- **Taskbar Show Desktop button click** — primary path, immune to keyboard remapping (PowerToys, etc.)
- **Win+D `SendInput`** — fallback if taskbar button is unavailable
- **`EnumWindows` + `WINDOWPLACEMENT`** — captures exact position and state (including maximized) of every window
- **`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`** — watches for when you switch back to an app
- **`SetWindowPlacement`** — restores windows to their exact previous positions

No admin rights required. Uses < 5 MB RAM idle.

## System Tray

Right-click the tray icon for options:

- ✅ **Enabled** — toggle the peek feature on/off
- 🔁 **Start with Windows** — launch automatically at login
- 🖱️ **Require Double-Click** — optionally require a double-click on the desktop to trigger peek
- 🎮 **Pause While Gaming / Full-Screen** — on by default for exclusive full-screen and known gaming fullscreen apps
- 📌 **Peek on Taskbar Click** — optionally trigger peek from empty taskbar space
- 🪟 **Restore All Windows on App Switch** — on by default; in Explorer show desktop mode, taskbar/Alt+Tab app switches restore all hidden windows behind the selected app
- 👀 **Peek Style** — switch between Explorer and fly-away modes
- ℹ️ **About** — version info
- ⬇️ **Check for Updates** — see if a newer version is out and open the download page
- ❌ **Exit** — quit PeekDesktop

When Windows is in dark mode, the tray menu also follows the system theme when supported by the OS.

## What's New

- **Small Native AOT single-file builds** for both x64 and ARM64
- **Peek on Taskbar Click** — optional trigger from empty taskbar space
- **Dark tray menu support** — follows Windows dark mode when available
- **Taskbar button Show Desktop** — bypasses keyboard remappers (PowerToys Keyboard Manager, etc.)
- **Pause While Gaming / Full-Screen** — avoids interference during gaming sessions
- **Require Double-Click** — optional double-click trigger for desktop peek
- **Auto-update notifications** via GitHub Releases

## macOS Sonoma vs PeekDesktop

| Feature | macOS Sonoma | PeekDesktop |
|---------|-------------|-------------|
| Click wallpaper to peek | ✅ | ✅ |
| Restore on app click | ✅ | ✅ |
| Restore on second wallpaper click | ✅ | ✅ |
| Clicking/dragging icons does not trigger peek | ✅ | ✅ |
| Desktop icons accessible | ✅ | ✅ |
| Exact window position restore | ✅ | ✅ |
| System tray control | ❌ | ✅ |
| Multi-monitor support | ✅ | ✅ |
| Start with OS | Login Items | ✅ Registry |
| Smooth animation | ✅ | Fly Away mode |

## Build from Source

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```bash
git clone https://github.com/shanselman/PeekDesktop.git
cd PeekDesktop
dotnet build src/PeekDesktop/PeekDesktop.csproj
```

### Run it

```bash
dotnet run --project src/PeekDesktop/PeekDesktop.csproj
```

### Publish a self-contained single-file exe

```bash
# For Intel/AMD
dotnet publish src/PeekDesktop/PeekDesktop.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# For ARM64
dotnet publish src/PeekDesktop/PeekDesktop.csproj -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true
```

### Release packaging

Release builds use **.NET Native AOT** — the exe is a fully native binary with no .NET runtime dependency. Current releases ship as self-contained single-file executables for both x64 and ARM64. Earlier experiments also used [PublishAotCompressed](https://github.com/MichalStrehovsky/PublishAotCompressed) (LZMA), but current builds favor compatibility and predictable startup behavior.

## Architecture

```
src/PeekDesktop/
├── Program.cs             # Entry point, single-instance mutex
├── DesktopPeek.cs         # Core state machine (Idle ↔ Peeking)
├── MouseHook.cs           # WH_MOUSE_LL global mouse hook
├── FocusWatcher.cs        # EVENT_SYSTEM_FOREGROUND monitor
├── WindowTracker.cs       # Enumerate, minimize, and restore windows
├── DesktopDetector.cs     # Identify desktop windows, icons, taskbar
├── Win32MessageLoop.cs    # Win32 message loop + TaskbarCreated recovery
├── Win32TrayIcon.cs       # Shell_NotifyIcon wrapper
├── Win32Menu.cs           # Win32 popup menu wrapper
├── Win32Icon.cs           # Programmatic icon via CreateIconIndirect
├── WinHttp.cs             # WinHTTP wrapper (replaces HttpClient)
├── TrayIcon.cs            # Tray icon business logic + menu wiring
├── AppUpdater.cs          # GitHub release update checker (via WinHTTP)
├── AppDiagnostics.cs      # Logging via Trace/DebugView
├── Settings.cs            # Hand-written UTF-8 JSON persistence + autostart
└── NativeMethods.cs       # Win32 P/Invoke declarations
```

## Contributing

PRs welcome! Current status and next ideas:

- [x] Click empty wallpaper to peek
- [x] Click empty taskbar area to peek (opt-in)
- [x] Restore on app click or taskbar click
- [x] Restore on a second wallpaper click
- [x] Clicking or dragging desktop icons does **not** start peek
- [x] Right-click desktop icons while peeking (context menus stay open)
- [x] Desktop icons remain usable while peeking
- [x] Exact window positions are restored
- [x] GitHub release-based update checks
- [x] Works with PowerToys Keyboard Manager (keyboard remapping)
- [ ] Smooth minimize/restore animations (slide/fade)
- [ ] Hotkey support (e.g., `Ctrl+F12` to toggle peek)
- [ ] Per-monitor peek (only minimize windows on the clicked monitor)
- [ ] Exclude specific apps from being minimized

## .NET Native AOT — The Size Journey 💾

PeekDesktop is a showcase for how small a .NET Native AOT application can get. Starting from a standard WinForms app, we systematically eliminated every managed framework dependency until the binary was pure Win32 P/Invoke — then compressed it to fit on a floppy disk.

| Version | Binary Size | What Changed |
|---------|------------|--------------|
| v0.4.5 | ~65 MB | Self-contained .NET (no AOT) |
| v0.5.0 | 17.5 MB | Enabled Native AOT |
| v0.6.0 | 4.2 MB | Dropped WinForms — pure Win32 P/Invoke for tray icon, menus, message loop |
| v0.6.1 | 2.3 MB | Replaced `HttpClient` with OS-native WinHTTP (`winhttp.dll`) |
| v0.7.2 | 1.88 MB | Eliminated JSON source generator, `System.Reflection`, `Process.Start` |
| v0.7.2 + LZMA | **~564 KB** | **LZMA compression via [PublishAotCompressed](https://github.com/MichalStrehovsky/PublishAotCompressed)** |

**What's left in the 1.88 MB (pre-compression)?**
- ~1.2 MB — .NET Native AOT runtime (GC, threading, exception handling, type system)
- ~0.4 MB — `Utf8JsonReader`/`Utf8JsonWriter` + async task machinery
- ~0.2 MB — App code, P/Invoke stubs, string literals
- ~0.08 MB — PE headers and metadata

**Key techniques:**
- **No WinForms, no System.Drawing** — `Shell_NotifyIcon`, `CreatePopupMenu`, `TrackPopupMenuEx`, `MessageBoxW`, `CreateIconIndirect` via P/Invoke
- **No HttpClient** — `WinHttpOpen`/`WinHttpSendRequest` uses the OS HTTP+TLS stack at zero binary cost
- **No JSON source generator** — hand-written `Utf8JsonReader`/`Utf8JsonWriter` for the two tiny JSON shapes we need
- **No System.Reflection** — PE version resources read via `GetFileVersionInfoExW` P/Invoke
- **No managed delegates for WndProc** — `UnmanagedCallersOnly` function pointers avoid marshaling overhead
- **`OptimizationPreference=Size`** + `InvariantGlobalization` + stripped diagnostics

Special thanks to [Michal Strehovský](https://github.com/MichalStrehovsky) — the architect of .NET Native AOT — whose [PR #5](https://github.com/shanselman/PeekDesktop/pull/5) inspired the final round of optimizations that eliminated the JSON source generator, reflection, and managed delegates. When the person who *built* the AOT compiler optimizes your app, you pay attention. 🙏

## License

[MIT](LICENSE)
