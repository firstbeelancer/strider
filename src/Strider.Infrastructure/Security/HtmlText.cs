using AngleSharp.Html.Parser;

namespace Strider.Infrastructure.Security;

/// <summary>
/// Turns (already sanitized) HTML into readable plain text: keeps line breaks
/// for block elements, drops markup. Used by the reader until a WebView renderer lands.
/// </summary>
public static class HtmlText
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "table", "hr",
    };

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var doc = new HtmlParser().ParseDocument(html);
        var sb = new System.Text.StringBuilder();
        Walk(doc.Body ?? (AngleSharp.Dom.INode)doc.DocumentElement, sb);
        var lines = sb.ToString().Replace("\r", "").Split('\n').Select(l => l.TrimEnd());
        var text = string.Join("\n", lines);
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text.Trim();
    }

    private static void Walk(AngleSharp.Dom.INode node, System.Text.StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case AngleSharp.Dom.IText t:
                    sb.Append(System.Text.RegularExpressions.Regex.Replace(t.Data, "[ \t\r\n]+", " "));
                    break;
                case AngleSharp.Dom.IElement e:
                    var tag = e.LocalName;
                    if (tag is "script" or "style" or "head") break;
                    if (tag == "pre") { sb.Append('\n').Append(e.TextContent).Append('\n'); break; }
                    var block = BlockTags.Contains(tag);
                    if (block) sb.Append('\n');
                    if (tag == "li") sb.Append("• ");
                    Walk(e, sb);
                    if (tag == "a" && e.GetAttribute("href") is { Length: > 0 } href
                        && !href.StartsWith("mailto:") && href != e.TextContent.Trim())
                        sb.Append(" (").Append(href).Append(')');
                    if (block) sb.Append('\n');
                    break;
            }
        }
    }
}
