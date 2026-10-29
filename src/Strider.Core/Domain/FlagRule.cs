namespace Strider.Core.Domain;

/// <summary>Which part of a message a rule looks at.</summary>
public enum FlagRuleField
{
    Sender = 0,
    Subject,
    Recipient,
    Any,
}

/// <summary>How the pattern is compared.</summary>
public enum FlagRuleOperator
{
    Contains = 0,
    Equals,
    StartsWith,
    Regex,
}

/// <summary>
/// A rule that colours incoming mail automatically: "everything from this
/// sender gets a blue flag", "subject contains INVOICE gets a red one".
/// Rules are evaluated by <see cref="Flags.FlagRuleMatcher"/> in priority order;
/// the first match wins.
/// </summary>
public class FlagRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public FlagRuleField Field { get; set; } = FlagRuleField.Sender;
    public FlagRuleOperator Operator { get; set; } = FlagRuleOperator.Contains;
    public string Pattern { get; set; } = string.Empty;
    public FlagColor Color { get; set; } = FlagColor.Blue;

    /// <summary>Lower value = evaluated earlier.</summary>
    public int Priority { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string DisplaySummary =>
        $"{(string.IsNullOrWhiteSpace(Name) ? Field.ToString() : Name)}: " +
        $"{Field.ToString().ToLowerInvariant()} {OperatorLabel(Operator)} \"{Pattern}\" -> {Color}";

    public static string OperatorLabel(FlagRuleOperator op) => op switch
    {
        FlagRuleOperator.Contains => "contains",
        FlagRuleOperator.Equals => "is",
        FlagRuleOperator.StartsWith => "starts with",
        FlagRuleOperator.Regex => "matches regex",
        _ => op.ToString(),
    };
}
