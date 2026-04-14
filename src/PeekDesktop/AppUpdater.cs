using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PeekDesktop;

internal sealed class AppUpdater
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/shanselman/PeekDesktop/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/shanselman/PeekDesktop/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly SynchronizationContext? _syncContext = SynchronizationContext.Current;
    private bool _isChecking;
    private string? _latestReleaseUrl;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    public async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_isChecking)
        {
            if (interactive)
            {
                MessageBox.Show(
                    "PeekDesktop is already checking for updates.",
                    "PeekDesktop Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        _isChecking = true;

        try
        {
            AppDiagnostics.Log(interactive ? "Manual update check started" : "Background update check started");

            GitHubReleaseInfo? release = await GetLatestReleaseAsync();
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                throw new InvalidOperationException("GitHub did not return a usable release.");

            string latestVersion = NormalizeVersion(release.TagName);
            string currentVersion = GetCurrentVersion();
            _latestReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl;

            AppDiagnostics.Log($"Current version={currentVersion}, latest version={latestVersion}");

            if (!IsNewerVersion(latestVersion, currentVersion))
            {
                if (interactive)
                {
                    MessageBox.Show(
                        $"You're already on the latest version of PeekDesktop ({currentVersion}).",
                        "PeekDesktop Update",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            if (!interactive)
            {
                RaiseUpdateAvailable(latestVersion, _latestReleaseUrl);
                return;
            }

            DialogResult result = MessageBox.Show(
                $"PeekDesktop {latestVersion} is available.\n\nOpen the GitHub release page to download it?",
                "Update Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
                OpenLatestReleasePage();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Update check failed: {ex}");

            if (interactive)
            {
                MessageBox.Show(
                    $"PeekDesktop couldn't check for updates.\n\n{ex.Message}",
                    "Update Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void RaiseUpdateAvailable(string version, string releaseUrl)
    {
        if (_syncContext is not null)
        {
            _syncContext.Post(_ => UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(version, releaseUrl)), null);
            return;
        }

        UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(version, releaseUrl));
    }

    private static async Task<GitHubReleaseInfo?> GetLatestReleaseAsync()
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(LatestReleaseApiUrl);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await System.Text.Json.JsonSerializer.DeserializeAsync(
            stream,
            PeekDesktopJsonContext.Default.GitHubReleaseInfo);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("PeekDesktop");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetCurrentVersion()
    {
        var assembly = typeof(Program).Assembly;
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string rawVersion = informationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
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
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
}
