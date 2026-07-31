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
            settings = Normalize(settings);
            if (!string.IsNullOrWhiteSpace(settings.TranslatorApiKeyProtected))
            {
                settings.TranslatorApiKey = CredentialProtector.Unprotect(
                    settings.TranslatorApiKeyProtected);
            }

            return settings;
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

        var storedSettings = settings.Clone();
        storedSettings.TranslatorApiKeyProtected =
            string.IsNullOrWhiteSpace(settings.TranslatorApiKey)
                ? string.Empty
                : CredentialProtector.Protect(settings.TranslatorApiKey);

        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(storedSettings, _jsonOptions));
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
        settings.OcrLanguageTag = settings.OcrLanguageTag?.Trim() ?? string.Empty;
        settings.TranslationTargetLanguage =
            string.IsNullOrWhiteSpace(settings.TranslationTargetLanguage)
                ? "zh-Hans"
                : settings.TranslationTargetLanguage.Trim();
        settings.TranslatorEndpoint =
            string.IsNullOrWhiteSpace(settings.TranslatorEndpoint)
                ? "https://api.cognitive.microsofttranslator.com"
                : settings.TranslatorEndpoint.Trim().TrimEnd('/');
        settings.TranslatorRegion = settings.TranslatorRegion?.Trim() ?? string.Empty;
        return settings;
    }
}
