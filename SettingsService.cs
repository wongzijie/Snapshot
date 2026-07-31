using System.Text.Json;

namespace Snapshot;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Snapshot",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            return Normalize(settings);
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, _jsonOptions));
        File.Move(tempPath, SettingsPath, true);
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        if (settings is null)
        {
            return AppSettings.CreateDefault();
        }

        if (settings.ClipboardHotkey is null || !settings.ClipboardHotkey.IsValid)
        {
            settings.ClipboardHotkey = AppSettings.DefaultClipboardHotkey();
        }

        if (settings.SaveHotkey is null || !settings.SaveHotkey.IsValid)
        {
            settings.SaveHotkey = AppSettings.DefaultSaveHotkey();
        }

        if (string.IsNullOrWhiteSpace(settings.SaveDirectory))
        {
            settings.SaveDirectory = KnownFolders.GetDownloadsPath();
        }

        settings.JpegQuality = Math.Clamp(settings.JpegQuality, 60, 100);
        return settings;
    }
}
