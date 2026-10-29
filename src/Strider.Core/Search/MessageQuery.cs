namespace Strider.Core.Search;

/// <summary>
/// A parsed search query — the mailbox search syntax from ТЗ §5.1 F2.8.
///
/// Supported tokens (case-insensitive keys):
///   from:alice            — sender address or display name contains "alice"
///   to:bob                — recipient (To/Cc) contains "bob"
///   subject:invoice       — subject contains "invoice"
///   body:"payment terms"  — body (plain or html) contains the phrase
///   is:unread / is:read   — read state
///   is:starred            — starred flag
///   has:attachment        — has at least one attachment
/// Any bare word is free text and is matched against subject, sender,
/// recipients and body.
///
/// Values may be quoted ("two words") and a field may be repeated; terms
/// inside one field are OR-ed, different fields are AND-ed.
/// </summary>
public sealed class MessageQuery
{
    public static readonly MessageQuery Empty = new();

    public IReadOnlyList<string> From { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Subject { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Body { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Text { get; init; } = Array.Empty<string>();

    public bool? IsUnread { get; init; }
    public bool? IsStarred { get; init; }
    public bool? HasAttachment { get; init; }

    /// <summary>True when the query has no conditions at all (matches everything).</summary>
    public bool IsEmpty =>
        From.Count == 0 && To.Count == 0 && Subject.Count == 0 && Body.Count == 0 &&
        Text.Count == 0 && IsUnread is null && IsStarred is null && HasAttachment is null;

    /// <summary>True when matching needs the message body table (message_bodies).</summary>
    public bool NeedsBody => Body.Count > 0 || Text.Count > 0;

    public static MessageQuery Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Empty;

        var from = new List<string>();
        var to = new List<string>();
        var subject = new List<string>();
        var body = new List<string>();
        var text = new List<string>();
        bool? unread = null;
        bool? starred = null;
        bool? hasAttachment = null;

        foreach (var token in Tokenize(input))
        {
            var colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1)
            {
                text.Add(token);
                continue;
            }

            var key = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..];

            switch (key)
            {
                case "from":
                case "sender":
                    from.Add(value);
                    break;
                case "to":
                case "cc":
                    to.Add(value);
                    break;
                case "subject":
                    subject.Add(value);
                    break;
                case "body":
                    body.Add(value);
                    break;
                case "is":
                    switch (value.ToLowerInvariant())
                    {
                        case "unread":
                        case "new":
                            unread = true;
                            break;
                        case "read":
                            unread = false;
                            break;
                        case "starred":
                            starred = true;
                            break;
                        case "unstarred":
                            starred = false;
                            break;
                        default:
                            // Unknown modifier: treat as free text, never silently drop input.
                            text.Add(token);
                            break;
                    }
                    break;
                case "has":
                    if (value.Equals("attachment", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("attachments", StringComparison.OrdinalIgnoreCase))
                        hasAttachment = true;
                    else
                        text.Add(token);
                    break;
                default:
                    // Not a known field ("http://..." or "12:30"): keep it as text.
                    text.Add(token);
                    break;
            }
        }

        return new MessageQuery
        {
            From = from,
            To = to,
            Subject = subject,
            Body = body,
            Text = text,
            IsUnread = unread,
            IsStarred = starred,
            HasAttachment = hasAttachment,
        };
    }

    /// <summary>
    /// Splits on whitespace, honouring double quotes so
    /// <c>subject:"quarterly report"</c> stays one token.
    /// </summary>
    internal static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>Re-quotes values containing spaces so ToString() parses back to the same query.</summary>
    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    /// <summary>Human-readable form used in the status bar and in logs.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        parts.AddRange(From.Select(f => $"from:{Quote(f)}"));
        parts.AddRange(To.Select(t => $"to:{Quote(t)}"));
        parts.AddRange(Subject.Select(s => $"subject:{Quote(s)}"));
        parts.AddRange(Body.Select(b => $"body:{Quote(b)}"));
        parts.AddRange(Text.Select(Quote));
        if (IsUnread == true) parts.Add("is:unread");
        if (IsUnread == false) parts.Add("is:read");
        if (IsStarred == true) parts.Add("is:starred");
        if (IsStarred == false) parts.Add("is:unstarred");
        if (HasAttachment == true) parts.Add("has:attachment");
        return string.Join(' ', parts);
    }
}
