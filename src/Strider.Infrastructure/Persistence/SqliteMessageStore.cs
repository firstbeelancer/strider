using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of message storage.
/// </summary>
public class SqliteMessageStore : IMessageStore
{
    private readonly string _connectionString;

    public SqliteMessageStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    // === Messages ===

    public async Task SaveMessageAsync(Message message, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO messages (id, account_id, folder_id, message_uid, message_id, in_reply_to,
                references_json, from_address, from_name, to_addresses, cc_addresses,
                subject, date_utc, size, has_attachments, is_read, is_starred, is_flagged,
                thread_id, ai_category, ai_summary, pgp_status, pgp_verified, fetched_at)
            VALUES (@Id, @AccountId, @FolderId, @MessageUid, @MessageId, @InReplyTo,
                @References, @FromAddress, @FromName, @ToAddresses, @CcAddresses,
                @Subject, @DateUtc, @Size, @HasAttachments, @IsRead, @IsStarred, @IsFlagged,
                @ThreadId, @AiCategory, @AiSummary, @PgpStatus, @PgpVerified, @FetchedAt)
            ON CONFLICT(account_id, folder_id, message_uid) DO UPDATE SET
                message_id=@MessageId, in_reply_to=@InReplyTo, references_json=@References,
                from_address=@FromAddress, from_name=@FromName, to_addresses=@ToAddresses,
                cc_addresses=@CcAddresses, subject=@Subject, date_utc=@DateUtc, size=@Size,
                has_attachments=@HasAttachments, is_read=@IsRead, is_starred=@IsStarred,
                is_flagged=@IsFlagged, thread_id=@ThreadId, ai_category=@AiCategory,
                ai_summary=@AiSummary, pgp_status=@PgpStatus, pgp_verified=@PgpVerified,
                fetched_at=@FetchedAt
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, MapToParams(message), cancellationToken: ct));
    }

    public async Task SaveMessagesAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        foreach (var message in messages)
        {
            await SaveMessageAsync(message, ct);
        }
    }

    public async Task<Message?> GetMessageAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM messages WHERE id = @Id";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));

        return row is null ? null : MapMessage(row);
    }

    public async Task<Message?> GetMessageByUidAsync(Guid accountId, Guid folderId, int uid, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM messages WHERE account_id=@AccountId AND folder_id=@FolderId AND message_uid=@Uid";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new
            {
                AccountId = accountId.ToString(),
                FolderId = folderId.ToString(),
                Uid = uid,
            }, cancellationToken: ct));

        return row is null ? null : MapMessage(row);
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid folderId, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM messages WHERE folder_id=@FolderId ORDER BY date_utc DESC LIMIT @Limit OFFSET @Offset";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new
            {
                FolderId = folderId.ToString(),
                Limit = limit,
                Offset = offset,
            }, cancellationToken: ct));

        return rows.Select(MapMessage).ToList();
    }

    public async Task<IReadOnlyList<Message>> GetThreadMessagesAsync(string threadId, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM messages WHERE thread_id=@ThreadId ORDER BY date_utc ASC";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new { ThreadId = threadId }, cancellationToken: ct));

        return rows.Select(MapMessage).ToList();
    }

    public async Task UpdateMessageAsync(Message message, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            UPDATE messages SET
                is_read=@IsRead, is_starred=@IsStarred, is_flagged=@IsFlagged,
                ai_category=@AiCategory, ai_summary=@AiSummary,
                pgp_status=@PgpStatus, pgp_verified=@PgpVerified
            WHERE id=@Id
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, MapToParams(message), cancellationToken: ct));
    }

    public async Task DeleteMessageAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "DELETE FROM messages WHERE id = @Id";
        await db.ExecuteAsync(new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));
    }

    public async Task<int> GetMessageCountAsync(Guid folderId, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT COUNT(*) FROM messages WHERE folder_id = @FolderId";
        return await db.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { FolderId = folderId.ToString() }, cancellationToken: ct));
    }

    public async Task<int> GetUnreadCountAsync(Guid folderId, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT COUNT(*) FROM messages WHERE folder_id = @FolderId AND is_read = 0";
        return await db.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { FolderId = folderId.ToString() }, cancellationToken: ct));
    }

    // === Message Bodies ===

    public async Task SaveBodyAsync(MessageBody body, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO message_bodies (message_id, text_plain, text_html, raw_mime_path)
            VALUES (@MessageId, @TextPlain, @TextHtml, @RawMimePath)
            ON CONFLICT(message_id) DO UPDATE SET
                text_plain=@TextPlain, text_html=@TextHtml, raw_mime_path=@RawMimePath
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            MessageId = body.MessageId.ToString(),
            body.TextPlain,
            body.TextHtml,
            body.RawMimePath,
        }, cancellationToken: ct));
    }

    public async Task<MessageBody?> GetBodyAsync(Guid messageId, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM message_bodies WHERE message_id = @MessageId";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { MessageId = messageId.ToString() }, cancellationToken: ct));

        if (row is null) return null;

        return new MessageBody
        {
            MessageId = Guid.Parse((string)row.message_id),
            TextPlain = (string?)row.text_plain,
            TextHtml = (string?)row.text_html,
            RawMimePath = (string?)row.raw_mime_path,
        };
    }

    // === Attachments ===

    public async Task SaveAttachmentAsync(Attachment attachment, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO attachments (id, message_id, filename, content_type, size, content_id, local_path)
            VALUES (@Id, @MessageId, @Filename, @ContentType, @Size, @ContentId, @LocalPath)
            ON CONFLICT(id) DO UPDATE SET
                filename=@Filename, content_type=@ContentType, size=@Size,
                content_id=@ContentId, local_path=@LocalPath
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = attachment.Id.ToString(),
            MessageId = attachment.MessageId.ToString(),
            attachment.Filename,
            attachment.ContentType,
            attachment.Size,
            attachment.ContentId,
            attachment.LocalPath,
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid messageId, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM attachments WHERE message_id = @MessageId";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new { MessageId = messageId.ToString() }, cancellationToken: ct));

        return rows.Select(row => new Attachment
        {
            Id = Guid.Parse((string)row.id),
            MessageId = Guid.Parse((string)row.message_id),
            Filename = (string?)row.filename,
            ContentType = (string?)row.content_type,
            Size = (long)row.size,
            ContentId = (string?)row.content_id,
            LocalPath = (string?)row.local_path,
        }).ToList();
    }

    public async Task<Attachment?> GetAttachmentAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM attachments WHERE id = @Id";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));

        if (row is null) return null;

        return new Attachment
        {
            Id = Guid.Parse((string)row.id),
            MessageId = Guid.Parse((string)row.message_id),
            Filename = (string?)row.filename,
            ContentType = (string?)row.content_type,
            Size = (long)row.size,
            ContentId = (string?)row.content_id,
            LocalPath = (string?)row.local_path,
        };
    }

    // === Search ===

    public async Task<IReadOnlyList<Message>> SearchAsync(Guid accountId, string query, int limit = 100, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        // Simple LIKE-based search (FTS5 can be added later)
        const string sql = """
            SELECT * FROM messages 
            WHERE account_id = @AccountId 
              AND (subject LIKE @Query OR from_address LIKE @Query OR from_name LIKE @Query)
            ORDER BY date_utc DESC 
            LIMIT @Limit
            """;

        var searchPattern = $"%{query}%";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new
            {
                AccountId = accountId.ToString(),
                Query = searchPattern,
                Limit = limit,
            }, cancellationToken: ct));

        return rows.Select(MapMessage).ToList();
    }

    // === Threads ===

    public async Task<IReadOnlyList<EmailThread>> GetThreadsAsync(Guid folderId, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            SELECT 
                thread_id,
                COUNT(*) as message_count,
                MAX(date_utc) as last_date_utc,
                (SELECT from_address FROM messages m2 WHERE m2.thread_id = messages.thread_id ORDER BY m2.date_utc DESC LIMIT 1) as last_from_address,
                (SELECT from_name FROM messages m2 WHERE m2.thread_id = messages.thread_id ORDER BY m2.date_utc DESC LIMIT 1) as last_from_name,
                MAX(subject) as subject,
                SUM(CASE WHEN is_read = 0 THEN 1 ELSE 0 END) as unread_count,
                MAX(has_attachments) as has_attachments,
                MAX(is_starred) as is_starred,
                MAX(ai_category) as ai_category,
                MAX(ai_summary) as ai_summary
            FROM messages 
            WHERE folder_id = @FolderId
            GROUP BY thread_id
            ORDER BY last_date_utc DESC
            LIMIT @Limit OFFSET @Offset
            """;

        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new
            {
                FolderId = folderId.ToString(),
                Limit = limit,
                Offset = offset,
            }, cancellationToken: ct));

        return rows.Select(row => new EmailThread
        {
            ThreadId = (string)row.thread_id,
            MessageCount = (int)(long)row.message_count,
            LastDateUtc = DateTimeOffset.FromUnixTimeSeconds((long)row.last_date_utc).UtcDateTime,
            LastFromAddress = (string)row.last_from_address,
            LastFromName = (string?)row.last_from_name,
            Subject = (string)row.subject,
            HasUnread = (long)row.unread_count > 0,
            HasAttachments = (long)row.has_attachments == 1,
            IsStarred = (long)row.is_starred == 1,
            AiCategory = (string?)row.ai_category,
            AiSummary = (string?)row.ai_summary,
        }).ToList();
    }

    // === Helpers ===

    private static object MapToParams(Message m) => new
    {
        Id = m.Id.ToString(),
        AccountId = m.AccountId.ToString(),
        FolderId = m.FolderId.ToString(),
        MessageUid = m.MessageUid,
        m.MessageId,
        m.InReplyTo,
        References = m.References,
        m.FromAddress,
        m.FromName,
        m.ToAddresses,
        m.CcAddresses,
        m.Subject,
        DateUtc = m.DateUtc.HasValue ? new DateTimeOffset(m.DateUtc.Value).ToUnixTimeSeconds() : (long?)null,
        Size = m.Size,
        HasAttachments = m.HasAttachments ? 1 : 0,
        IsRead = m.IsRead ? 1 : 0,
        IsStarred = m.IsStarred ? 1 : 0,
        IsFlagged = m.IsFlagged ? 1 : 0,
        m.ThreadId,
        m.AiCategory,
        m.AiSummary,
        PgpStatus = m.PgpStatus.ToString().ToLowerInvariant(),
        PgpVerified = m.PgpVerified.ToString().ToLowerInvariant(),
        FetchedAt = new DateTimeOffset(m.FetchedAt).ToUnixTimeSeconds(),
    };

    private static Message MapMessage(dynamic row)
    {
        return new Message
        {
            Id = Guid.Parse((string)row.id),
            AccountId = Guid.Parse((string)row.account_id),
            FolderId = Guid.Parse((string)row.folder_id),
            MessageUid = (int)(long)row.message_uid,
            MessageId = (string?)row.message_id,
            InReplyTo = (string?)row.in_reply_to,
            References = (string?)row.references_json,
            FromAddress = (string)row.from_address,
            FromName = (string?)row.from_name,
            ToAddresses = (string)row.to_addresses,
            CcAddresses = (string?)row.cc_addresses,
            Subject = (string)row.subject,
            DateUtc = row.date_utc is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)row.date_utc).UtcDateTime,
            Size = (long)row.size,
            HasAttachments = (long)row.has_attachments == 1,
            IsRead = (long)row.is_read == 1,
            IsStarred = (long)row.is_starred == 1,
            IsFlagged = (long)row.is_flagged == 1,
            ThreadId = (string?)row.thread_id,
            AiCategory = (string?)row.ai_category,
            AiSummary = (string?)row.ai_summary,
            PgpStatus = Enum.TryParse<PgpStatus>((string)row.pgp_status, true, out var ps) ? ps : PgpStatus.None,
            PgpVerified = Enum.TryParse<PgpVerification>((string)row.pgp_verified, true, out var pv) ? pv : PgpVerification.Unknown,
            FetchedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.fetched_at).UtcDateTime,
        };
    }
}
