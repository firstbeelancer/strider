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
    public static MimeMessage Build(OutgoingMessage m, bool requireRecipients = true)
    {
        if (requireRecipients && m.To.Count == 0 && m.Cc.Count == 0 && m.Bcc.Count == 0)
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
        if (!string.IsNullOrEmpty(m.BodyHtml)) builder.HtmlBody = EmbedDataImages(m.BodyHtml, builder);
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

    private static readonly System.Text.RegularExpressions.Regex DataImage = new(
        "src\\s*=\\s*[\"']data:(?<type>image/[a-zA-Z0-9.+-]+);base64,(?<data>[A-Za-z0-9+/=\\s]+)[\"']",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Turns inline data: images (a signature logo) into cid: parts: most mail clients,
    /// Gmail and Outlook included, refuse to show data: URLs.
    /// </summary>
    private static string EmbedDataImages(string html, BodyBuilder builder) =>
        DataImage.Replace(html, match =>
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(match.Groups["data"].Value);
            }
            catch (FormatException)
            {
                return match.Value;
            }

            var contentType = ContentType.Parse(match.Groups["type"].Value);
            var linked = builder.LinkedResources.Add($"image.{contentType.MediaSubtype}", bytes, contentType);
            linked.ContentId = MimeUtils.GenerateMessageId();
            return $"src=\"cid:{linked.ContentId}\"";
        });

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
