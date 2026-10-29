using MimeKit;
using MimeKit.Utils;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Mail;

/// <summary>
/// Builds a MimeMessage from Core's <see cref="OutgoingMessage"/>.
/// Separate from the SMTP gateway so it can be unit-tested without a server.
/// </summary>
public static class MimeMessageFactory
{
    public static MimeMessage Build(OutgoingMessage m)
    {
        if (m.To.Count == 0 && m.Cc.Count == 0 && m.Bcc.Count == 0)
            throw new ArgumentException("At least one recipient is required", nameof(m));

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(m.FromName ?? string.Empty, m.From));
        AddAll(msg.To, m.To);
        AddAll(msg.Cc, m.Cc);
        AddAll(msg.Bcc, m.Bcc);
        msg.Subject = m.Subject ?? string.Empty;
        msg.Date = DateTimeOffset.Now;

        var domain = m.From.Contains('@') ? m.From[(m.From.IndexOf('@') + 1)..] : "localhost";
        msg.MessageId = MimeUtils.GenerateMessageId(domain);

        if (!string.IsNullOrWhiteSpace(m.InReplyTo))
            msg.InReplyTo = ReplyBuilder.StripBrackets(m.InReplyTo);
        foreach (var r in m.References.Select(ReplyBuilder.StripBrackets).Where(r => r.Length > 0))
            msg.References.Add(r);

        var builder = new BodyBuilder();
        if (!string.IsNullOrEmpty(m.BodyPlain)) builder.TextBody = m.BodyPlain;
        if (!string.IsNullOrEmpty(m.BodyHtml)) builder.HtmlBody = m.BodyHtml;
        if (builder.TextBody == null && builder.HtmlBody == null) builder.TextBody = string.Empty;

        foreach (var att in m.Attachments)
        {
            if (!string.IsNullOrEmpty(att.ContentId))
            {
                var linked = builder.LinkedResources.Add(att.Filename, att.Content);
                linked.ContentId = att.ContentId;
            }
            else
            {
                builder.Attachments.Add(att.Filename, att.Content, ContentType.Parse(att.ContentType));
            }
        }

        msg.Body = builder.ToMessageBody();
        return msg;
    }

    public static byte[] ToBytes(MimeMessage message)
    {
        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return ms.ToArray();
    }

    private static void AddAll(InternetAddressList list, IEnumerable<string> addresses)
    {
        foreach (var a in addresses)
        {
            if (!InternetAddress.TryParse(a, out var parsed))
                throw new FormatException($"Invalid e-mail address: {a}");
            list.Add(parsed);
        }
    }
}
