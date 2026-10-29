using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Persistence for AI provider configuration (table <c>ai_settings</c>).
/// The API key itself never lives here — only the keychain reference
/// (<see cref="AiSettings.ApiKeyRef"/>), see <see cref="KeychainKeys.AiApiKey"/>.
/// </summary>
public interface IAiSettingsStore
{
    Task<IReadOnlyList<AiSettings>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the provider marked <c>is_default</c>, or the newest one, or null.</summary>
    Task<AiSettings?> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>Insert or replace by <see cref="AiSettings.Id"/>. Setting IsDefault clears the flag on other rows.</summary>
    Task SaveAsync(AiSettings settings, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
