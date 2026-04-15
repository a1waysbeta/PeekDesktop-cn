# PeekDesktop 👀

**Click empty desktop wallpaper to reveal your desktop — just like macOS Sonoma.**

PeekDesktop brings macOS Sonoma's "click wallpaper to reveal desktop" feature to Windows 10 and 11. By default it uses Explorer's native **Show Desktop** behavior, and it also includes optional **Classic Minimize** and **Fly Away** peek styles from the tray menu. Click or drag desktop icons normally without accidentally triggering peek. When you're done, click any window, the taskbar, or the wallpaper again and everything comes right back where it was.

<p align="center">
  <img src="img/demo.gif" alt="PeekDesktop demo showing windows minimizing when you click the wallpaper" width="900" />
</p>

## Download

📥 **[Download the latest release](https://github.com/shanselman/PeekDesktop/releases/latest)**

| File | Platform |
|------|----------|
| `PeekDesktop-vX.Y-win-x64.zip` | Intel/AMD (most PCs) |
| `PeekDesktop-vX.Y-win-arm64.zip` | ARM64 (Surface Pro X, Snapdragon, etc.) |

No installer needed. Download the zip, extract it, and run `PeekDesktop.exe`. Release builds are **self-contained**, so you do not need to install .NET separately. It lives in your system tray and can notify you when a newer GitHub Release is available.

## Documentation

- **[Engineering Deep Dive](Docs/PeekDesktop-Engineering-Deep-Dive.md)** - architecture, shell internals, experiments, debugging workflow, undocumented API notes, and release tradeoffs

## How It Works

1. **Click empty desktop wallpaper** (not an icon) -> your desktop is revealed
2. **Stay on the desktop** -> click or drag icons, right-click, and rearrange things while windows stay hidden
3. **Click any app, the taskbar, or empty wallpaper again** -> all windows restore to exactly where they were

That's it. It just works.

## Peek Styles

PeekDesktop includes three styles you can switch live from the tray icon:

- **Native Show Desktop (Explorer)** — the default and recommended mode
- **Classic Minimize** — minimizes and restores tracked windows
- **Fly Away (Experimental)** — animates windows offscreen before restoring them

### Under the Hood

PeekDesktop uses lightweight Windows APIs:

- **`SetWindowsHookEx(WH_MOUSE_LL)`** — low-level mouse hook to detect desktop clicks
- **`WindowFromPoint`** — identifies the window under your cursor
- **MSAA hit-testing (`AccessibleObjectFromPoint`)** — distinguishes empty wallpaper from desktop icons
- **Win+D `SendInput`** — uses Explorer's native Show Desktop for the default mode
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
- 👀 **Peek Style** — switch between Explorer, minimize, and fly-away modes
- ℹ️ **About** — version info
- ⬇️ **Check for Updates** — see if a newer version is out and open the download page
- ❌ **Exit** — quit PeekDesktop

## What's New in v0.7.0

- **Smaller Native AOT binary** via `PublishAotCompressed` (x64 test output dropped from ~2.32 MB to ~0.99 MB)
- **Pause While Gaming / Full-Screen** option (enabled by default) to avoid desktop-peek interference during gaming sessions

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
| Smooth animation | ✅ | Coming soon |

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

Release builds use **.NET Native AOT** — the exe is a fully native binary with no .NET runtime dependency. The x64 build is further compressed with [PublishAotCompressed](https://github.com/AustinWise/PublishAotCompressed) (UPX) for a final download under 1 MB.

## Architecture

```
src/PeekDesktop/
├── Program.cs             # Entry point, single-instance mutex
├── DesktopPeek.cs         # Core state machine (Idle ↔ Peeking)
├── MouseHook.cs           # WH_MOUSE_LL global mouse hook
├── FocusWatcher.cs        # EVENT_SYSTEM_FOREGROUND monitor
├── WindowTracker.cs       # Enumerate, minimize, and restore windows
├── DesktopDetector.cs     # Identify Progman/WorkerW desktop windows
├── Win32MessageLoop.cs    # Win32 message loop (replaces WinForms)
├── Win32TrayIcon.cs       # Shell_NotifyIcon wrapper
├── Win32Menu.cs           # Win32 popup menu wrapper
├── Win32Icon.cs           # Programmatic icon via CreateIconIndirect
├── WinHttp.cs             # WinHTTP wrapper (replaces HttpClient)
├── TrayIcon.cs            # Tray icon business logic + menu wiring
├── AppUpdater.cs          # GitHub release update checker
├── Settings.cs            # Hand-written JSON persistence + registry autostart
└── NativeMethods.cs       # Win32 P/Invoke declarations
```

### State Machine

```
┌──────┐  empty wallpaper click   ┌─────────┐
│ Idle │ ───────────────→ │ Peeking │
│      │ ←─────────────── │         │
└──────┘  app click / taskbar      └─────────┘
          click / wallpaper click
          to restore
```

## Contributing

PRs welcome! Current status and next ideas:

- [x] Click empty wallpaper to peek
- [x] Restore on app click or taskbar click
- [x] Restore on a second wallpaper click
- [x] Clicking or dragging desktop icons does **not** start peek
- [x] Desktop icons remain usable while peeking
- [x] Exact window positions are restored
- [x] GitHub release-based update checks
- [ ] Smooth minimize/restore animations (slide/fade)
- [ ] Hotkey support (e.g., `Ctrl+F12` to toggle peek)
- [ ] Per-monitor peek (only minimize windows on the clicked monitor)
- [ ] Exclude specific apps from being minimized
- [ ] Better icon (the current one is programmatically generated)
- [ ] Windows 11 widgets area awareness
- [ ] Sound effect on peek/restore

## .NET Native AOT — The Size Journey 💾

PeekDesktop is a showcase for how small a .NET Native AOT application can get. Starting from a standard WinForms app, we systematically eliminated every managed framework dependency until the binary was pure Win32 P/Invoke — then compressed it to fit on a floppy disk.

| Version | Binary Size | What Changed |
|---------|------------|--------------|
| v0.4.5 | ~65 MB | Self-contained .NET (no AOT) |
| v0.5.0 | 17.5 MB | Enabled Native AOT |
| v0.6.0 | 4.2 MB | Dropped WinForms — pure Win32 P/Invoke for tray icon, menus, message loop |
| v0.6.1 | 2.3 MB | Replaced `HttpClient` with OS-native WinHTTP (`winhttp.dll`) |
| v0.7.2 | 1.88 MB | Eliminated JSON source generator, `System.Reflection`, `Process.Start` |
| v0.7.2 + UPX | **~834 KB** | **UPX compression via [PublishAotCompressed](https://github.com/AustinWise/PublishAotCompressed)** |

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
- **`OptimizationPreference=Size`** + `IlcFoldIdenticalMethodBodies` + `InvariantGlobalization` + stripped diagnostics

Special thanks to [Michal Strehovský](https://github.com/MichalStrehovsky) — the architect of .NET Native AOT — whose [PR #5](https://github.com/shanselman/PeekDesktop/pull/5) inspired the final round of optimizations that eliminated the JSON source generator, reflection, and managed delegates. When the person who *built* the AOT compiler optimizes your app, you pay attention. 🙏

## License

[MIT](LICENSE)
