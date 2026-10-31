using System.Net;
using System.Text;

namespace Strider.Core.Text;

/// <summary>
/// A piece of text with per-range formatting — the model behind the composer's
/// "select words and colour them" toolbar.
///
/// The plain text stays the source of truth (so the user keeps editing it in a
/// normal text box); formatting is a set of <see cref="StyleSpan"/> ranges that
/// are re-applied in <see cref="ToHtml"/>. Ranges are split correctly when two
/// styles overlap, which is what makes "colour a phrase, then bold a part of it"
/// behave the way people expect.
/// </summary>
public sealed class RichTextDocument
{
    private readonly List<StyleSpan> _spans = new();

    public string Text { get; private set; } = string.Empty;

    /// <summary>Defaults applied to the whole body.</summary>
    public TextStyle DefaultStyle { get; set; } = TextStyle.Default;

    public IReadOnlyList<StyleSpan> Spans => _spans;

    public RichTextDocument(string text = "")
    {
        Text = text ?? string.Empty;
    }

    /// <summary>
    /// Replaces the text; formatting follows the edit. The change is found as the
    /// region between the common prefix and suffix: spans after it shift, spans
    /// around it grow or shrink, and text typed right next to a span stays unstyled.
    /// </summary>
    public void SetText(string text)
    {
        var old = Text;
        Text = text ?? string.Empty;
        if (_spans.Count == 0 || old == Text) return;

        var limit = Math.Min(old.Length, Text.Length);
        var prefix = 0;
        while (prefix < limit && old[prefix] == Text[prefix]) prefix++;
        var suffix = 0;
        while (suffix < limit - prefix && old[old.Length - 1 - suffix] == Text[Text.Length - 1 - suffix]) suffix++;

        var oldEnd = old.Length - suffix;
        var newEnd = Text.Length - suffix;
        var delta = newEnd - oldEnd;

        var moved = new List<StyleSpan>(_spans.Count);
        foreach (var span in _spans)
        {
            var end = span.Start + span.Length;
            var start = span.Start >= oldEnd ? span.Start + delta
                : span.Start <= prefix ? span.Start
                : newEnd;
            var stop = end <= prefix ? end
                : end >= oldEnd ? end + delta
                : prefix;
            if (stop > start) moved.Add(span with { Start = start, Length = stop - start });
        }

        _spans.Clear();
        _spans.AddRange(Normalize(moved));
    }

    /// <summary>
    /// Applies <paramref name="style"/> to [start, start+length). Existing spans are
    /// split so the new style only affects exactly that range.
    /// </summary>
    public void Apply(int start, int length, TextStyle style)
    {
        if (length <= 0 || style.IsEmpty) return;
        if (start < 0)
        {
            // A range that starts before the text is clipped, not shifted: the
            // part outside the document carries no formatting.
            length += start;
            start = 0;
            if (length <= 0) return;
        }
        if (start >= Text.Length) return;
        length = Math.Min(length, Text.Length - start);
        if (length <= 0) return;

        var end = start + length;
        var result = new List<StyleSpan>();

        foreach (var span in _spans)
        {
            var spanEnd = span.Start + span.Length;

            // No overlap: keep as is.
            if (spanEnd <= start || span.Start >= end)
            {
                result.Add(span);
                continue;
            }

            // Left remainder.
            if (span.Start < start)
                result.Add(span with { Length = start - span.Start });

            // Right remainder.
            if (spanEnd > end)
                result.Add(span with
                {
                    Start = end,
                    Length = spanEnd - end,
                });

            // Overlapping middle gets the merged style, added below.
        }

        var overlapping = _spans
            .Where(s => s.Start < end && s.Start + s.Length > start)
            .Select(s => s.Style)
            .ToList();

        var merged = style;
        foreach (var existing in overlapping) merged = existing.Merge(merged);

        if (!merged.IsEmpty) result.Add(new StyleSpan(start, length, merged));

        _spans.Clear();
        _spans.AddRange(Normalize(result));
    }

    /// <summary>
    /// Removes a property (or several) from a range — used by "clear colour"
    /// and by toggling bold/italic/underline off.
    /// </summary>
    public void Clear(int start, int length, Func<TextStyle, TextStyle> strip)
    {
        if (length <= 0) return;
        if (start < 0) start = 0;
        if (start >= Text.Length) return;
        length = Math.Min(length, Text.Length - start);
        if (length <= 0) return;

        var end = start + length;
        var result = new List<StyleSpan>();

        foreach (var span in _spans)
        {
            var spanEnd = span.Start + span.Length;
            if (spanEnd <= start || span.Start >= end)
            {
                result.Add(span);
                continue;
            }

            // Part before the cleared range keeps its style untouched.
            if (span.Start < start)
                result.Add(span with { Length = start - span.Start });

            // The overlapped part: strip the property.
            var overlapStart = Math.Max(span.Start, start);
            var overlapEnd = Math.Min(spanEnd, end);
            var stripped = strip(span.Style);
            if (!stripped.IsEmpty)
                result.Add(new StyleSpan(overlapStart, overlapEnd - overlapStart, stripped));

            // Part after keeps its style.
            if (spanEnd > end)
                result.Add(span with { Start = end, Length = spanEnd - end });
        }

        _spans.Clear();
        _spans.AddRange(Normalize(result));
    }

