namespace Strider.Core.Domain;

/// <summary>
/// Email account configuration.
/// </summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // IMAP settings
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;

    // SMTP settings
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;

    // OAuth2 reference (token stored in keychain)
    public string? OAuth2TokenRef { get; set; }

    // Sync state (JSON: last UIDs per folder)
    public string? SyncState { get; set; }

    // Default signature
    public Guid? DefaultSignatureId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
