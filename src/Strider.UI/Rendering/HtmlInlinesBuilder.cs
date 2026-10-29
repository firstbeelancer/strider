using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Strider.UI.Rendering;

/// <summary>
/// Turns (sanitized) mail HTML into Avalonia <see cref="Inline"/> objects —
/// enough to show bold, italic, underline, colours, highlights, fonts and sizes
/// without a WebView. That is what makes "colour these words blue" visible in
/// the reader and in the composer preview, today, on both platforms.
///
/// Deliberately not a browser: tables are flattened into rows of text, images
/// are skipped (no remote loading), unknown elements contribute their text.
/// </summary>
public static class HtmlInlinesBuilder
{
    private static readonly HtmlParser Parser = new();

    /// <summary>Builds the inline collection for a block of HTML.</summary>
    public static IList<Inline> Build(string? html, TextRenderDefaults? defaults = null)
    {
        defaults ??= new TextRenderDefaults();
        var inlines = new List<Inline>();
        if (string.IsNullOrWhiteSpace(html)) return inlines;

        var document = Parser.ParseDocument(html);
        var context = new RenderContext(defaults, inlines);
        Walk(document.Body ?? document.DocumentElement, context, new InlineStyle());
        return inlines;
    }

    /// <summary>Plain text of the HTML — used for snippets and for the text/plain part.</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var document = Parser.ParseDocument(html);
        var sb = new StringBuilder();

