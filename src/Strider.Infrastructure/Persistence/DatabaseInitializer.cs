using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Text;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// Initializes the SQLite database by applying pending migrations.
///
/// Migrations are embedded .sql resources under <c>Persistence.Migrations</c>,
/// named <c>NNNN_description.sql</c> (e.g., <c>0001_initial.sql</c>).
/// The schema version is tracked in <c>schema_migrations</c> table.
///
/// This replaces the old approach of inline Schema.sql duplication (F-013).
/// </summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly Assembly _assembly = typeof(DatabaseInitializer).Assembly;

    private const string MigrationsNamespace = "Strider.Infrastructure.Persistence.Migrations";

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Apply PRAGMAs that must be set per-connection
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;
                """;
            await pragma.ExecuteNonQueryAsync(ct);
        }

        // Ensure schema_migrations table exists
        await using (var ensureMigrationsTable = connection.CreateCommand())
        {
            ensureMigrationsTable.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at INTEGER NOT NULL
                );
                """;
            await ensureMigrationsTable.ExecuteNonQueryAsync(ct);
        }

        // Read already-applied migrations
        var applied = new HashSet<int>();
        await using (var readApplied = connection.CreateCommand())
        {
            readApplied.CommandText = "SELECT version FROM schema_migrations";
            await using var reader = await readApplied.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                applied.Add(reader.GetInt32(0));
            }
        }

        // Discover migrations from embedded resources
        var migrationResources = _assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(MigrationsNamespace + ".", StringComparison.Ordinal))
            .Select(n => new
            {
                ResourceName = n,
                // Extract "0001" from "Strider.Infrastructure.Persistence.Migrations.0001_initial.sql"
                Version = int.Parse(n.Substring(MigrationsNamespace.Length + 1, 4)),
                Name = n.Substring(MigrationsNamespace.Length + 1),
            })
            .OrderBy(m => m.Version)
            .ToList();

        if (migrationResources.Count == 0)
        {
            throw new InvalidOperationException(
                "No embedded SQL migrations found. Ensure Migrations/*.sql are marked as EmbeddedResource in csproj.");
        }

        // Apply pending migrations in order, each in its own transaction
        foreach (var migration in migrationResources)
        {
            if (applied.Contains(migration.Version)) continue;

            await ApplyMigrationAsync(connection, migration.Version, migration.Name, migration.ResourceName, ct);
        }
    }

    private async Task ApplyMigrationAsync(
        SqliteConnection connection, int version, string name, string resourceName, CancellationToken ct)
    {
        // Read SQL from embedded resource
        await using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(ct);

        // Apply in a transaction. Note: SQLite's PRAGMA statements cannot be
        // inside a transaction, so we split them out.
        var statements = SplitSqlStatements(sql);
        var pragmas = statements.Where(s => s.TrimStart().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)).ToList();
        var nonPragmas = statements.Where(s => !s.TrimStart().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)).ToList();

        // PRAGMAs run outside transaction
        foreach (var pragma in pragmas)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = pragma;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Non-PRAGMA statements in a transaction
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            foreach (var stmt in nonPragmas)
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                await using var cmd = connection.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = stmt;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Record migration
            await using var record = connection.CreateCommand();
            record.Transaction = (SqliteTransaction)tx;
            record.CommandText = """
                INSERT INTO schema_migrations (version, name, applied_at)
                VALUES (@version, @name, @appliedAt);
                """;
            record.Parameters.AddWithValue("@version", version);
            record.Parameters.AddWithValue("@name", name);
            record.Parameters.AddWithValue("@appliedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await record.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Splits SQL on semicolons that are not inside string literals.
    /// </summary>
    private static List<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        bool inString = false;
        char stringChar = '\'';

        for (int i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (inString)
            {
                current.Append(c);
                if (c == stringChar)
                {
                    // Handle escaped quote ('' inside string)
                    if (i + 1 < sql.Length && sql[i + 1] == stringChar)
                    {
                        current.Append(sql[++i]);
                    }
                    else
                    {
                        inString = false;
                    }
                }
            }
            else
            {
                if (c == '\'' || c == '"')
                {
                    inString = true;
                    stringChar = c;
                    current.Append(c);
                }
                else if (c == ';')
                {
                    statements.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
        {
            statements.Add(current.ToString());
        }
        return statements;
    }

    /// <summary>
    /// Returns the list of applied migration versions for diagnostics.
    /// </summary>
    public async Task<IReadOnlyList<(int Version, string Name, DateTime AppliedAt)>> GetAppliedMigrationsAsync(
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var result = new List<(int, string, DateTime)>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version, name, applied_at FROM schema_migrations ORDER BY version";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)).UtcDateTime));
        }
        return result;
    }
}
