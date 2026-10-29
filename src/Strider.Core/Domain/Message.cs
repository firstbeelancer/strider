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

    /// <summary>
    /// Coloured flag shown in the message list (colour bar + coloured subject).
    /// Set manually by the user or automatically by a <see cref="FlagRule"/>.
    /// </summary>
    public FlagColor FlagColor { get; set; } = FlagColor.None;

    /// <summary>True when the colour came from a rule rather than a manual choice.</summary>
    public bool FlagColorIsAutomatic { get; set; }

    /// <summary>True when the row should show a flag bar and a coloured subject.</summary>
    public bool HasFlagColor => FlagColor != Domain.FlagColor.None;

    /// <summary>Hex of the flag colour, ready for the message list.</summary>
    public string FlagColorHex => Domain.FlagColors.ToHex(FlagColor);

    /// <summary>Name of the flag colour for menus and tooltips.</summary>
    public string FlagColorName => Domain.FlagColors.ToDisplayName(FlagColor);

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
