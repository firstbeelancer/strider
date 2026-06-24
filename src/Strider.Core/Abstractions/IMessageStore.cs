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

    // Message bodies
    Task SaveBodyAsync(MessageBody body, CancellationToken ct = default);
    Task<MessageBody?> GetBodyAsync(Guid messageId, CancellationToken ct = default);

    // Attachments
    Task SaveAttachmentAsync(Attachment attachment, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid messageId, CancellationToken ct = default);
    Task<Attachment?> GetAttachmentAsync(Guid id, CancellationToken ct = default);

    // Search
    Task<IReadOnlyList<Message>> SearchAsync(Guid accountId, string query, int limit = 100, CancellationToken ct = default);

    // Threads
    Task<IReadOnlyList<EmailThread>> GetThreadsAsync(Guid folderId, int limit = 50, int offset = 0, CancellationToken ct = default);
}
