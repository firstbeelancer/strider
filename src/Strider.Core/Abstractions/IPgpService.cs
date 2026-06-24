using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// PGP encryption/signing service.
/// </summary>
public interface IPgpService
{
    Task<PgpKey> GenerateKeyPairAsync(string userId, string passphrase, int keySize = 4096, CancellationToken ct = default);
    Task<PgpKey> ImportPublicKeyAsync(string armoredKey, CancellationToken ct = default);
    Task<PgpKey> ImportPrivateKeyAsync(string armoredKey, string passphrase, CancellationToken ct = default);
    Task<string> ExportPublicKeyAsync(PgpKey key, CancellationToken ct = default);
    Task<string> ExportPrivateKeyAsync(PgpKey key, string passphrase, CancellationToken ct = default);

    Task<string> EncryptAsync(string plainText, IReadOnlyList<PgpKey> recipientKeys, CancellationToken ct = default);
    Task<string> DecryptAsync(string encryptedText, PgpKey privateKey, string passphrase, CancellationToken ct = default);
    Task<string> SignAsync(string plainText, PgpKey privateKey, string passphrase, CancellationToken ct = default);
    Task<PgpVerificationResult> VerifyAsync(string signedText, IReadOnlyList<PgpKey> senderKeys, CancellationToken ct = default);
    Task<string> EncryptAndSignAsync(string plainText, IReadOnlyList<PgpKey> recipientKeys, PgpKey senderKey, string passphrase, CancellationToken ct = default);
}

public class PgpVerificationResult
{
    public PgpVerification Status { get; set; }
    public string? SignerUserId { get; set; }
    public DateTime? SignatureTime { get; set; }
}
