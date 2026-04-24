# PeekDesktop Auto-Updater

Technical documentation for PeekDesktop's in-place auto-update system. Zero external dependencies — built entirely on `System.IO.Compression` (BCL) and Win32 P/Invoke.

## How It Works

### User Experience

1. **Background check** — 2 seconds after startup, PeekDesktop queries the GitHub Releases API for the latest version
2. **Notification** — if a newer version exists, a balloon/toast notification appears: "Version X.Y.Z is available"
3. **User prompt** — clicking the balloon (or using tray menu → "Check for Updates") shows a dialog: "Download and install? PeekDesktop will restart automatically."
4. **Automatic update** — on "Yes", the app downloads, verifies, swaps, and restarts in ~1 second
5. **Settings** — users can disable background checks via tray menu → "Auto-Check for Updates"

### The Update Sequence

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Query GitHub API: /repos/shanselman/PeekDesktop/releases/latest │
│ 2. Parse assets[] array → pick correct zip (x64 or arm64)      │
│ 3. Download zip to %TEMP%\PeekDesktop-update-{guid}.zip         │
│ 4. Extract PeekDesktop.exe → same directory as PeekDesktop.new.exe │
│ 5. Verify Authenticode signature + signer identity              │
│ 6. Preflight: verify directory is writable                      │
│ 7. Rename PeekDesktop.exe → PeekDesktop.old.exe                 │
│ 8. Rename PeekDesktop.new.exe → PeekDesktop.exe                 │
│ 9. Launch new PeekDesktop.exe --restarting                      │
│ 10. Remove tray icon, release mutex, exit                       │
└─────────────────────────────────────────────────────────────────┘
```

### Architecture Detection

The updater uses `RuntimeInformation.ProcessArchitecture` to select the correct release asset:

| Architecture | Asset pattern |
|-------------|---------------|
| `X64` | `PeekDesktop-vX.Y.Z-win-x64.zip` |
| `Arm64` | `PeekDesktop-vX.Y.Z-win-arm64.zip` |

This means an x64 build running under emulation on ARM64 stays on x64. Users who want native ARM64 should download that build manually once.

---

## Security

### Authenticode Signature Verification

Every downloaded update is verified before installation using two layers:

1. **Chain trust** — `WinVerifyTrust` with `WINTRUST_ACTION_GENERIC_VERIFY_V2` verifies the binary has a valid Authenticode signature from a trusted certificate authority, with full revocation checking (`WTD_REVOKE_WHOLECHAIN`)

2. **Signer identity** — after trust verification, the updater extracts the signer's display name using `WTHelperProvDataFromStateData` → `WTHelperGetProvSignerFromChain` → `CertGetNameStringW`, and compares it against the expected publisher substring (`"Hanselman"`)

If either check fails, the update is rejected with a clear error message and no files are modified.

### Resource Cleanup

`WinVerifyTrust` is called with `WTD_STATEACTION_VERIFY` followed by `WTD_STATEACTION_CLOSE` in a `finally` path to prevent leaking the Authenticode certificate store.

### URL Validation

Release URLs are validated against `https://github.com/shanselman/PeekDesktop/` before being used. The download uses the `browser_download_url` from the GitHub API response, which redirects to GitHub's CDN. WinHTTP follows HTTPS→HTTPS redirects automatically.

### Download Safety

- Downloads are capped at 50 MB to prevent memory exhaustion
- WinHTTP read/query errors throw immediately (no silent truncation)
- Temp files are cleaned up on failure

---

## The Swap Dance

The trickiest part of the auto-updater is replacing the running executable. Windows allows renaming a running exe (the OS holds a handle to the inode, not the filename), so the sequence is:

```
PeekDesktop.exe      (running)
PeekDesktop.new.exe  (verified new version)

Step 1: File.Move(PeekDesktop.exe → PeekDesktop.old.exe, overwrite: true)
Step 2: File.Move(PeekDesktop.new.exe → PeekDesktop.exe)
Step 3: CreateProcessW("PeekDesktop.exe" --restarting)
Step 4: Remove tray icon, release mutex, Environment.Exit(0)
```

### Why the Exact Path Matters

The autostart registry entry (`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\PeekDesktop`) stores the **exact exe path**. If the new exe doesn't land at exactly the same path with the same filename, "Start with Windows" breaks. That's why we do in-place replacement, not "install to a new version folder."

### Rollback on Failure

If any step fails after the rename:
- If `PeekDesktop.exe` is missing but `PeekDesktop.old.exe` exists → rename it back
- Partial downloads (`.new.exe`, temp zips) are cleaned up
- The user sees an error dialog but the app keeps running on the old version

### Startup Recovery

On every launch, `CleanupPreviousUpdate()` runs (after acquiring the mutex) to delete:
- `PeekDesktop.old.exe` — leftover from successful update
- `PeekDesktop.new.exe` — leftover from failed update  
- `PeekDesktop-update-*.zip` in `%TEMP%` — leftover temp downloads

---

## Mutex Handoff

PeekDesktop uses a named mutex (`Local\PeekDesktop_SingleInstance`) to prevent multiple instances. During an update restart:

