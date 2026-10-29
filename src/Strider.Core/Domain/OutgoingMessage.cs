namespace Strider.Core.Domain;

/// <summary>
/// A message the user is about to send. Pure data, no MIME types, so Core
/// stays free of MailKit. The SMTP gateway turns it into a MimeMessage.
/// </summary>
public class OutgoingMessage
{
    public string From { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string? BodyPlain { get; set; }
    public string? BodyHtml { get; set; }

    /// <summary>RFC 5322 Message-ID of the message being replied to (no angle brackets).</summary>
    public string? InReplyTo { get; set; }

    /// <summary>Full References chain, oldest first (no angle brackets).</summary>
    public List<string> References { get; set; } = new();

    public List<Abstractions.AttachmentData> Attachments { get; set; } = new();
}
