using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Persistence;

/// <summary>SQLite key/value store for user preferences (table <c>app_settings</c>).</summary>
public class SqliteAppSettingsStore : IAppSettingsStore
{
    private readonly string _connectionString;

    public SqliteAppSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        return await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM app_settings WHERE key = @Key",
            new { Key = key }, cancellationToken: ct));
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@Key, @Value, @UpdatedAt)
            ON CONFLICT(key) DO UPDATE SET value = @Value, updated_at = @UpdatedAt
            """,
            new
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM app_settings WHERE key = @Key", new { Key = key }, cancellationToken: ct));
    }
}
