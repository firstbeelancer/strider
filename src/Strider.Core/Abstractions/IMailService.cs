using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Pulls mail from the server into the local store.
/// </summary>
public interface IMailSyncService
{
    /// <summary>Refreshes the folder list and syncs the Inbox. Returns number of new messages.</summary>
    Task<int> SyncAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Fetches headers of messages newer than the folder's last synced UID.</summary>
    Task<int> SyncFolderAsync(Guid accountId, Guid folderId, CancellationToken ct = default);

    /// <summary>Returns the cached body, downloading it from IMAP on first open.</summary>
    Task<MessageBody?> EnsureBodyAsync(Message message, CancellationToken ct = default);

    /// <summary>Returns the attachment list, reading it from the server on first open.</summary>
    Task<IReadOnlyList<Attachment>> EnsureAttachmentsAsync(Message message, CancellationToken ct = default);

    /// <summary>Downloads the attachment content.</summary>
    Task<byte[]> DownloadAttachmentAsync(Message message, Attachment attachment, CancellationToken ct = default);

    /// <summary>Sets the \Seen flag on the server. Best effort.</summary>
    Task MarkAsReadOnServerAsync(Message message, CancellationToken ct = default);
}

/// <summary>
/// User actions that change mail on the server.
/// </summary>
public interface IMailActionService
{
    /// <summary>Sends via SMTP and stores a copy in the Sent folder.</summary>
    Task SendAsync(Guid accountId, OutgoingMessage message, CancellationToken ct = default);

    /// <summary>
    /// Stores the draft in the Drafts folder, replacing the previous version when its UID is known.
    /// Returns the new draft UID (null if the server does not report it).
    /// </summary>
    Task<int?> SaveDraftAsync(Guid accountId, OutgoingMessage draft, int? previousUid, CancellationToken ct = default);

    /// <summary>Removes a saved draft (after the message is sent).</summary>
    Task DeleteDraftAsync(Guid accountId, int uid, CancellationToken ct = default);

    /// <summary>Moves the message to the account's Archive folder.</summary>
    Task ArchiveAsync(Message message, CancellationToken ct = default);

    /// <summary>Moves to Trash, or deletes permanently when already in Trash.</summary>
    Task TrashAsync(Message message, CancellationToken ct = default);
}
