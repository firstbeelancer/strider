namespace Strider.Core.Text;

/// <summary>
/// Formatting applied to a range of the composed text. Empty/neutral values
/// mean "inherit from the document defaults".
/// </summary>
public sealed record TextStyle
{
    public static readonly TextStyle Default = new();

    public string? Color { get; init; }
    public string? Highlight { get; init; }
    public string? FontFamily { get; init; }
    public int? FontSize { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }

    /// <summary>True when nothing at all is set — such spans are dropped.</summary>
    public bool IsEmpty =>
        Color is null && Highlight is null && FontFamily is null && FontSize is null &&
        !Bold && !Italic && !Underline;

    /// <summary>Merges <paramref name="over"/> on top of this style (later value wins).</summary>
    public TextStyle Merge(TextStyle over) => new()
    {
        Color = over.Color ?? Color,
        Highlight = over.Highlight ?? Highlight,
        FontFamily = over.FontFamily ?? FontFamily,
        FontSize = over.FontSize ?? FontSize,
        Bold = over.Bold || Bold,
        Italic = over.Italic || Italic,
        Underline = over.Underline || Underline,
    };

    public TextStyle WithoutColor() => this with { Color = null };
    public TextStyle WithoutHighlight() => this with { Highlight = null };
    public TextStyle WithoutFontFamily() => this with { FontFamily = null };
    public TextStyle WithoutFontSize() => this with { FontSize = null };
    public TextStyle WithoutBold() => this with { Bold = false };
    public TextStyle WithoutItalic() => this with { Italic = false };
    public TextStyle WithoutUnderline() => this with { Underline = false };
}
