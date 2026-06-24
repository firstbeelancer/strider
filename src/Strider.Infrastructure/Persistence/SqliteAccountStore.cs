using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of account and folder storage.
/// </summary>
public class SqliteAccountStore : IAccountStore
{
    private readonly string _connectionString;

    public SqliteAccountStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    // === Accounts ===

    public async Task SaveAccountAsync(Account account, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO accounts (id, email, display_name, imap_host, imap_port, imap_use_ssl,
                smtp_host, smtp_port, smtp_use_ssl, oauth2_token_ref, sync_state,
                default_signature_id, created_at, updated_at)
            VALUES (@Id, @Email, @DisplayName, @ImapHost, @ImapPort, @ImapUseSsl,
                @SmtpHost, @SmtpPort, @SmtpUseSsl, @OAuth2TokenRef, @SyncState,
                @DefaultSignatureId, @CreatedAt, @UpdatedAt)
            ON CONFLICT(id) DO UPDATE SET
                email=@Email, display_name=@DisplayName,
                imap_host=@ImapHost, imap_port=@ImapPort, imap_use_ssl=@ImapUseSsl,
                smtp_host=@SmtpHost, smtp_port=@SmtpPort, smtp_use_ssl=@SmtpUseSsl,
                oauth2_token_ref=@OAuth2TokenRef, sync_state=@SyncState,
                default_signature_id=@DefaultSignatureId, updated_at=@UpdatedAt
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            account.Id,
            account.Email,
            DisplayName = account.DisplayName,
            account.ImapHost,
            account.ImapPort,
            ImapUseSsl = account.ImapUseSsl ? 1 : 0,
            account.SmtpHost,
            account.SmtpPort,
            SmtpUseSsl = account.SmtpUseSsl ? 1 : 0,
            account.OAuth2TokenRef,
            account.SyncState,
            account.DefaultSignatureId,
            CreatedAt = new DateTimeOffset(account.CreatedAt).ToUnixTimeSeconds(),
            UpdatedAt = new DateTimeOffset(account.UpdatedAt).ToUnixTimeSeconds(),
        }, cancellationToken: ct));
    }

    public async Task<Account?> GetAccountAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM accounts WHERE id = @Id";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));

        return row is null ? null : MapAccount(row);
    }

    public async Task<IReadOnlyList<Account>> GetAllAccountsAsync(CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM accounts ORDER BY email";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(MapAccount).ToList();
    }

    public async Task UpdateAccountAsync(Account account, CancellationToken ct = default)
    {
        account.UpdatedAt = DateTime.UtcNow;
        await SaveAccountAsync(account, ct);
    }

    public async Task DeleteAccountAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "DELETE FROM accounts WHERE id = @Id";
        await db.ExecuteAsync(new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));
    }

    // === Folders ===

    public async Task SaveFolderAsync(Folder folder, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO folders (id, account_id, remote_name, type, parent_id, last_sync_uid, unread_count)
            VALUES (@Id, @AccountId, @RemoteName, @Type, @ParentId, @LastSyncUid, @UnreadCount)
            ON CONFLICT(id) DO UPDATE SET
                remote_name=@RemoteName, type=@Type, parent_id=@ParentId,
                last_sync_uid=@LastSyncUid, unread_count=@UnreadCount
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = folder.Id.ToString(),
            AccountId = folder.AccountId.ToString(),
            folder.RemoteName,
            Type = folder.Type.ToString().ToLowerInvariant(),
            ParentId = folder.ParentId?.ToString(),
            folder.LastSyncUid,
            folder.UnreadCount,
        }, cancellationToken: ct));
    }

    public async Task SaveFoldersAsync(IEnumerable<Folder> folders, CancellationToken ct = default)
    {
        foreach (var folder in folders)
        {
            await SaveFolderAsync(folder, ct);
        }
    }

    public async Task<Folder?> GetFolderAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM folders WHERE id = @Id";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));

        return row is null ? null : MapFolder(row);
    }

    public async Task<IReadOnlyList<Folder>> GetFoldersAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM folders WHERE account_id = @AccountId ORDER BY type, remote_name";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new { AccountId = accountId.ToString() }, cancellationToken: ct));

        return rows.Select(MapFolder).ToList();
    }

    public async Task UpdateFolderAsync(Folder folder, CancellationToken ct = default)
    {
        await SaveFolderAsync(folder, ct);
    }

    public async Task DeleteFolderAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        const string sql = "DELETE FROM folders WHERE id = @Id";
        await db.ExecuteAsync(new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));
    }

    // === Mapping ===

    private static Account MapAccount(dynamic row)
    {
        return new Account
        {
            Id = Guid.Parse((string)row.id),
            Email = (string)row.email,
            DisplayName = (string)row.display_name,
            ImapHost = (string)row.imap_host,
            ImapPort = (int)row.imap_port,
            ImapUseSsl = (long)row.imap_use_ssl == 1,
            SmtpHost = (string)row.smtp_host,
            SmtpPort = (int)row.smtp_port,
            SmtpUseSsl = (long)row.smtp_use_ssl == 1,
            OAuth2TokenRef = (string?)row.oauth2_token_ref,
            SyncState = (string?)row.sync_state,
            DefaultSignatureId = row.default_signature_id is null ? null : Guid.Parse((string)row.default_signature_id),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.created_at).UtcDateTime,
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.updated_at).UtcDateTime,
        };
    }

    private static Folder MapFolder(dynamic row)
    {
        return new Folder
        {
            Id = Guid.Parse((string)row.id),
            AccountId = Guid.Parse((string)row.account_id),
            RemoteName = (string)row.remote_name,
            Type = Enum.Parse<FolderType>((string)row.type, ignoreCase: true),
            ParentId = row.parent_id is null ? null : Guid.Parse((string)row.parent_id),
            LastSyncUid = (int)(long)row.last_sync_uid,
            UnreadCount = (int)(long)row.unread_count,
        };
    }
}
