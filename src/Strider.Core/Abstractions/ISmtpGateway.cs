using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// SMTP email gateway.
/// </summary>
public interface ISmtpGateway
{
    Task ConnectAsync(Account account, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    Task SendAsync(
        Account account,
        string from,
        IReadOnlyList<string> to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc,
        string subject,
        string bodyHtml,
        string? bodyPlain,
        IReadOnlyList<AttachmentData>? attachments,
        CancellationToken ct = default);

    /// <summary>
    /// Send with PGP encryption/signing.
    /// </summary>
    Task SendEncryptedAsync(
        Account account,
        string from,
        IReadOnlyList<string> to,
        string subject,
        string bodyHtml,
        string? bodyPlain,
        IReadOnlyList<string>? recipientPublicKeys,
        string? senderPrivateKey,
        string? senderPassphrase,
        IReadOnlyList<AttachmentData>? attachments,
        CancellationToken ct = default);
}

/// <summary>
/// Attachment data for sending.
/// </summary>
public class AttachmentData
{
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string? ContentId { get; set; } // for inline
}
