using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Applies coloured-flag rules to mail: after a sync (new messages) and on
/// demand ("apply rules to this folder now").
/// </summary>
public interface IFlagService
{
    /// <summary>
    /// Colours the given messages according to the enabled rules and returns the
    /// ones that changed. Messages already carrying a manual flag are left alone.
    /// Never throws on a bad rule — the rule is skipped.
    /// </summary>
    Task<int> ApplyRulesAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);

    /// <summary>Re-evaluates every cached message of an account (rules may have changed).</summary>
    Task<int> ApplyRulesToAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Sets or clears the flag on one message (manual choice wins over rules).</summary>
    Task SetFlagAsync(Message message, FlagColor color, CancellationToken ct = default);
}
