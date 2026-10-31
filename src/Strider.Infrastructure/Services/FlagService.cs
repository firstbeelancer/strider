using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Core.Flags;

namespace Strider.Infrastructure.Services;

/// <summary>
/// Colours mail with the user's flag rules.
///
/// Rules that keep this predictable:
///  • a flag the user set by hand is never overwritten by a rule;
///  • a message that no longer matches any rule loses its *automatic* colour
///    (manual colours stay);
///  • a broken rule is skipped and logged, never fatal.
/// </summary>
public sealed class FlagService : IFlagService
{
    private readonly IFlagRuleStore _rules;
    private readonly IMessageStore _messages;
    private readonly IAccountStore _accounts;

    public FlagService(IFlagRuleStore rules, IMessageStore messages, IAccountStore accounts)
    {
        _rules = rules;
        _messages = messages;
        _accounts = accounts;
    }

    public async Task<int> ApplyRulesAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return 0;

        // No early exit on "no rules": removing the last rule must clear its colours.
        var rules = await _rules.GetEnabledAsync(ct);

        var changed = 0;
        foreach (var message in messages)
        {
            if (message.FlagColor != FlagColor.None && !message.FlagColorIsAutomatic) continue;

            var color = FlagRuleMatcher.Match(message, rules);
            if (color == message.FlagColor) continue;

            message.FlagColor = color;
            message.FlagColorIsAutomatic = color != FlagColor.None;
            await _messages.UpdateMessageAsync(message, ct);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// Re-evaluates the cached mail of an account (the user just edited a rule).
    /// The scan is capped per folder: this runs after a UI action, and walking
    /// tens of thousands of rows would be worse than leaving old mail uncoloured.
    /// </summary>
    public async Task<int> ApplyRulesToAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        const int limitPerFolder = 500;

        var folders = await _accounts.GetFoldersAsync(accountId, ct);
        var changed = 0;

        foreach (var folder in folders)
        {
            var messages = await _messages.GetMessagesAsync(folder.Id, limitPerFolder, 0, ct);
            changed += await ApplyRulesAsync(messages, ct);
        }

        return changed;
    }

    public async Task SetFlagAsync(Message message, FlagColor color, CancellationToken ct = default)
    {
        message.FlagColor = color;
        message.FlagColorIsAutomatic = false; // an explicit choice beats the rules
        await _messages.UpdateMessageAsync(message, ct);
    }
}
