using System.Text.Json;

namespace Strider.Core.Domain;

/// <summary>
/// Helpers for the two address formats Strider deals with:
/// user input ("a@x.com, Bob &lt;b@y.com&gt;; c@z") and the JSON arrays
/// stored in messages.to_addresses / cc_addresses.
/// </summary>
public static class AddressList
{
    /// <summary>Splits user input on comma/semicolon, ignoring separators inside quotes.</summary>
    public static List<string> ParseInput(string? input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return result;

        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in input)
        {
            if (ch == '"') inQuotes = !inQuotes;
            if (!inQuotes && (ch == ',' || ch == ';'))
            {
                Flush();
                continue;
            }
            current.Append(ch);
        }
        Flush();
        return result;

        void Flush()
        {
            var s = current.ToString().Trim();
            if (s.Length > 0) result.Add(s);
            current.Clear();
        }
    }

    /// <summary>Reads a stored JSON array; tolerates null, empty, or a plain non-JSON string.</summary>
    public static List<string> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(trimmed)?
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            }
            catch (JsonException)
            {
                // fall through to plain-text parsing
            }
        }
        return ParseInput(trimmed);
    }

    /// <summary>Joins addresses for a compose field.</summary>
    public static string ToInput(IEnumerable<string> addresses) => string.Join(", ", addresses);

    /// <summary>
    /// Extracts the bare e-mail from "Name &lt;a@b&gt;" or "a@b". Lower-cased for comparison.
    /// </summary>
    public static string BareAddress(string address)
    {
        var lt = address.LastIndexOf('<');
        var gt = address.LastIndexOf('>');
        var bare = lt >= 0 && gt > lt ? address.Substring(lt + 1, gt - lt - 1) : address;
        return bare.Trim().Trim('"').ToLowerInvariant();
    }
}
