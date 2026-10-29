namespace Strider.Core.Domain;

/// <summary>
/// Pure logic for building replies and forwards: subject prefixes,
/// recipient lists and threading headers. No I/O, fully unit-testable.
/// </summary>
public static class ReplyBuilder
{
    private static readonly string[] ReplyPrefixes = { "re:", "ответ:", "aw:", "sv:" };
    private static readonly string[] ForwardPrefixes = { "fwd:", "fw:", "пересл:", "wg:" };

    public static string ReplySubject(string? subject) => Prefixed(subject, "Re: ", ReplyPrefixes);

    public static string ForwardSubject(string? subject) => Prefixed(subject, "Fwd: ", ForwardPrefixes);

    private static string Prefixed(string? subject, string prefix, string[] known)
    {
        var s = (subject ?? string.Empty).Trim();
        return known.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? s : prefix + s;
    }

    /// <summary>Recipients for Reply: the original sender.</summary>
    public static List<string> ReplyTo(Message original) =>
        string.IsNullOrWhiteSpace(original.FromAddress)
            ? new List<string>()
            : new List<string> { Format(original.FromName, original.FromAddress) };

    /// <summary>
    /// Recipients for Reply All. To = sender + original To; Cc = original Cc.
    /// The user's own address and duplicates are removed.
    /// </summary>
    public static (List<string> To, List<string> Cc) ReplyAll(Message original, string selfEmail)
    {
        var self = AddressList.BareAddress(selfEmail);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };

        var to = new List<string>();
        foreach (var a in ReplyTo(original).Concat(AddressList.FromJson(original.ToAddresses)))
        {
            if (seen.Add(AddressList.BareAddress(a))) to.Add(a);
        }

        var cc = new List<string>();
        foreach (var a in AddressList.FromJson(original.CcAddresses))
        {
            if (seen.Add(AddressList.BareAddress(a))) cc.Add(a);
        }

        // Replying to your own sent message: keep its recipients as To.
        if (to.Count == 0 && cc.Count > 0)
        {
            to.AddRange(cc);
            cc.Clear();
        }
        return (to, cc);
    }

    /// <summary>References chain for a reply: original References + original Message-ID.</summary>
    public static List<string> References(Message original)
    {
        var refs = AddressList.FromJson(original.References)
            .Select(StripBrackets)
            .Where(r => r.Length > 0)
            .ToList();
        var id = StripBrackets(original.MessageId);
        if (id.Length > 0 && !refs.Contains(id)) refs.Add(id);
        return refs;
    }

    public static string StripBrackets(string? messageId) =>
        (messageId ?? string.Empty).Trim().TrimStart('<').TrimEnd('>').Trim();

    /// <summary>Plain-text quote block for reply/forward bodies.</summary>
    public static string QuoteHeader(Message original, bool forward)
    {
        var title = forward ? "---------- Forwarded message ----------" : "---------- Original message ----------";
        var date = original.DateUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "";
        return $"{title}\nFrom: {Format(original.FromName, original.FromAddress)}\nDate: {date}\nSubject: {original.Subject}\n";
    }

    public static string QuotePlain(string? text) =>
        string.Join("\n", (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(l => "> " + l));

    private static string Format(string? name, string address) =>
        string.IsNullOrWhiteSpace(name) || name == address ? address : $"\"{name.Replace("\"", "")}\" <{address}>";
}