    /// <summary>True when any span in the range has the property (for toggle buttons).</summary>
    public bool HasInRange(int start, int length, Func<TextStyle, bool> predicate)
    {
        var end = start + Math.Max(length, 0);
        return _spans.Any(s =>
            s.Start < end && s.Start + s.Length > start && predicate(s.Style));
    }

    public void ClearAll() => _spans.Clear();

    /// <summary>
    /// Renders the document as HTML: a wrapper div with the default style, then
    /// runs of text wrapped in spans wherever formatting applies. Plain text is
    /// HTML-encoded, line breaks become &lt;br&gt;.
    /// </summary>
    public string ToHtml(string? wrapCss = null)
    {
        var css = wrapCss ?? BuildDefaultCss();
        var builder = new StringBuilder();
        builder.Append("<div style=\"").Append(css).Append("\">");

        if (_spans.Count == 0)
        {
            AppendEncoded(builder, Text);
        }
        else
        {
            var boundaries = new SortedSet<int> { 0, Text.Length };
            foreach (var span in _spans)
            {
                boundaries.Add(Math.Clamp(span.Start, 0, Text.Length));
                boundaries.Add(Math.Clamp(span.Start + span.Length, 0, Text.Length));
            }

            var points = boundaries.ToList();
            for (var i = 0; i < points.Count - 1; i++)
            {
                var from = points[i];
                var to = points[i + 1];
                if (to <= from) continue;

                var style = StyleAt(from);
                var chunk = Text.Substring(from, to - from);
                if (style.IsEmpty)
                {
                    AppendEncoded(builder, chunk);
                }
                else
                {
                    builder.Append("<span style=\"").Append(Css(style)).Append("\">");
                    AppendEncoded(builder, chunk);
                    builder.Append("</span>");
                }
            }
        }

        builder.Append("</div>");
        return builder.ToString();
    }

    private string BuildDefaultCss()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(DefaultStyle.FontFamily))
            sb.Append($"font-family:'{DefaultStyle.FontFamily}',sans-serif;");
        if (DefaultStyle.FontSize is { } size) sb.Append($"font-size:{size}px;");
        if (!string.IsNullOrWhiteSpace(DefaultStyle.Color)) sb.Append($"color:{DefaultStyle.Color};");
        return sb.ToString();
    }

    private TextStyle StyleAt(int index)
    {
        // Later spans win, same as applying them in order.
        var style = TextStyle.Default;
        foreach (var span in _spans.Where(s => s.Start <= index && index < s.Start + s.Length))
            style = style.Merge(span.Style);
        return style;
    }

    private static string Css(TextStyle style)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(style.Color)) sb.Append($"color:{style.Color};");
        if (!string.IsNullOrWhiteSpace(style.Highlight)) sb.Append($"background-color:{style.Highlight};");
        if (!string.IsNullOrWhiteSpace(style.FontFamily)) sb.Append($"font-family:'{style.FontFamily}',sans-serif;");
        if (style.FontSize is { } size) sb.Append($"font-size:{size}px;");
        if (style.Bold) sb.Append("font-weight:bold;");
        if (style.Italic) sb.Append("font-style:italic;");
        if (style.Underline) sb.Append("text-decoration:underline;");
        return sb.ToString();
    }

    private static void AppendEncoded(StringBuilder builder, string text)
    {
        // Encode everything, then turn real newlines into <br> so paragraphs survive.
        var encoded = WebUtility.HtmlEncode(text);
        builder.Append(encoded.Replace("\r\n", "\n").Replace("\n", "<br>"));
    }

    /// <summary>Drops empty spans, sorts them and merges touching identical styles.</summary>
    private static List<StyleSpan> Normalize(List<StyleSpan> spans)
    {
        var cleaned = spans
            .Where(s => s.Length > 0 && !s.Style.IsEmpty)
            .OrderBy(s => s.Start)
            .ThenBy(s => s.Length)
            .ToList();

        var merged = new List<StyleSpan>(cleaned.Count);
        foreach (var span in cleaned)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (last.Style == span.Style && last.Start + last.Length == span.Start)
                {
                    merged[^1] = last with { Length = last.Length + span.Length };
                    continue;
                }
            }

            merged.Add(span);
        }

        return merged;
    }
}

/// <summary>A formatted range: [Start, Start+Length) carries <paramref name="Style"/>.</summary>
public sealed record StyleSpan(int Start, int Length, TextStyle Style);
