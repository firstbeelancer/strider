using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// IMAP email gateway.
/// </summary>
public interface IImapGateway
{
    Task ConnectAsync(Account account, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<bool> IsConnectedAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Folder>> GetFoldersAsync(Account account, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> FetchMessagesAsync(
        Account account, string folderName, int fromUid, int toUid, CancellationToken ct = default);
    /// <summary>
    /// Fetches headers of messages with UID greater than <paramref name="afterUid"/>,
    /// at most the newest <paramref name="maxCount"/> of them.
    /// </summary>
    Task<IReadOnlyList<Message>> FetchNewMessagesAsync(
        Account account, string folderName, int afterUid, int maxCount, CancellationToken ct = default);

    /// <summary>
    /// Returns UIDVALIDITY and the flags of messages with UID &gt;= <paramref name="minUid"/>;
    /// a cached UID missing from the result was removed on the server.
    /// </summary>
    Task<FolderSnapshot> GetFolderSnapshotAsync(
        Account account, string folderName, int minUid, CancellationToken ct = default);

    /// <summary>
    /// Appends a raw RFC 822 message to a folder (sent copies, drafts).
    /// Returns the new UID when the server reports it (UIDPLUS), otherwise null.
    /// </summary>
    Task<int?> AppendMessageAsync(Account account, string folderName, byte[] rawMime, bool seen, bool draft = false, CancellationToken ct = default);

    Task<MessageBody> FetchBodyAsync(Account account, string folderName, int messageUid, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> FetchAttachmentsAsync(
        Account account, string folderName, int messageUid, CancellationToken ct = default);
    /// <summary>
    /// Downloads one attachment. <paramref name="key"/> is its Content-ID, or its file name
    /// when the part has no Content-ID (most regular attachments).
    /// </summary>
    Task<Stream> DownloadAttachmentAsync(
        Account account, string folderName, int messageUid, string key, CancellationToken ct = default);

    Task MarkAsReadAsync(Account account, string folderName, int messageUid, CancellationToken ct = default);
    Task MarkAsUnreadAsync(Account account, string folderName, int messageUid, CancellationToken ct = default);
    Task MoveMessageAsync(Account account, string fromFolder, int messageUid, string toFolder, CancellationToken ct = default);
    Task DeleteMessageAsync(Account account, string folderName, int messageUid, CancellationToken ct = default);
    Task AddFlagAsync(Account account, string folderName, int messageUid, string flag, CancellationToken ct = default);
    Task RemoveFlagAsync(Account account, string folderName, int messageUid, string flag, CancellationToken ct = default);

    /// <summary>
    /// Start IDLE mode for real-time notifications.
    /// </summary>
    Task IdleAsync(Account account, string folderName, Action onNewMessage, CancellationToken ct = default);
}
