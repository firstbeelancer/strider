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

    /// <summary>Appends a raw RFC 822 message to a folder (used to store sent mail).</summary>
    Task AppendMessageAsync(Account account, string folderName, byte[] rawMime, bool seen, CancellationToken ct = default);

    Task<MessageBody> FetchBodyAsync(Account account, string folderName, int messageUid, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> FetchAttachmentsAsync(
        Account account, string folderName, int messageUid, CancellationToken ct = default);
    Task<Stream> DownloadAttachmentAsync(
        Account account, string folderName, int messageUid, string contentId, CancellationToken ct = default);

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
