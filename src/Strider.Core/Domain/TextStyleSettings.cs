namespace Strider.Core.Domain;

/// <summary>
/// The user's default text style for new mail: font, size, colour and
/// highlight. Stored in <c>app_settings</c> as JSON under one key, so adding a
/// field later needs no migration.
/// </summary>
public class TextStyleSettings
{
    public const string SettingsKey = "text.style";

    /// <summary>Family applied to the whole body ("Inter", "Georgia", a font the user added...).</summary>
    public string FontFamily { get; set; } = "Inter";

    /// <summary>Base size in pixels (8–72).</summary>
    public int FontSize { get; set; } = 14;

    /// <summary>Default text colour as #RRGGBB.</summary>
    public string TextColor { get; set; } = "#1A1A1A";

    /// <summary>Default highlight (background) colour; empty = none.</summary>
    public string HighlightColor { get; set; } = "";

    /// <summary>Colours offered first in the composer palette.</summary>
    public List<string> RecentColors { get; set; } = new();

    public static readonly IReadOnlyList<string> Palette = new[]
    {
        "#1A1A1A", "#E5484D", "#F76B15", "#E2A336", "#30A46C", "#3E63DD", "#8E4EC6", "#8D8D8D",
    };

    public static readonly IReadOnlyList<int> Sizes = new[] { 10, 12, 14, 16, 18, 24, 32 };

    /// <summary>CSS for the wrapper element around the body.</summary>
    public string ToCss()
    {
        var css = $"font-family:'{FontFamily}',sans-serif;font-size:{FontSize}px;color:{TextColor};";
        if (!string.IsNullOrWhiteSpace(HighlightColor))
            css += $"background-color:{HighlightColor};";
        return css;
    }

    public TextStyleSettings Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        TextColor = TextColor,
        HighlightColor = HighlightColor,
        RecentColors = new List<string>(RecentColors),
    };
}

/// <summary>A font file the user added, so it survives restarts and is offered in lists.</summary>
public class CustomFont
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Family { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
