namespace Snapshot;

public sealed record LanguageOption(string Tag, string Name);

public static class TranslationLanguageOptions
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁體中文"),
        new("en", "English"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("pt", "Português"),
        new("ru", "Русский")
    ];

    public static string GetDisplayName(string tag) =>
        All.FirstOrDefault(
            language => string.Equals(
                language.Tag,
                tag,
                StringComparison.OrdinalIgnoreCase))?.Name ?? tag;
}
