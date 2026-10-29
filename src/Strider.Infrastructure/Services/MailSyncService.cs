using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;

namespace Strider.Infrastructure.Services;

/// <summary>
/// Downloads mail from IMAP into the local SQLite cache.
/// v0.1 scope: new messages only (UID &gt; last synced). Flag changes and
/// deletions made on other devices are not reconciled yet (see Техдолг).
/// </summary>
public sealed class MailSyncService : IMailSyncService
{
    /// <summary>How many newest headers to pull on the very first sync of a folder.</summary>
    public const int InitialSyncLimit = 300;
    public const int IncrementalSyncLimit = 2000;

    private readonly IAccountStore _accounts;
    private readonly IMessageStore _messages;
    private readonly ImapSessionRunner _imap;

    public MailSyncService(IAccountStore accounts, IMessageStore messages, ImapSessionRunner imap)
    {
        _accounts = accounts;
        _messages = messages;
        _imap = imap;
    }

    public async Task<int> SyncAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(accountId, ct);

        return await _imap.RunAsync(account, async gw =>
        {
            var remote = await gw.GetFoldersAsync(account, ct);
            var local = (await _accounts.GetFoldersAsync(account.Id, ct)).ToList();
            var merged = MergeFolders(account.Id, local, remote);
            if (merged.Count > 0) await _accounts.SaveFoldersAsync(merged, ct);
            local.AddRange(merged);

            var inbox = local.FirstOrDefault(f => f.Type == FolderType.Inbox)
                        ?? local.FirstOrDefault(f => f.RemoteName.Equals("INBOX", StringComparison.OrdinalIgnoreCase));
            return inbox == null ? 0 : await SyncFolderCoreAsync(account, inbox, gw, ct);
        }, ct);
    }

    public async Task<int> SyncFolderAsync(Guid accountId, Guid folderId, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(accountId, ct);
        var folder = await _accounts.GetFolderAsync(folderId, ct)
                     ?? throw new InvalidOperationException("Folder not found");
        return await _imap.RunAsync(account, gw => SyncFolderCoreAsync(account, folder, gw, ct), ct);
    }

    public async Task<MessageBody?> EnsureBodyAsync(Message message, CancellationToken ct = default)
    {
        var cached = await _messages.GetBodyAsync(message.Id, ct);
        if (cached != null) return cached;

        var account = await _accounts.GetAccountAsync(message.AccountId, ct);
        var folder = await _accounts.GetFolderAsync(message.FolderId, ct);
        if (account == null || folder == null) return null;

        var body = await _imap.RunAsync(account,
            gw => gw.FetchBodyAsync(account, folder.RemoteName, message.MessageUid, ct), ct);
        body.MessageId = message.Id;
        await _messages.SaveBodyAsync(body, ct);
        return body;
    }

    public async Task<IReadOnlyList<Attachment>> EnsureAttachmentsAsync(Message message, CancellationToken ct = default)
    {
        var cached = await _messages.GetAttachmentsAsync(message.Id, ct);
        if (cached.Count > 0 || !message.HasAttachments) return cached;

        var (account, folder) = await LoadAsync(message, ct);
        if (account == null || folder == null) return cached;

        var remote = await _imap.RunAsync(account,
            gw => gw.FetchAttachmentsAsync(account, folder.RemoteName, message.MessageUid, ct), ct);
        foreach (var a in remote)
        {
            a.Id = Guid.NewGuid();
            a.MessageId = message.Id;
            a.Filename ??= "attachment";
            await _messages.SaveAttachmentAsync(a, ct);
        }
        return remote;
    }

    public async Task<byte[]> DownloadAttachmentAsync(Message message, Attachment attachment, CancellationToken ct = default)
    {
        var (account, folder) = await LoadAsync(message, ct);
        if (account == null || folder == null) throw new InvalidOperationException("Message folder not found");
        var key = !string.IsNullOrEmpty(attachment.ContentId) ? attachment.ContentId! : attachment.Filename ?? "";

        return await _imap.RunAsync(account, async gw =>
        {
            await using var stream = await gw.DownloadAttachmentAsync(account, folder.RemoteName, message.MessageUid, key, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }, ct);
    }

    private async Task<(Account?, Folder?)> LoadAsync(Message message, CancellationToken ct) =>
        (await _accounts.GetAccountAsync(message.AccountId, ct), await _accounts.GetFolderAsync(message.FolderId, ct));

    public async Task MarkAsReadOnServerAsync(Message message, CancellationToken ct = default)
    {
        var account = await _accounts.GetAccountAsync(message.AccountId, ct);
        var folder = await _accounts.GetFolderAsync(message.FolderId, ct);
        if (account == null || folder == null) return;
        await _imap.RunAsync(account, gw => gw.MarkAsReadAsync(account, folder.RemoteName, message.MessageUid, ct), ct);
    }

    /// <summary>
    /// Returns remote folders that are not yet stored locally (matched by RemoteName),
    /// with ids/account set and type classified. Pure, unit-tested.
    /// </summary>
    public static List<Folder> MergeFolders(Guid accountId, IReadOnlyCollection<Folder> local, IEnumerable<Folder> remote)
    {
        var known = new HashSet<string>(local.Select(f => f.RemoteName), StringComparer.Ordinal);
        var added = new List<Folder>();
        foreach (var rf in remote)
        {
            if (string.IsNullOrEmpty(rf.RemoteName) || !known.Add(rf.RemoteName)) continue;
            added.Add(new Folder
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                RemoteName = rf.RemoteName,
                Type = rf.Type != FolderType.Custom ? rf.Type : FolderClassifier.Classify(rf.RemoteName),
            });
        }
        return added;
    }

    private async Task<int> SyncFolderCoreAsync(Account account, Folder folder, IImapGateway gw, CancellationToken ct)
    {
        var limit = folder.LastSyncUid == 0 ? InitialSyncLimit : IncrementalSyncLimit;
        var fetched = await gw.FetchNewMessagesAsync(account, folder.RemoteName, folder.LastSyncUid, limit, ct);

        if (fetched.Count > 0)
        {
            var threads = new Dictionary<string, string>();
            foreach (var m in fetched.OrderBy(m => m.DateUtc ?? DateTime.MinValue))
            {
                m.FolderId = folder.Id;
                m.AccountId = account.Id;
                m.ThreadId = ThreadIdResolver.Resolve(m, threads);
                ThreadIdResolver.RegisterMessage(m, m.ThreadId, threads);
            }
            await _messages.SaveMessagesAsync(fetched, ct);
            folder.LastSyncUid = Math.Max(folder.LastSyncUid, fetched.Max(m => m.MessageUid));
        }

        folder.UnreadCount = await _messages.GetUnreadCountAsync(folder.Id, ct);
        await _accounts.UpdateFolderAsync(folder, ct);
        return fetched.Count;
    }

    private async Task<Account> RequireAccountAsync(Guid id, CancellationToken ct) =>
        await _accounts.GetAccountAsync(id, ct) ?? throw new InvalidOperationException("Account not found");
}
