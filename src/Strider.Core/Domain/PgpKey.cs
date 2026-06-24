namespace Strider.Core.Domain;

/// <summary>
/// PGP key (public or keypair).
/// </summary>
public class PgpKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string KeyId { get; set; } = string.Empty; // last 16 hex chars
    public string Fingerprint { get; set; } = string.Empty;
    public string PublicKeyArmored { get; set; } = string.Empty;
    public string? PrivateKeyArmored { get; set; } // null for imported public-only
    public string? UserId { get; set; } // "Name <email>"
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
