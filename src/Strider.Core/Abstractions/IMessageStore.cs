using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Message persistence store.
/// </summary>
public interface IMessageStore
{
    // Messages
    Task SaveMessageAsync(Message message, CancellationToken ct = default);
    Task SaveMessagesAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    Task<Message?> GetMessageAsync(Guid id, CancellationToken ct = default);
    Task<Message?> GetMessageByUidAsync(Guid accountId, Guid folderId, int uid, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid folderId, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetThreadMessagesAsync(string threadId, CancellationToken ct = default);
    Task UpdateMessageAsync(Message message, CancellationToken ct = default);
    Task DeleteMessageAsync(Guid id, CancellationToken ct = default);
    Task<int> GetMessageCountAsync(Guid folderId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid folderId, CancellationToken ct = default);

    // Reconciliation with the server
    Task<IReadOnlyList<LocalMessageState>> GetLocalStatesAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Removes messages gone from the server and applies read/star changes, in one transaction.</summary>
    Task ApplyRemoteChangesAsync(
        IReadOnlyCollection<Guid> removed,
        IReadOnlyCollection<(Guid Id, bool IsRead, bool IsStarred)> changed,
        CancellationToken ct = default);

    /// <summary>Drops the whole cache of a folder (its UIDVALIDITY changed).</summary>
    Task DeleteFolderMessagesAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Message-ID to thread id for the given ids, so replies join threads synced earlier.</summary>
    Task<IReadOnlyDictionary<string, string>> GetThreadLookupAsync(
        Guid accountId, IReadOnlyCollection<string> messageIds, CancellationToken ct = default);

    // Message bodies
    Task SaveBodyAsync(MessageBody body, CancellationToken ct = default);
    Task<MessageBody?> GetBodyAsync(Guid messageId, CancellationToken ct = default);

    // Attachments
    Task SaveAttachmentAsync(Attachment attachment, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid messageId, CancellationToken ct = default);
    Task<Attachment?> GetAttachmentAsync(Guid id, CancellationToken ct = default);

    // Search
    /// <summary>Simple substring search over subject / sender.</summary>
    Task<IReadOnlyList<Message>> SearchAsync(Guid accountId, string query, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Structured search (F2.8). Pass <paramref name="folderId"/> to stay inside
    /// one folder, or null to search the whole account. Body matches use
    /// message_bodies, so cached bodies only.
    /// </summary>
    Task<IReadOnlyList<Message>> SearchMessagesAsync(
        Guid accountId,
        Guid? folderId,
        Strider.Core.Search.MessageQuery query,
        int limit = 200,
        CancellationToken ct = default);

    // Threads
    Task<IReadOnlyList<EmailThread>> GetThreadsAsync(Guid folderId, int limit = 50, int offset = 0, CancellationToken ct = default);
}
