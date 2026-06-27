using System.Collections.Concurrent;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Mail;

/// <summary>
/// MailKit-based IMAP gateway implementation.
/// Per-account instance — created via <see cref="IImapGatewayFactory"/>.
/// Credentials are read from <see cref="IKeychainService"/> at auth time,
/// never from the Account entity (which only stores keychain references).
/// </summary>
public class MailKitImapGateway : IImapGateway, IDisposable
{
    private readonly IKeychainService _keychain;
    private readonly IAccountStore _accountStore;
    private readonly Guid _accountId;
    private ImapClient? _client;
    private readonly object _lock = new();

    /// <summary>
    /// Constructor used by <see cref="MailKitImapGatewayFactory"/>.
    /// </summary>
    public MailKitImapGateway(IKeychainService keychain, IAccountStore accountStore, Guid accountId)
    {
        _keychain = keychain;
        _accountStore = accountStore;
        _accountId = accountId;
    }

    public async Task ConnectAsync(Account account, CancellationToken ct = default)
    {
        var client = new ImapClient();

        await client.ConnectAsync(account.ImapHost, account.ImapPort, account.ImapUseSsl, ct);

        // Authenticate: OAuth2 or plain password — credentials fetched from keychain
        if (!string.IsNullOrEmpty(account.OAuth2TokenRef))
        {
            // account.OAuth2TokenRef is the keychain key (not the token itself)
            var oauthToken = await _keychain.GetSecretAsync(account.OAuth2TokenRef, ct)
                ?? throw new InvalidOperationException(
                    $"OAuth2 token not found in keychain under key '{account.OAuth2TokenRef}'");
            await client.AuthenticateAsync(
                new SaslMechanismOAuth2(account.Email, oauthToken), ct);
        }
        else
        {
            // Plain password: stored in keychain under "strider:{accountId}:password"
            var passwordKey = $"strider:{account.Id}:password";
            var password = await _keychain.GetSecretAsync(passwordKey, ct)
                ?? throw new InvalidOperationException(
                    $"Password not found in keychain under key '{passwordKey}'");
            await client.AuthenticateAsync(account.Email, password, ct);
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

    public Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_client?.IsConnected == true && _client?.IsAuthenticated == true);
        }
    }

    public async Task<IReadOnlyList<Folder>> GetFoldersAsync(Account account, CancellationToken ct = default)
    {
        var client = GetClient();
        var folders = new List<Folder>();

        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        var allFolders = await personal.GetSubfoldersAsync(false, ct);

        foreach (var folder in allFolders)
        {
            folders.Add(MapFolder(folder, account.Id));
            // Get subfolders recursively
            await GetSubfoldersRecursive(folder, account.Id, folders, ct);
        }

        return folders;
    }

    private async Task GetSubfoldersRecursive(IMailFolder folder, Guid accountId, List<Folder> result, CancellationToken ct)
    {
        try
        {
            var subfolders = await folder.GetSubfoldersAsync(false, ct);
            foreach (var sub in subfolders)
            {
                result.Add(MapFolder(sub, accountId));
                await GetSubfoldersRecursive(sub, accountId, result, ct);
            }
        }
        catch (CommandException)
        {
            // Some folders don't support subfolders
        }
    }

    public async Task<IReadOnlyList<Message>> FetchMessagesAsync(
        Account account, string folderName, int fromUid, int toUid, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var messages = new List<Message>();

        // Fetch messages by UID range
        var uids = await folder.SearchAsync(
            SearchQuery.Uids(new UniqueIdRange(new UniqueId((uint)fromUid), new UniqueId((uint)toUid))), ct);

        if (uids.Count == 0) return messages;

        // Fetch headers only (no body)
        var summaries = await folder.FetchAsync(uids.ToArray(),
            MessageSummaryItems.UniqueId |
            MessageSummaryItems.Envelope |
            MessageSummaryItems.Flags |
            MessageSummaryItems.Size |
            MessageSummaryItems.BodyStructure, ct);

        foreach (var summary in summaries)
        {
            var envelope = summary.Envelope;
            var toList = envelope?.To?.Select(a => a.ToString()).ToList() ?? new List<string>();
            var ccList = envelope?.Cc?.Select(a => a.ToString()).ToList();

            messages.Add(new Message
            {
                AccountId = account.Id,
                FolderId = Guid.Empty,
                MessageUid = (int)summary.UniqueId.Id,
                MessageId = envelope?.MessageId,
                InReplyTo = envelope?.InReplyTo,
                References = null, // Will be populated from headers if needed
                FromAddress = envelope?.From?.Mailboxes?.FirstOrDefault()?.Address ?? "",
                FromName = envelope?.From?.Mailboxes?.FirstOrDefault()?.Name,
                ToAddresses = System.Text.Json.JsonSerializer.Serialize(toList),
                CcAddresses = ccList != null ? System.Text.Json.JsonSerializer.Serialize(ccList) : null,
                Subject = envelope?.Subject ?? "",
                DateUtc = envelope?.Date?.UtcDateTime,
                Size = (long)(summary.Size ?? 0),
                HasAttachments = summary.Body != null && HasAttachments(summary.Body),
                IsRead = summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                IsStarred = summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                IsFlagged = summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                FetchedAt = DateTime.UtcNow,
            });
        }

        return messages;
    }

    public async Task<MessageBody> FetchBodyAsync(Account account, string folderName, int messageUid, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var uid = new UniqueId((uint)messageUid);
        var message = await folder.GetMessageAsync(uid, ct);

        var body = new MessageBody
        {
            TextPlain = message.TextBody,
            TextHtml = message.HtmlBody,
        };

        return body;
    }

    public async Task<IReadOnlyList<Attachment>> FetchAttachmentsAsync(
        Account account, string folderName, int messageUid, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var uid = new UniqueId((uint)messageUid);
        var message = await folder.GetMessageAsync(uid, ct);

        var attachments = new List<Attachment>();

        foreach (var part in message.Attachments)
        {
            if (part is MimePart mimeAtt)
            {
                attachments.Add(new Attachment
                {
                    Filename = mimeAtt.FileName,
                    ContentType = mimeAtt.ContentType?.MimeType,
                    Size = mimeAtt.ContentDisposition?.Size ?? 0,
                    ContentId = mimeAtt.ContentId,
                });
            }
        }

        return attachments;
    }

    public async Task<Stream> DownloadAttachmentAsync(
        Account account, string folderName, int messageUid, string contentId, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var uid = new UniqueId((uint)messageUid);
        var message = await folder.GetMessageAsync(uid, ct);

        foreach (var part in message.BodyParts)
        {
            if (part is MimePart mp && mp.ContentId == contentId && mp.Content != null)
            {
                var stream = new MemoryStream();
                await mp.Content.DecodeToAsync(stream, ct);
                stream.Position = 0;
                return stream;
            }
        }

        throw new InvalidOperationException($"Attachment with ContentId '{contentId}' not found");
    }

    public async Task MarkAsReadAsync(Account account, string folderName, int messageUid, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        await folder.AddFlagsAsync(new UniqueId((uint)messageUid), MessageFlags.Seen, true, ct);
    }

    public async Task MarkAsUnreadAsync(Account account, string folderName, int messageUid, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        await folder.RemoveFlagsAsync(new UniqueId((uint)messageUid), MessageFlags.Seen, true, ct);
    }

    public async Task MoveMessageAsync(Account account, string fromFolder, int messageUid, string toFolder, CancellationToken ct = default)
    {
        var client = GetClient();
        var source = await client.GetFolderAsync(fromFolder, ct);
        await source.OpenAsync(FolderAccess.ReadWrite, ct);

        var dest = await client.GetFolderAsync(toFolder, ct);
        await source.MoveToAsync(new UniqueId((uint)messageUid), dest, ct);
    }

    public async Task DeleteMessageAsync(Account account, string folderName, int messageUid, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        await folder.AddFlagsAsync(new UniqueId((uint)messageUid), MessageFlags.Deleted, true, ct);
        await folder.ExpungeAsync(ct);
    }

    public async Task AddFlagAsync(Account account, string folderName, int messageUid, string flag, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var flags = flag.ToLowerInvariant() switch
        {
            "flagged" => MessageFlags.Flagged,
            "answered" => MessageFlags.Answered,
            "draft" => MessageFlags.Draft,
            "deleted" => MessageFlags.Deleted,
            _ => MessageFlags.None,
        };

        if (flags != MessageFlags.None)
        {
            await folder.AddFlagsAsync(new UniqueId((uint)messageUid), flags, true, ct);
        }
    }

    public async Task RemoveFlagAsync(Account account, string folderName, int messageUid, string flag, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var flags = flag.ToLowerInvariant() switch
        {
            "flagged" => MessageFlags.Flagged,
            "answered" => MessageFlags.Answered,
            "draft" => MessageFlags.Draft,
            "deleted" => MessageFlags.Deleted,
            _ => MessageFlags.None,
        };

        if (flags != MessageFlags.None)
        {
            await folder.RemoveFlagsAsync(new UniqueId((uint)messageUid), flags, true, ct);
        }
    }

    public async Task IdleAsync(Account account, string folderName, Action onNewMessage, CancellationToken ct = default)
    {
        var client = GetClient();
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        // Set up IDLE
        client.Inbox.CountChanged += (s, e) => onNewMessage();

        while (!ct.IsCancellationRequested)
        {
            if (client.Capabilities.HasFlag(ImapCapabilities.Idle))
            {
                // Use IDLE
                await client.IdleAsync(ct);
            }
            else
            {
                // Fallback: poll every 30 seconds
                await Task.Delay(30000, ct);
                await client.NoOpAsync(ct);
            }
        }
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

    private ImapClient GetClient()
    {
        lock (_lock)
        {
            if (_client?.IsConnected != true || _client?.IsAuthenticated != true)
                throw new InvalidOperationException("IMAP client is not connected");
            return _client;
        }
    }

    private static bool HasAttachments(BodyPart body)
    {
        if (body is BodyPartMultipart multipart)
        {
            foreach (var part in multipart.BodyParts)
            {
                if (part is BodyPartBasic basic && basic.IsAttachment)
                    return true;
                if (part is BodyPartMultipart nested && HasAttachments(nested))
                    return true;
            }
        }
        return false;
    }

    private static Folder MapFolder(IMailFolder mailFolder, Guid accountId)
    {
        var type = mailFolder.Name.ToLowerInvariant() switch
        {
            "inbox" => FolderType.Inbox,
            "sent" or "sent mail" or "sent items" or "отправленные" => FolderType.Sent,
            "drafts" or "draft" or "черновики" => FolderType.Drafts,
            "trash" or "deleted" or "корзина" => FolderType.Trash,
            "archive" or "archives" or "архив" => FolderType.Archive,
            "spam" or "junk" or "spam folder" or "спам" => FolderType.Spam,
            _ => FolderType.Custom,
        };

        return new Folder
        {
            AccountId = accountId,
            RemoteName = mailFolder.FullName,
            Type = type,
        };
    }
}
