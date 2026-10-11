using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MarkdownPad.Services;

/// <summary>User state that survives a restart.</summary>
public sealed class AppSettings
{
    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    public int WindowX { get; set; } = int.MinValue;

    public int WindowY { get; set; } = int.MinValue;

    public bool WindowMaximized { get; set; }

    public double EditorColumnWidth { get; set; } = 560;

    public string EditorFontFamily { get; set; } = "Cascadia Mono, Consolas, Courier New";

    public double EditorFontSize { get; set; } = 14;

    public bool ShowPreview { get; set; } = true;

    public bool WordWrap { get; set; } = true;

    public string PreviewTheme { get; set; } = "light";

    /// <summary>Application chrome theme: "light", "dark" or "system".</summary>
    public string AppTheme { get; set; } = "system";

    /// <summary>UI language: "zh-CN" or "en-US".</summary>
    public string Language { get; set; } = "zh-CN";

    public string? LastFilePath { get; set; }
}

/// <summary>Loads and stores <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkdownPad");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string DraftFile => Path.Combine(DataDirectory, "draft.md");

    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public static string AppDirectory { get; } = AppContext.BaseDirectory;

    public static string AssetsDirectory => Path.Combine(AppDirectory, "Assets");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or inaccessible settings must never stop the app from starting.
        }

        return new AppSettings();
    }

    public static async Task SaveAsync(AppSettings settings)
    {
        // The self test harness must not overwrite the user's real state.
        if (App.IsSelfTest)
        {
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(settings, Options);
            await File.WriteAllTextAsync(AppPaths.SettingsFile, json, new UTF8Encoding(false));
        }
        catch
        {
            // Best effort only.
        }
    }
}