        void WalkText(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IText text)
                {
                    sb.Append(text.Data);
                }
                else if (child is IElement element)
                {
                    var name = element.LocalName;
                    if (name is "br") sb.Append('\n');
                    else if (name is "p" or "div" or "li" or "tr" or "h1" or "h2" or "h3" or "blockquote")
                    {
                        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                        WalkText(element);
                        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                    }
                    else if (name is "script" or "style") { /* skip */ }
                    else WalkText(element);
                }
            }
        }

        WalkText(document.Body ?? document.DocumentElement);
        return sb.ToString().Trim();
    }

    private sealed class RenderContext
    {
        public RenderContext(TextRenderDefaults defaults, List<Inline> target)
        {
            Defaults = defaults;
            Target = target;
        }

        public TextRenderDefaults Defaults { get; }
        public List<Inline> Target { get; }
    }

    /// <summary>The subset of CSS we honour while walking the tree.</summary>
    private struct InlineStyle
    {
        public Color? Foreground;
        public Color? Background;
        public string? FontFamily;
        public double? FontSize;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;
        public bool Monospace;

        public bool IsNeutral =>
            Foreground is null && Background is null && FontFamily is null && FontSize is null &&
            !Bold && !Italic && !Underline && !Strikethrough && !Monospace;

        public InlineStyle WithChild(IElement element)
        {
            var style = this;
            switch (element.LocalName)
            {
                case "b":
                case "strong":
                    style.Bold = true;
                    break;
                case "i":
                case "em":
                    style.Italic = true;
                    break;
                case "u":
                case "ins":
                    style.Underline = true;
                    break;
                case "s":
                case "strike":
                case "del":
                    style.Strikethrough = true;
                    break;
                case "code":
                case "pre":
                case "kbd":
                case "samp":
                    style.Monospace = true;
                    break;
                case "a":
                    style.Underline = true;
                    style.Foreground ??= Colors.SteelBlue;
                    break;
                case "h1":
                    style.FontSize ??= 24;
                    style.Bold = true;
                    break;
                case "h2":
                    style.FontSize ??= 20;
                    style.Bold = true;
                    break;
                case "h3":
                    style.FontSize ??= 17;
                    style.Bold = true;
                    break;
                case "small":
                    style.FontSize ??= 11;
                    break;
            }

            ApplyCss(element.GetAttribute("style"), ref style);
            return style;
        }
    }

    private static void ApplyCss(string? css, ref InlineStyle style)
    {
        if (string.IsNullOrWhiteSpace(css)) return;

        foreach (var declaration in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;

            var property = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim().Trim('"', '\'');
            if (value.Length == 0) continue;

            switch (property)
            {
                case "color":
                    if (TryParseColor(value, out var fg)) style.Foreground = fg;
                    break;
                case "background-color":
                case "background":
                    if (TryParseColor(value, out var bg)) style.Background = bg;
                    break;
                case "font-family":
                    var family = value.Split(',')[0].Trim().Trim('"', '\'');
                    if (family.Length > 0) style.FontFamily = family;
                    break;
                case "font-size":
                    if (TryParsePixels(value, out var size)) style.FontSize = size;
                    break;
                case "font-weight":
                    if (value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                        (int.TryParse(value, out var weight) && weight >= 600))
                        style.Bold = true;
                    break;
                case "font-style":
                    if (value.Contains("italic", StringComparison.OrdinalIgnoreCase)) style.Italic = true;
                    break;
                case "text-decoration":
                case "text-decoration-line":
                    if (value.Contains("underline", StringComparison.OrdinalIgnoreCase)) style.Underline = true;
                    if (value.Contains("line-through", StringComparison.OrdinalIgnoreCase)) style.Strikethrough = true;
                    break;
            }
        }
    }

    private static bool TryParsePixels(string value, out double size)
    {
        size = 0;
        var raw = value.Replace("px", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("pt", "", StringComparison.OrdinalIgnoreCase)
                       .Trim();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
        if (value.Contains("pt", StringComparison.OrdinalIgnoreCase)) parsed *= 1.333; // pt → px
        if (parsed is < 6 or > 96) return false;
        size = parsed;
        return true;
    }

    /// <summary>Accepts #rgb, #rrggbb, rgb(r,g,b) and the common CSS names.</summary>
    public static bool TryParseColor(string value, out Color color)
    {
        color = default;
        var text = value.Trim();
        if (text.Length == 0) return false;

        if (text[0] == '#')
        {
            var hex = text[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length == 8) hex = hex[..6];
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
                return false;

            color = Color.FromRgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
            return true;
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = text.IndexOf('(');
            var close = text.IndexOf(')');
            if (open < 0 || close < open) return false;
            var parts = text[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3) return false;
            if (byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
            {
                color = Color.FromRgb(r, g, b);
                return true;
            }
            return false;
        }

        try
        {
            color = Color.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Walk(INode? node, RenderContext context, InlineStyle style)
    {
        if (node is null) return;

        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    AppendText(context, style, NormalizeWhitespace(text.Data));
                    break;

                case IElement element:
                {
                    var name = element.LocalName;

                    if (name is "script" or "style" or "head" or "title") break;

                    if (name is "br")
                    {
                        context.Target.Add(new LineBreak());
                        break;
                    }

                    if (name is "img")
                    {
                        AppendText(context, style with { Foreground = Colors.Gray },
                            element.GetAttribute("alt") is { Length: > 0 } alt ? $"[{alt}]" : "[image]");
                        break;
                    }

                    if (name is "li")
                    {
                        AppendText(context, style, "• ");
                    }

                    if (name is "p" or "div" or "li" or "tr" or "blockquote" or "h1" or "h2" or "h3")
                        EnsureLineStart(context);

                    if (name is "blockquote")
                        AppendText(context, style with { Foreground = style.Foreground ?? Colors.DimGray }, "| ");

                    Walk(element, context, style.WithChild(element));

                    if (name is "p" or "div" or "tr" or "li" or "blockquote" or "h1" or "h2" or "h3")
                        context.Target.Add(new LineBreak());

                    if (name is "td" or "th")
                        AppendText(context, style, "\t");

                    break;
                }
            }
        }
    }

    /// <summary>Collapses runs of whitespace the way a browser would inside a block.</summary>
    private static string NormalizeWhitespace(string data)
    {
        if (data.Length == 0) return data;
        var sb = new StringBuilder(data.Length);
        var lastWasSpace = false;
        foreach (var ch in data)
        {
            var isSpace = ch is ' ' or '\r' or '\n' or '\t';
            if (isSpace)
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static void EnsureLineStart(RenderContext context)
    {
        if (context.Target.Count == 0) return;
        if (context.Target[^1] is LineBreak) return;
        context.Target.Add(new LineBreak());
    }

    private static void AppendText(RenderContext context, InlineStyle style, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var run = new Run(text);
        if (style.Foreground is { } fg) run.Foreground = new SolidColorBrush(fg);
        if (style.Background is { } bg) run.Background = new SolidColorBrush(bg);
        if (style.FontSize is { } size) run.FontSize = size;
        if (style.FontFamily is { Length: > 0 } family) run.FontFamily = new FontFamily(family);
        if (style.Monospace) run.FontFamily = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,monospace");
        if (style.Bold) run.FontWeight = FontWeight.Bold;
        if (style.Italic) run.FontStyle = FontStyle.Italic;
        if (style.Underline || style.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (style.Underline)
                decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (style.Strikethrough)
                decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            run.TextDecorations = decorations;
        }

        context.Target.Add(run);
    }
}

/// <summary>Fallback metrics used when the HTML does not specify a font/size.</summary>
public sealed class TextRenderDefaults
{
    public string FontFamily { get; init; } = "Inter";
    public double FontSize { get; init; } = 14;
    public Color Foreground { get; init; } = Color.Parse("#1A1A1A");
}
