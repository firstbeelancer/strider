using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Key/value preferences (JSON values). Text style, composer options and any
/// future toggle live here — one table, no migration per new setting.
/// </summary>
public interface IAppSettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>Typed helpers on top of <see cref="IAppSettingsStore"/>.</summary>
public static class AppSettingsExtensions
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    /// <summary>Reads the text style, falling back to defaults on a missing/!broken row.</summary>
    public static async Task<TextStyleSettings> GetTextStyleAsync(
        this IAppSettingsStore store, CancellationToken ct = default)
    {
        var json = await store.GetAsync(TextStyleSettings.SettingsKey, ct);
        if (string.IsNullOrWhiteSpace(json)) return new TextStyleSettings();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<TextStyleSettings>(json, JsonOptions)
                   ?? new TextStyleSettings();
        }
        catch (System.Text.Json.JsonException)
        {
            // Unreadable row (older version, hand-edited DB): fall back to defaults
            // rather than blocking the composer.
            return new TextStyleSettings();
        }
    }

    public static Task SaveTextStyleAsync(
        this IAppSettingsStore store, TextStyleSettings settings, CancellationToken ct = default) =>
        store.SetAsync(TextStyleSettings.SettingsKey,
            System.Text.Json.JsonSerializer.Serialize(settings, JsonOptions), ct);
}
