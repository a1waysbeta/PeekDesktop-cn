using System;
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
    private bool _isChecking;
    private string? _latestReleaseUrl;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    public AppUpdater(Win32MessageLoop? messageLoop = null)
    {
        _messageLoop = messageLoop;
    }

    public async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_isChecking)
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

        _isChecking = true;

        try
        {
            AppDiagnostics.Log(interactive ? "Manual update check started" : "Background update check started");

            GitHubReleaseInfo? release = await GetLatestReleaseAsync();
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                throw new InvalidOperationException("GitHub 未返回有效的版本信息。");

            string latestVersion = NormalizeVersion(release.TagName);
            string currentVersion = GetCurrentVersion();
            _latestReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl;

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

            int result = NativeMethods.MessageBoxW(
                IntPtr.Zero,
                $"PeekDesktop {latestVersion} 已发布。\n\n是否打开 GitHub 发布页面进行下载？",
                "发现更新",
                NativeMethods.MB_YESNO | NativeMethods.MB_ICONINFORMATION);

            if (result == NativeMethods.IDYES)
                OpenLatestReleasePage();
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
            _isChecking = false;
        }
    }

    public void OpenLatestReleasePage()
    {
        string url = _latestReleaseUrl ?? ReleasesPageUrl;

        // Validate URL before launching — only allow https://github.com/a1waysbeta/PeekDesktop-cn/
        if (!IsValidReleaseUrl(url))
        {
            AppDiagnostics.Log($"Update URL failed validation, using hardcoded fallback: {url}");
            url = ReleasesPageUrl;
        }

        NativeMethods.ShellExecuteW(IntPtr.Zero, "open", url, null, null, NativeMethods.SW_SHOWNORMAL);
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
        // WinHttp is synchronous; run on thread pool to keep UI responsive
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
            else
            {
                reader.Skip();
            }
        }

        return info;
    }

    private static string GetCurrentVersion()
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

            // If version metadata falls back to the SDK default 1.0.0, treat it as a dev build
            // so GitHub releases still show as updates during testing.
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
}
