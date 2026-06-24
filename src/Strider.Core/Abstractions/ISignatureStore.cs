using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Signature store.
/// </summary>
public interface ISignatureStore
{
    Task SaveSignatureAsync(Signature signature, CancellationToken ct = default);
    Task<Signature?> GetSignatureAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Signature>> GetSignaturesAsync(Guid accountId, CancellationToken ct = default);
    Task UpdateSignatureAsync(Signature signature, CancellationToken ct = default);
    Task DeleteSignatureAsync(Guid id, CancellationToken ct = default);
}
