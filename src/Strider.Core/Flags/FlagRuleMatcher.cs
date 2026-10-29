using System.Text.RegularExpressions;
using Strider.Core.Domain;

namespace Strider.Core.Flags;

/// <summary>
/// Decides which colour an incoming message should get.
///
/// Rules are evaluated in <see cref="FlagRule.Operator"/> order of priority
/// (lower <see cref="FlagRule.Priority"/> first, ties broken by creation order),
/// and the first matching enabled rule wins. Disabled rules never match.
/// A broken regex is skipped instead of throwing — one bad rule must not stop
/// the rest of the mailbox from being colourised.
/// </summary>
public static class FlagRuleMatcher
{
    /// <summary>Returns the colour the message should carry, or <see cref="FlagColor.None"/>.</summary>
    public static FlagColor Match(Message message, IEnumerable<FlagRule>? rules)
    {
        if (message is null || rules is null) return FlagColor.None;

        foreach (var rule in rules
                     .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Pattern) && r.Color != FlagColor.None)
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.CreatedAt))
        {
            if (Matches(message, rule)) return rule.Color;
        }

        return FlagColor.None;
    }

    /// <summary>True when the rule matches this message.</summary>
    public static bool Matches(Message message, FlagRule rule)
    {
        var haystack = FieldValue(message, rule.Field);
        if (haystack is null) return false;

        var pattern = rule.Pattern?.Trim() ?? string.Empty;
        if (pattern.Length == 0) return false;

        return rule.Operator switch
        {
            FlagRuleOperator.Contains =>
                haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            FlagRuleOperator.Equals =>
                haystack.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            FlagRuleOperator.StartsWith =>
                haystack.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            FlagRuleOperator.Regex => TryRegex(pattern, haystack),
            _ => false,
        };
    }

    /// <summary>
    /// The text a rule looks at. Sender includes the display name, recipient
    /// covers both To and Cc, "any" is sender + subject.
    /// </summary>
    public static string? FieldValue(Message message, FlagRuleField field) => field switch
    {
        FlagRuleField.Sender => Combine(message.FromAddress, message.FromName),
        FlagRuleField.Subject => message.Subject,
        FlagRuleField.Recipient => Combine(message.ToAddresses, message.CcAddresses),
        FlagRuleField.Any => Combine(message.FromAddress, message.FromName, message.Subject),
        _ => null,
    };

    private static string Combine(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static bool TryRegex(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            // Invalid regex: rule simply does not match.
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological pattern must not freeze the sync.
            return false;
        }
    }
}
