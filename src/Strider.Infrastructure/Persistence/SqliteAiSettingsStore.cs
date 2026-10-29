using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IAiSettingsStore"/> (table <c>ai_settings</c>).
/// Stores configuration only — the API key lives in the OS keychain and is
/// referenced by <see cref="AiSettings.ApiKeyRef"/>.
/// </summary>
public class SqliteAiSettingsStore : IAiSettingsStore
{
    private readonly string _connectionString;

    public SqliteAiSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    private const string SelectColumns =
        "id, provider, model, api_key_ref, base_url, is_default, created_at";

    public async Task<IReadOnlyList<AiSettings>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        var rows = await db.QueryAsync(new CommandDefinition(
            $"SELECT {SelectColumns} FROM ai_settings ORDER BY is_default DESC, created_at DESC",
            cancellationToken: ct));

        return rows.Select(MapSettings).ToList();
    }

    public async Task<AiSettings?> GetDefaultAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = $"""
            SELECT {SelectColumns} FROM ai_settings
            ORDER BY is_default DESC, created_at DESC
            LIMIT 1
            """;

        var row = await db.QuerySingleOrDefaultAsync(new CommandDefinition(sql, cancellationToken: ct));
        return row is null ? null : MapSettings(row);
    }

    public async Task SaveAsync(AiSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        try
        {
            // Exactly one default provider: clear the flag on everybody else first.
            if (settings.IsDefault)
            {
                await db.ExecuteAsync(new CommandDefinition(
                    "UPDATE ai_settings SET is_default = 0",
                    transaction: tx, cancellationToken: ct));
            }

            const string sql = """
                INSERT INTO ai_settings (id, provider, model, api_key_ref, base_url, is_default, created_at)
                VALUES (@Id, @Provider, @Model, @ApiKeyRef, @BaseUrl, @IsDefault, @CreatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    provider=@Provider, model=@Model, api_key_ref=@ApiKeyRef,
                    base_url=@BaseUrl, is_default=@IsDefault
                """;

            await db.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = settings.Id.ToString(),
                settings.Provider,
                settings.Model,
                settings.ApiKeyRef,
                settings.BaseUrl,
                IsDefault = settings.IsDefault ? 1 : 0,
                CreatedAt = new DateTimeOffset(settings.CreatedAt.ToUniversalTime()).ToUnixTimeSeconds(),
            }, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ai_settings WHERE id = @Id",
            new { Id = id.ToString() }, cancellationToken: ct));
    }

    private static AiSettings MapSettings(dynamic row) => new()
    {
        Id = Guid.Parse((string)row.id),
        Provider = (string)row.provider,
        Model = (string)row.model,
        ApiKeyRef = (string?)row.api_key_ref,
        BaseUrl = (string?)row.base_url,
        IsDefault = (long)row.is_default == 1,
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.created_at).UtcDateTime,
    };
}
