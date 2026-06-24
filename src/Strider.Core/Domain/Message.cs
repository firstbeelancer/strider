namespace Strider.Core.Domain;

/// <summary>
/// Email message metadata.
/// </summary>
public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid FolderId { get; set; }
    public int MessageUid { get; set; } // IMAP UID
    public string? MessageId { get; set; } // RFC 5322 Message-ID
    public string? InReplyTo { get; set; }
    public string? References { get; set; } // JSON array

    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public string ToAddresses { get; set; } = "[]"; // JSON
    public string? CcAddresses { get; set; } // JSON

    public string Subject { get; set; } = string.Empty;
    public DateTime? DateUtc { get; set; }
    public long Size { get; set; }

    public bool HasAttachments { get; set; }
    public bool IsRead { get; set; }
    public bool IsStarred { get; set; }
    public bool IsFlagged { get; set; }

    public string? ThreadId { get; set; }

    // AI enrichment
    public string? AiCategory { get; set; } // Work/Personal/Newsletter/etc
    public string? AiSummary { get; set; }

    // PGP status
    public PgpStatus PgpStatus { get; set; } = PgpStatus.None;
    public PgpVerification PgpVerified { get; set; } = PgpVerification.Unknown;

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public enum PgpStatus
{
    None,
    Signed,
    Encrypted,
    SignedAndEncrypted
}

public enum PgpVerification
{
    Unknown,
    Valid,
    Invalid,
    NoKey
}
