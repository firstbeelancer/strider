using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>Persistence for coloured-flag rules.</summary>
public interface IFlagRuleStore
{
    /// <summary>All rules, enabled first, ordered by priority.</summary>
    Task<IReadOnlyList<FlagRule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Enabled rules only — what the matcher needs.</summary>
    Task<IReadOnlyList<FlagRule>> GetEnabledAsync(CancellationToken ct = default);

    Task SaveAsync(FlagRule rule, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Rewrites the priority of all rules according to the given order.</summary>
    Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default);
}