1. Old process launches new process with `--restarting` flag
2. Old process releases the mutex and exits
3. New process detects `--restarting` and retries mutex acquisition for up to 5 seconds (20 × 250ms)
4. If the old process is slow to exit, `AbandonedMutexException` is caught and treated as success

The launch happens **before** the mutex release — if `CreateProcessW` fails, the mutex is never released, and the old process stays running with an error message.

---

## Files Changed

| File | Role |
|------|------|
| `AppUpdater.cs` | Core update logic: check, download, extract, verify, swap, restart, cleanup |
| `NativeMethods.cs` | `WinVerifyTrust`, `WTHelper*`, `CertGetNameStringW`, `CreateProcessW` P/Invoke |
| `WinHttp.cs` | `DownloadToFile()` — writes HTTP response to disk (50MB cap) |
| `Program.cs` | `--restarting` mutex retry, cleanup on startup, mutex release wiring |
| `Settings.cs` | `AutoCheckForUpdates` property (default: true) |
| `TrayIcon.cs` | "Auto-Check for Updates" toggle, balloon → prompt → install flow |

---

## Dependencies

**Zero new NuGet packages.** Everything uses:

| Capability | Source | Binary cost |
|-----------|--------|-------------|
| HTTP downloads | `winhttp.dll` (OS native, already in use) | 0 KB |
| ZIP extraction | `System.IO.Compression` (BCL) | ~200-400 KB |
| Signature verification | `wintrust.dll` + `crypt32.dll` (OS native) | 0 KB |
| Process launch | `kernel32.dll` `CreateProcessW` (OS native) | 0 KB |
| JSON parsing | `System.Text.Json.Utf8JsonReader` (already in use) | 0 KB |

---

## Testing

### Interop Harness Tests

7 auto-updater tests in `PeekDesktop.InteropHarness`:

| Test | What it covers |
|------|---------------|
| Version comparison logic | Model shape, tag normalization |
| Asset matching by architecture | x64/arm64 selection, fake arch rejection |
| Release JSON deserialization | `assets[]` array parsing with `Utf8JsonReader` |
| Authenticode rejects unsigned | Fake PE file correctly rejected by `WinVerifyTrust` |
| WinHttp download to file | `Get()` + `DownloadToFile()` against real HTTPS endpoints |
| Zip extraction round-trip | Create zip → extract `PeekDesktop.exe` → verify content |
| WinVerifyTrust state cleanup | 100 calls with no handle leak |

Run with: `dotnet run --project src\PeekDesktop.InteropHarness\PeekDesktop.InteropHarness.csproj`

### Manual Swap Dance Test

To test the full download→swap→restart flow locally:

1. Add `#if DEBUG` bypasses in `AppUpdater.cs`:
   - Around the version comparison (force update regardless of version)
   - Around the signature check (skip for unsigned dev builds)
2. Build debug: `dotnet build src\PeekDesktop\PeekDesktop.csproj`
3. Run: `dotnet run --project src\PeekDesktop\PeekDesktop.csproj`
4. Right-click tray → "Check for Updates" → Yes
5. Watch it download v0.8.5, swap, and restart
6. Verify: About → should show v0.8.5

### Full Signed Test

To test with real Authenticode verification:
1. Tag a new version: `git tag v0.9.0` and push
2. The CI workflow builds, signs (Azure Trusted Signing), and creates a GitHub Release
3. Run the previous signed release — it should auto-update to v0.9.0
4. Verify the signer identity check passes in the log

---

## Design Decisions

### Why not a separate updater process?
A separate updater exe adds complexity (two binaries to ship, coordinate, keep in sync). Since Windows allows renaming running executables, the app can update itself in-place.

### Why not Squirrel, WinSparkle, or other update frameworks?
They pull in significant dependencies that conflict with the project's goal of minimal binary size and zero NuGet runtime deps. The entire auto-updater adds ~200-400 KB (from `System.IO.Compression`) vs megabytes for a framework.

### Why WinHTTP instead of HttpClient?
`HttpClient` pulls in the entire managed networking stack (~1 MB in AOT). `WinHTTP` uses the OS HTTP+TLS stack at zero binary cost and is already used for update checks.

### Why not download in the background automatically?
The user should consent before we download and replace their executable. A ~2 MB download on metered connections deserves a prompt. The "Auto-Check for Updates" toggle controls only the *check*, not the download.

### Why require Authenticode verification?
Without it, a compromised GitHub release or MITM attack could inject a malicious binary. The signature check ensures only properly signed binaries from the expected publisher are installed.

### What about the signer name constant?
`NativeMethods.cs` contains `ExpectedSignerSubstring = "Hanselman"`. This must match the CN or display name from the Azure Trusted Signing certificate. After signing a release, verify with `signtool verify /pa /v PeekDesktop.exe` and update the constant if needed.

---

## Transition for Existing Users

Users on v0.8.5 and earlier have the old `AppUpdater` that only opens the browser. Their upgrade path:

1. They see "new version available" → opens GitHub Releases page
2. They download and extract manually (one last time)
3. From that point forward, all updates are automatic via this auto-updater

This is an inherent one-time transition — there's no way to silently upgrade the updater itself without the user installing the new version first.
