using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Mail;

/// <summary>
/// MailKit-based SMTP gateway implementation.
/// </summary>
public class MailKitSmtpGateway : ISmtpGateway, IDisposable
{
    private SmtpClient? _client;
    private readonly object _lock = new();

    public async Task ConnectAsync(Account account, CancellationToken ct = default)
    {
        var client = new SmtpClient();

        await client.ConnectAsync(account.SmtpHost, account.SmtpPort, account.SmtpUseSsl, ct);

        // Authenticate
        if (!string.IsNullOrEmpty(account.OAuth2TokenRef))
        {
            await client.AuthenticateAsync(
                new SaslMechanismOAuth2(account.Email, account.OAuth2TokenRef), ct);
        }
        else
        {
            // Password from SyncState (temporary; should come from keychain)
            await client.AuthenticateAsync(account.Email, account.SyncState ?? "", ct);
        }

        lock (_lock)
        {
            _client?.Dispose();
            _client = client;
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_client?.IsConnected == true)
            {
                return _client.DisconnectAsync(true, ct);
            }
        }
        return Task.CompletedTask;
    }

    public async Task SendAsync(
        Account account,
        string from,
        IReadOnlyList<string> to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc,
        string subject,
        string bodyHtml,
        string? bodyPlain,
        IReadOnlyList<AttachmentData>? attachments,
        CancellationToken ct = default)
    {
        var client = GetClient();

        var message = BuildMimeMessage(from, to, cc, bcc, subject, bodyHtml, bodyPlain, attachments);

        await client.SendAsync(message, ct);
    }

    public async Task SendEncryptedAsync(
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
        CancellationToken ct = default)
    {
        // PGP encryption will be implemented in PgpService
        // For now, fall back to regular send
        await SendAsync(account, from, to, null, null, subject, bodyHtml, bodyPlain, attachments, ct);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
        }
    }

    // === Helpers ===

    private SmtpClient GetClient()
    {
        lock (_lock)
        {
            if (_client?.IsConnected != true)
                throw new InvalidOperationException("SMTP client is not connected");
            return _client;
        }
    }

    private static MimeMessage BuildMimeMessage(
        string from,
        IReadOnlyList<string> to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc,
        string subject,
        string bodyHtml,
        string? bodyPlain,
        IReadOnlyList<AttachmentData>? attachments)
    {
        var message = new MimeMessage();

        // From
        message.From.Add(MailboxAddress.Parse(from));

        // To
        foreach (var addr in to)
        {
            message.To.Add(MailboxAddress.Parse(addr));
        }

        // Cc
        if (cc != null)
        {
            foreach (var addr in cc)
            {
                message.Cc.Add(MailboxAddress.Parse(addr));
            }
        }

        // Bcc
        if (bcc != null)
        {
            foreach (var addr in bcc)
            {
                message.Bcc.Add(MailboxAddress.Parse(addr));
            }
        }

        message.Subject = subject;

        // Body
        var bodyBuilder = new BodyBuilder();

        if (!string.IsNullOrEmpty(bodyPlain))
        {
            bodyBuilder.TextBody = bodyPlain;
        }

        if (!string.IsNullOrEmpty(bodyHtml))
        {
            bodyBuilder.HtmlBody = bodyHtml;
        }

        // Attachments
        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                if (!string.IsNullOrEmpty(att.ContentId))
                {
                    // Inline attachment
                    var linked = bodyBuilder.LinkedResources.Add(att.Filename, att.Content);
                    linked.ContentId = att.ContentId;
                }
                else
                {
                    bodyBuilder.Attachments.Add(att.Filename, att.Content,
                        ContentType.Parse(att.ContentType));
                }
            }
        }

        message.Body = bodyBuilder.ToMessageBody();

        return message;
    }
}
