using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PeekDesktop;

internal sealed class AppUpdater
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/a1waysbeta/PeekDesktop-cn/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/a1waysbeta/PeekDesktop-cn/releases/latest";

    private readonly Win32MessageLoop? _messageLoop;
    private int _isChecking;
    private int _isUpdating;
    private string? _latestReleaseUrl;
    private GitHubReleaseInfo? _latestRelease;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    /// <summary>
    /// Set by Program.cs so the updater can release the single-instance mutex before relaunching.
    /// Must be called on the UI thread (the thread that owns the mutex).
    /// </summary>
    public static Action? ReleaseMutex { get; set; }

    /// <summary>
    /// Set by TrayIcon so the updater can remove the tray icon before exiting.
    /// </summary>
    public static Action? RemoveTrayIcon { get; set; }

    public AppUpdater(Win32MessageLoop? messageLoop = null)
    {
        _messageLoop = messageLoop;
    }

    public async Task CheckForUpdatesAsync(bool interactive)
    {
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
        {
            if (interactive)
            {
                NativeMethods.MessageBoxW(
                    IntPtr.Zero,
                    "PeekDesktop 正在检查更新中。",
                    "PeekDesktop 更新",
                    NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
            }

            return;
        }

        try
        {
            AppDiagnostics.Log(interactive ? "Manual update check started" : "Background update check started");

            GitHubReleaseInfo? release = await GetLatestReleaseAsync();
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                throw new InvalidOperationException("GitHub 未返回有效的版本信息。");

            string latestVersion = NormalizeVersion(release.TagName);
            string currentVersion = GetCurrentVersion();
            _latestReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl;
            _latestRelease = release;

            AppDiagnostics.Log($"Current version={currentVersion}, latest version={latestVersion}");

            if (!IsNewerVersion(latestVersion, currentVersion))
            {
                if (interactive)
                {
                    NativeMethods.MessageBoxW(
                        IntPtr.Zero,
                        $"您已经使用的是最新版本的 PeekDesktop（{currentVersion}）。",
                        "PeekDesktop 更新",
                        NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
                }

                return;
            }

            if (!interactive)
            {
                RaiseUpdateAvailable(latestVersion, _latestReleaseUrl);
                return;
            }

            // Interactive: prompt to download and install
            PromptAndInstall(latestVersion);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Update check failed: {ex}");

            if (interactive)
            {
                NativeMethods.MessageBoxW(
                    IntPtr.Zero,
                    $"PeekDesktop 无法检查更新。\n\n{ex.Message}",
                    "更新错误",
                    NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    /// <summary>
    /// Prompts the user and, if accepted, downloads and installs the update.
    /// Can be called from the balloon click handler or the interactive check flow.
    /// </summary>
    public void PromptAndInstall(string? latestVersion = null)
    {
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
            return;

        latestVersion ??= _latestRelease is not null ? NormalizeVersion(_latestRelease.TagName) : "新版本";

        int result = NativeMethods.MessageBoxW(
            IntPtr.Zero,
            $"PeekDesktop {latestVersion} 已发布。\n\n是否立即下载并安装？PeekDesktop 将自动重启。",
            "发现更新",
            NativeMethods.MB_YESNO | NativeMethods.MB_ICONINFORMATION);

        if (result != NativeMethods.IDYES)
        {
            Interlocked.Exchange(ref _isUpdating, 0);
            return;
        }

        _ = DownloadAndInstallAsync();
    }

    public void OpenLatestReleasePage()
    {
        string url = _latestReleaseUrl ?? ReleasesPageUrl;

        if (!IsValidReleaseUrl(url))
        {
            AppDiagnostics.Log($"Update URL failed validation, using hardcoded fallback: {url}");
            url = ReleasesPageUrl;
        }

        NativeMethods.ShellExecuteW(IntPtr.Zero, "open", url, null, null, NativeMethods.SW_SHOWNORMAL);
    }

    /// <summary>
    /// Downloads the update zip, extracts it, verifies the signature, swaps the exe, and relaunches.
    /// </summary>
    private async Task DownloadAndInstallAsync()
    {
        try
        {
            if (_latestRelease is null)
                throw new InvalidOperationException("没有可用的版本信息。");

            string? assetUrl = FindMatchingAssetUrl(_latestRelease);
            if (assetUrl is null)
                throw new InvalidOperationException(
                    $"未找到适用于 {RuntimeInformation.ProcessArchitecture} 的下载文件。");

            AppDiagnostics.Log($"Downloading update from: {assetUrl}");

            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前 exe 路径。");
            string exeDir = Path.GetDirectoryName(exePath)
                ?? throw new InvalidOperationException("无法确定 exe 目录。");
            string newExePath = Path.Combine(exeDir, "PeekDesktop.new.exe");
            string oldExePath = Path.Combine(exeDir, "PeekDesktop.old.exe");
            string tempZipPath = Path.Combine(
                Path.GetTempPath(),
                $"PeekDesktop-update-{Guid.NewGuid():N}.zip");

            try
            {
                // Step 1: Download zip to %TEMP%
                await Task.Run(() =>
                    WinHttp.DownloadToFile(assetUrl, "PeekDesktop", tempZipPath, timeoutSeconds: 120));
                AppDiagnostics.Log($"Downloaded update zip to: {tempZipPath}");

                // Step 2: Extract PeekDesktop.exe from zip as PeekDesktop.new.exe
                ExtractExeFromZip(tempZipPath, newExePath);
                AppDiagnostics.Log($"Extracted new exe to: {newExePath}");

                // Step 3: Verify Authenticode signature and signer identity
                var (isValid, signerName) = NativeMethods.VerifyAuthenticodeSignature(newExePath);
                AppDiagnostics.Log($"Authenticode check: valid={isValid}, signer='{signerName ?? "(none)"}'");
                if (!isValid)
                {
                    throw new InvalidOperationException(
                        $"下载的更新没有有效的代码签名" +
                        $"（签名者：{signerName ?? "未知"}）。" +
                        "为了您的安全，已取消本次更新。");
                }

                // Step 4: Preflight — verify we can write to the exe directory
                if (!CanWriteToDirectory(exeDir))
                {
                    throw new InvalidOperationException(
                        "PeekDesktop 无法在此文件夹中更新自身（权限不足）。\n\n" +
                        "请将 PeekDesktop 移动到一个用户可写的文件夹，或手动下载更新。");
                }

                // Step 5: Rename dance — swap the exe in place
                File.Move(exePath, oldExePath, overwrite: true);
                AppDiagnostics.Log("Renamed current exe to .old");

                File.Move(newExePath, exePath);
                AppDiagnostics.Log("Renamed new exe into place");

                // Step 6: Clean up temp zip
                try { File.Delete(tempZipPath); }
                catch { /* best effort */ }

                // Step 7: Relaunch — launch FIRST, then release mutex and exit
                AppDiagnostics.Log("Update complete — relaunching PeekDesktop");

                if (!NativeMethods.LaunchProcess(exePath, "--restarting"))
                {
                    NativeMethods.MessageBoxW(
                        IntPtr.Zero,
                        "更新已安装，但 PeekDesktop 无法自动重启。\n\n" +
                        "请手动启动 PeekDesktop。",
                        "更新已完成",
                        NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
                    return;
                }

                // New process is launched — clean up and exit
                RemoveTrayIcon?.Invoke();
                ReleaseMutex?.Invoke();
                Environment.Exit(0);
            }
            catch
            {
                // Rollback: if we renamed the exe but failed, try to restore
                if (!File.Exists(exePath) && File.Exists(oldExePath))
                {
                    try
                    {
                        File.Move(oldExePath, exePath);
                        AppDiagnostics.Log("Rolled back exe rename after failure");
                    }
                    catch (Exception rollbackEx)
                    {
                        AppDiagnostics.Log($"Rollback failed: {rollbackEx.Message}");
                    }
                }

                // Clean up partial downloads
                try { if (File.Exists(newExePath)) File.Delete(newExePath); }
                catch { /* best effort */ }
                try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); }
                catch { /* best effort */ }

                throw;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Update install failed: {ex}");

            NativeMethods.MessageBoxW(
                IntPtr.Zero,
                $"PeekDesktop 无法安装更新。\n\n{ex.Message}",
                "更新错误",
                NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
        }
        finally
        {
            Interlocked.Exchange(ref _isUpdating, 0);
        }
    }

    /// <summary>
    /// Extracts PeekDesktop.exe from the downloaded zip to the specified destination path.
    /// </summary>
    private static void ExtractExeFromZip(string zipPath, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        ZipArchiveEntry? exeEntry = null;
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Equals("PeekDesktop.exe", StringComparison.OrdinalIgnoreCase))
            {
                exeEntry = entry;
                break;
            }
        }

        if (exeEntry is null)
            throw new InvalidOperationException("更新压缩包中不包含 PeekDesktop.exe 文件。");

        exeEntry.ExtractToFile(destinationPath, overwrite: true);
    }

    /// <summary>
    /// Finds the download URL for the correct architecture asset in the release.
    /// </summary>
    private static string? FindMatchingAssetUrl(GitHubReleaseInfo release)
    {
        string archSuffix = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(archSuffix))
            return null;

        foreach (var asset in release.Assets)
        {
            if (asset.Name.Contains(archSuffix, StringComparison.OrdinalIgnoreCase)
                && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return asset.BrowserDownloadUrl;
            }
        }

        return null;
    }

    /// <summary>
    /// Deletes leftover files from a previous update (called on startup).
    /// </summary>
    public static void CleanupPreviousUpdate()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;

            string exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            if (string.IsNullOrEmpty(exeDir))
                return;

            string oldExePath = Path.Combine(exeDir, "PeekDesktop.old.exe");
            if (File.Exists(oldExePath))
            {
                File.Delete(oldExePath);
                AppDiagnostics.Log("Cleaned up PeekDesktop.old.exe from previous update");
            }

            string newExePath = Path.Combine(exeDir, "PeekDesktop.new.exe");
            if (File.Exists(newExePath))
            {
                File.Delete(newExePath);
                AppDiagnostics.Log("Cleaned up PeekDesktop.new.exe from previous update");
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Cleanup of old update files failed (non-fatal): {ex.Message}");
        }

        // Clean up any leftover temp zips
        try
        {
            string tempDir = Path.GetTempPath();
            foreach (string file in Directory.GetFiles(tempDir, "PeekDesktop-update-*.zip"))
            {
                try { File.Delete(file); }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    private static bool CanWriteToDirectory(string dirPath)
    {
        string testFile = Path.Combine(dirPath, $".peekdesktop-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(testFile, Array.Empty<byte>());
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidReleaseUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == "https"
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/a1waysbeta/PeekDesktop-cn/", StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseUpdateAvailable(string version, string releaseUrl)
    {
        AppDiagnostics.Log($"Update available: version={version}, url={releaseUrl}");

        void Raise() => UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(version, releaseUrl));

        if (_messageLoop is not null)
        {
            _messageLoop.BeginInvoke(Raise);
            return;
        }

        Raise();
    }

    private static async Task<GitHubReleaseInfo?> GetLatestReleaseAsync()
    {
        string json = await Task.Run(() =>
            WinHttp.Get(LatestReleaseApiUrl, "PeekDesktop", timeoutSeconds: 10));

        return DeserializeReleaseInfo(Encoding.UTF8.GetBytes(json));
    }

    private static GitHubReleaseInfo DeserializeReleaseInfo(ReadOnlySpan<byte> utf8Json)
    {
        var info = new GitHubReleaseInfo();
        var reader = new Utf8JsonReader(utf8Json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return info;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("tag_name"u8))
            {
                reader.Read();
                info.TagName = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("html_url"u8))
            {
                reader.Read();
                info.HtmlUrl = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("assets"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            var asset = DeserializeAsset(ref reader);
                            if (!string.IsNullOrEmpty(asset.Name))
                                info.Assets.Add(asset);
                        }
                    }
                }
            }
            else
            {
                reader.Skip();
            }
        }

        return info;
    }

    private static GitHubAssetInfo DeserializeAsset(ref Utf8JsonReader reader)
    {
        var asset = new GitHubAssetInfo();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("name"u8))
            {
                reader.Read();
                asset.Name = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("browser_download_url"u8))
            {
                reader.Read();
                asset.BrowserDownloadUrl = reader.GetString() ?? string.Empty;
            }
            else
            {
                reader.Skip();
            }
        }

        return asset;
    }

    internal static string GetCurrentVersion()
    {
        var (productVersion, fileVersion) = NativeMethods.GetExeVersionInfo();
        string? informationalVersion = productVersion;
        string rawVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            string normalizedInformational = NormalizeVersion(informationalVersion);
            string normalizedAssembly = fileVersion is null ? string.Empty : NormalizeVersion(fileVersion.ToString());
            string numericInformational = ExtractNumericPrefix(normalizedInformational);
            string numericAssembly = ExtractNumericPrefix(normalizedAssembly);

            rawVersion = numericInformational == "1.0.0" && numericInformational == numericAssembly
                ? "0.0.0-dev"
                : informationalVersion;
        }
        else
        {
            rawVersion = fileVersion is null || fileVersion == new Version(1, 0, 0, 0)
                ? "0.0.0-dev"
                : fileVersion.ToString();
        }

        return NormalizeVersion(rawVersion);
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        string latestCore = ExtractNumericPrefix(latestVersion);
        string currentCore = ExtractNumericPrefix(currentVersion);

        if (Version.TryParse(latestCore, out var latest) && Version.TryParse(currentCore, out var current))
            return latest > current;

        return !string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version)
    {
        string normalized = version.Trim();

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        int plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
            normalized = normalized[..plusIndex];

        return normalized;
    }

    private static string ExtractNumericPrefix(string version)
    {
        string normalized = NormalizeVersion(version);
        int dashIndex = normalized.IndexOf('-');
        return dashIndex >= 0 ? normalized[..dashIndex] : normalized;
    }
}

internal sealed class UpdateAvailableEventArgs : EventArgs
{
    public UpdateAvailableEventArgs(string version, string releaseUrl)
    {
        Version = version;
        ReleaseUrl = releaseUrl;
    }

    public string Version { get; }
    public string ReleaseUrl { get; }
}

internal sealed class GitHubReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public List<GitHubAssetInfo> Assets { get; set; } = new();
}

internal sealed class GitHubAssetInfo
{
    public string Name { get; set; } = string.Empty;
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
