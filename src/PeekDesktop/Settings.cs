using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PeekDesktop;

/// <summary>
/// Persists user settings (enabled state, autostart) as JSON in %APPDATA%
/// and manages the Windows Run registry key for auto-start.
/// </summary>
public sealed class Settings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PeekDesktop");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize(json, PeekDesktopJsonContext.Default.Settings) ?? new Settings();
            }
        }
        catch
        {
            // Fall through to defaults
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(this, PeekDesktopJsonContext.Default.Settings);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort persistence
        }
    }

    /// <summary>
    /// Adds or removes a registry entry under HKCU\...\Run to launch PeekDesktop at login.
    /// No admin rights required.
    /// </summary>
    public static void SetAutoStart(bool enabled)
    {
        try
        {
            const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "PeekDesktop";

            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key == null)
                return;

            if (enabled)
            {
                string exePath = Environment.ProcessPath ?? "";
                key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort — might fail in rare edge cases
        }
    }
}
