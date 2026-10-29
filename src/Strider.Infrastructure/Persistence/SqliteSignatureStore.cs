using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of signature storage.
/// </summary>
public class SqliteSignatureStore : ISignatureStore
{
    private readonly string _connectionString;

    public SqliteSignatureStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task SaveSignatureAsync(Signature signature, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO signatures (id, account_id, name, content_html, content_plain, is_default, sort_order, created_at, updated_at, attach_as_html_file)
            VALUES (@Id, @AccountId, @Name, @ContentHtml, @ContentPlain, @IsDefault, @SortOrder, @CreatedAt, @UpdatedAt, @AttachAsHtmlFile)
            ON CONFLICT(id) DO UPDATE SET
                name=@Name, content_html=@ContentHtml, content_plain=@ContentPlain,
                is_default=@IsDefault, sort_order=@SortOrder, updated_at=@UpdatedAt,
                attach_as_html_file=@AttachAsHtmlFile
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = signature.Id.ToString(),
            AccountId = signature.AccountId.ToString(),
            signature.Name,
            signature.ContentHtml,
            signature.ContentPlain,
            IsDefault = signature.IsDefault ? 1 : 0,
            AttachAsHtmlFile = signature.AttachAsHtmlFile ? 1 : 0,
            SortOrder = signature.SortOrder,
            CreatedAt = new DateTimeOffset(signature.CreatedAt).ToUnixTimeSeconds(),
            UpdatedAt = new DateTimeOffset(signature.UpdatedAt).ToUnixTimeSeconds(),
        }, cancellationToken: ct));
    }

    public async Task<Signature?> GetSignatureAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM signatures WHERE id = @Id";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));

        return row is null ? null : MapSignature(row);
    }

    public async Task<IReadOnlyList<Signature>> GetSignaturesAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM signatures WHERE account_id = @AccountId ORDER BY sort_order, name";
        var rows = await db.QueryAsync(
            new CommandDefinition(sql, new { AccountId = accountId.ToString() }, cancellationToken: ct));

        return rows.Select(MapSignature).ToList();
    }

    public async Task UpdateSignatureAsync(Signature signature, CancellationToken ct = default)
    {
        signature.UpdatedAt = DateTime.UtcNow;
        await SaveSignatureAsync(signature, ct);
    }

    public async Task DeleteSignatureAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = "DELETE FROM signatures WHERE id = @Id";
        await db.ExecuteAsync(new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));
    }

    private static Signature MapSignature(dynamic row) => new()
    {
        Id = Guid.Parse((string)row.id),
        AccountId = Guid.Parse((string)row.account_id),
        Name = (string)row.name,
        ContentHtml = (string?)row.content_html,
        AttachAsHtmlFile = (long)row.attach_as_html_file == 1,
        ContentPlain = (string?)row.content_plain,
        IsDefault = (long)row.is_default == 1,
        SortOrder = (int)(long)row.sort_order,
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.created_at).UtcDateTime,
        UpdatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.updated_at).UtcDateTime,
    };
}
