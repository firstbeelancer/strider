using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>SQLite implementation of <see cref="ICustomFontStore"/> (table <c>custom_fonts</c>).</summary>
public class SqliteCustomFontStore : ICustomFontStore
{
    private readonly string _connectionString;

    public SqliteCustomFontStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<CustomFont>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT id, family, file_path, added_at FROM custom_fonts ORDER BY family",
            cancellationToken: ct));

        return rows.Select(row => new CustomFont
        {
            Id = Guid.Parse((string)row.id),
            Family = (string)row.family,
            FilePath = (string)row.file_path,
            AddedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.added_at).UtcDateTime,
        }).ToList();
    }

    public async Task SaveAsync(CustomFont font, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(font);

        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO custom_fonts (id, family, file_path, added_at)
            VALUES (@Id, @Family, @FilePath, @AddedAt)
            ON CONFLICT(family) DO UPDATE SET file_path = @FilePath
            """,
            new
            {
                Id = font.Id.ToString(),
                font.Family,
                font.FilePath,
                AddedAt = new DateTimeOffset(font.AddedAt.ToUniversalTime()).ToUnixTimeSeconds(),
            },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM custom_fonts WHERE id = @Id", new { Id = id.ToString() }, cancellationToken: ct));
    }
}
