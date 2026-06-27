using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Text;

namespace Strider.Infrastructure.Security;

/// <summary>
/// Sanitizes untrusted email HTML using an allowlist approach.
/// Removes: &lt;script&gt;, &lt;iframe&gt;, &lt;object&gt;, &lt;embed&gt;, &lt;form&gt;,
/// inline event handlers (onclick, onload, ...), javascript: URLs,
/// external images by default (configurable).
///
/// This is the F-006 fix from the architecture review: MessageReaderViewModel
/// was assigning raw HTML to DisplayHtml without sanitization, creating an
/// XSS risk if a future migration to WebView-based rendering happens.
/// </summary>
public class HtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "blockquote",
        "br", "caption", "cite", "code", "col", "colgroup", "data", "dd", "del",
        "details", "dfn", "div", "dl", "dt", "em", "figcaption", "figure", "footer",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "i", "img", "ins",
        "kbd", "li", "main", "mark", "nav", "ol", "p", "pre", "q", "rp", "rt",
        "ruby", "s", "samp", "section", "small", "span", "strong", "sub", "summary",
        "sup", "table", "tbody", "td", "tfoot", "th", "thead", "time", "tr", "u",
        "ul", "var", "wbr",
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title", "alt", "src", "width", "height", "colspan", "rowspan",
        "datetime", "lang", "dir", "id", "class", "style",
    };

    private static readonly HashSet<string> DangerousAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "onclick", "onload", "onerror", "onmouseover", "onmouseout", "onfocus",
        "onblur", "onchange", "onsubmit", "onreset", "onkeydown", "onkeyup",
        "onkeypress", "oninput", "onselect",
    };

    private readonly bool _allowExternalImages;

    public HtmlSanitizer(bool allowExternalImages = false)
    {
        _allowExternalImages = allowExternalImages;
    }

    /// <summary>
    /// Sanitizes the given HTML. Returns safe HTML that can be rendered.
    /// If input is null or empty, returns empty string.
    /// </summary>
    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<div id=\"__root__\">{html}</div>");
        var root = document.GetElementById("__root__")!;

        SanitizeElement(root);

        using var writer = new StringWriter();
        root.ToHtml(writer, new HtmlMarkupFormatter());
        var result = writer.ToString();

        // Strip the wrapper div we added
        const string prefix = "<div id=\"__root__\">";
        const string suffix = "</div>";
        if (result.StartsWith(prefix, StringComparison.Ordinal) && result.EndsWith(suffix, StringComparison.Ordinal))
        {
            result = result.Substring(prefix.Length, result.Length - prefix.Length - suffix.Length);
        }
        return result;
    }

    private void SanitizeElement(IElement element)
    {
        // Process children first (depth-first)
        for (int i = element.Children.Length - 1; i >= 0; i--)
        {
            var child = element.Children[i];
            if (child is IHtmlUnknownElement || !AllowedTags.Contains(child.TagName))
            {
                // Replace disallowed element with its text content / children
                // For <script>, <style>, <iframe>, <object>, <embed>, <form>: drop entirely
                if (IsDangerousElement(child.TagName))
                {
                    child.Remove();
                    continue;
                }
                // For other disallowed (e.g., custom elements): unwrap (keep children)
                while (child.ChildNodes.Length > 0)
                {
                    var node = child.ChildNodes[0];
                    child.RemoveChild(node);
                    element.InsertBefore(node, child);
                }
                child.Remove();
                continue;
            }
            SanitizeElement(child);
        }

        // Sanitize attributes
        var attrsToRemove = new List<IAttr>();
        foreach (var attr in element.Attributes)
        {
            var name = attr.Name;
            var value = attr.Value;

            // Drop event handlers
            if (DangerousAttributes.Contains(name))
            {
                attrsToRemove.Add(attr);
                continue;
            }

            // Drop disallowed attributes
            if (!AllowedAttributes.Contains(name))
            {
                attrsToRemove.Add(attr);
                continue;
            }

            // Validate URL-bearing attributes
            if (name.Equals("href", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                if (IsDangerousUrl(value))
                {
                    attrsToRemove.Add(attr);
                    continue;
                }
                // Block external images unless allowed
                if (!_allowExternalImages && name.Equals("src", StringComparison.OrdinalIgnoreCase) &&
                    element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase) &&
                    IsExternalUrl(value))
                {
                    attrsToRemove.Add(attr);
                    continue;
                }
            }
        }

        foreach (var attr in attrsToRemove)
        {
            element.RemoveAttribute(attr.Name);
        }
    }

    private static bool IsDangerousElement(string tagName)
    {
        return tagName.Equals("SCRIPT", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("STYLE", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("IFRAME", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("OBJECT", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("EMBED", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("FORM", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("APPLET", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("BASE", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("META", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("LINK", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDangerousUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var trimmed = url.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("javascript:", StringComparison.Ordinal)) return true;
        if (trimmed.StartsWith("vbscript:", StringComparison.Ordinal)) return true;
        if (trimmed.StartsWith("data:text/html", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var trimmed = url.Trim().ToLowerInvariant();
        // Relative URLs and inline data: images are not external
        if (trimmed.StartsWith("data:", StringComparison.Ordinal) && !trimmed.StartsWith("data:text/html", StringComparison.Ordinal))
            return false;
        if (trimmed.StartsWith("cid:", StringComparison.Ordinal)) return false;
        if (trimmed.StartsWith("/", StringComparison.Ordinal)) return false;
        if (trimmed.StartsWith("#", StringComparison.Ordinal)) return false;
        if (!trimmed.Contains("://")) return false;
        return true;
    }
}
