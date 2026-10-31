using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Strider.Core.Domain;

/// <summary>
/// Email message metadata.
/// </summary>
public class Message : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isStarred;
    private FlagColor _flagColor = FlagColor.None;
    private string? _aiCategory;

    /// <summary>Lets the list repaint a row without being rebuilt (which would drop the selection).</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
    public bool IsRead
    {
        get => _isRead;
        set { if (_isRead == value) return; _isRead = value; Changed(); }
    }

    public bool IsStarred
    {
        get => _isStarred;
        set { if (_isStarred == value) return; _isStarred = value; Changed(); }
    }

    public bool IsFlagged { get; set; }

    public string? ThreadId { get; set; }

    /// <summary>
    /// Coloured flag shown in the message list (colour bar + coloured subject).
    /// Set manually by the user or automatically by a <see cref="FlagRule"/>.
    /// </summary>
    public FlagColor FlagColor
    {
        get => _flagColor;
        set
        {
            if (_flagColor == value) return;
            _flagColor = value;
            Changed();
            Changed(nameof(HasFlagColor));
            Changed(nameof(FlagColorHex));
            Changed(nameof(FlagColorName));
        }
    }

    /// <summary>True when the colour came from a rule rather than a manual choice.</summary>
    public bool FlagColorIsAutomatic { get; set; }

    /// <summary>True when the row should show a flag bar and a coloured subject.</summary>
    public bool HasFlagColor => FlagColor != Domain.FlagColor.None;

    /// <summary>Hex of the flag colour, ready for the message list.</summary>
    public string FlagColorHex => Domain.FlagColors.ToHex(FlagColor);

    /// <summary>Name of the flag colour for menus and tooltips.</summary>
    public string FlagColorName => Domain.FlagColors.ToDisplayName(FlagColor);

    // AI enrichment
    public string? AiCategory // Work/Personal/Newsletter/etc
    {
        get => _aiCategory;
        set { if (_aiCategory == value) return; _aiCategory = value; Changed(); }
    }

    public string? AiSummary { get; set; }

    // PGP status
    public PgpStatus PgpStatus { get; set; } = PgpStatus.None;
    public PgpVerification PgpVerified { get; set; } = PgpVerification.Unknown;

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Date for the message list: in the user's time zone, as short as it can be.</summary>
    public string ListDate => FormatListDate(DateUtc, DateTime.Now);

    /// <summary>Full local date and time for the reader header.</summary>
    public string FullDate => DateUtc is { } d
        ? ToUtc(d).ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>Today: time. This year: day and month. Older: the full date.</summary>
    public static string FormatListDate(DateTime? utc, DateTime nowLocal)
    {
        if (utc is not { } value) return string.Empty;
        var local = ToUtc(value).ToLocalTime();
        if (local.Date == nowLocal.Date) return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        return local.Year == nowLocal.Year
            ? local.ToString("dd MMM", CultureInfo.CurrentCulture)
            : local.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value;
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
