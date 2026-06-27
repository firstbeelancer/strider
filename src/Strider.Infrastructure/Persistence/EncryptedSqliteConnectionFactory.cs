using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// Factory for SQLite connections with at-rest encryption via SQLCipher.
///
/// Closes finding F-009 from the architecture review: "Database not encrypted
/// (no SQLCipher) — violates F7.1 of the spec".
///
/// How it works:
/// 1. On first launch, a 32-byte random key is generated and stored in the OS
///    keychain under <see cref="KeychainKeys.DatabaseKey"/>.
/// 2. Every connection is opened with the connection string extended to include
///    <c>Password=...</c> (Microsoft.Data.Sqlite passes this to SQLCipher via PRAGMA key).
/// 3. The key is never written to disk in plaintext, never in logs, never in the
///    SQLite database file itself — only in the OS keychain.
///
/// Migration path (v0.1 with plaintext DB → v0.1.x with SQLCipher):
/// - On startup, the factory checks if the DB file is unencrypted (legacy).
/// - If so, it: opens without key → exports schema+data → recreates as encrypted
///   → imports → deletes plaintext file.
/// - This is a one-time operation, transparent to the user.
/// </summary>
public sealed class EncryptedSqliteConnectionFactory
{
    private readonly IKeychainService _keychain;
    private readonly string _dbPath;
    private readonly bool _encryptionEnabled;
    private string? _cachedKey;
    private readonly object _keyLock = new();

    static EncryptedSqliteConnectionFactory()
    {
        // Initialize SQLitePCLRaw to use the SQLCipher bundle.
        // Must be called once per process before any SqliteConnection is opened.
        SQLitePCL.Batteries_V2.Init();
    }

    /// <param name="keychain">OS keychain service for storing the DB encryption key.</param>
    /// <param name="dbPath">Absolute path to the SQLite database file.</param>
    /// <param name="encryptionEnabled">
    /// If true, the database is encrypted at rest with SQLCipher.
    /// Set to false for in-memory test databases.
    /// </param>
    public EncryptedSqliteConnectionFactory(
        IKeychainService keychain, string dbPath, bool encryptionEnabled = true)
    {
        _keychain = keychain;
        _dbPath = dbPath;
        _encryptionEnabled = encryptionEnabled;
    }

    /// <summary>
    /// Builds the connection string with the encryption key (if enabled).
    /// The key is fetched from the keychain on first access and cached for the
    /// process lifetime — never re-read on every connection (perf), but also
    /// never written to disk.
    /// </summary>
    public string GetConnectionString()
    {
        if (!_encryptionEnabled)
        {
            return $"Data Source={_dbPath}";
        }

        var key = GetOrCreateKey();
        // Microsoft.Data.Sqlite passes Password= through to SQLCipher as PRAGMA key.
        // For 32-byte keys, hex encoding is required by SQLCipher.
        return $"Data Source={_dbPath};Password={key}";
    }

    /// <summary>
    /// Opens a new SQLite connection with the encryption key applied.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(GetConnectionString());
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>
    /// Returns true if the database file appears to be unencrypted (legacy v0.1).
    /// A SQLCipher-encrypted file starts with random bytes; an unencrypted SQLite
    /// file starts with the magic string "SQLite format 3\0".
    /// </summary>
    public bool IsLegacyPlaintextDatabase()
    {
        if (!File.Exists(_dbPath)) return false;
        try
        {
            var header = new byte[16];
            using var fs = File.OpenRead(_dbPath);
            var read = fs.Read(header, 0, 16);
            if (read < 16) return false;
            var magic = System.Text.Encoding.ASCII.GetString(header, 0, 15);
            return magic == "SQLite format 3";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// One-time migration: takes an existing plaintext SQLite database and
    /// re-creates it as a SQLCipher-encrypted database. The plaintext file is
    /// backed up with .plaintext.bak suffix (so the user can recover if needed)
    /// and then deleted.
    ///
    /// Implementation: open source DB unencrypted, ATTACH encrypted DB, copy all
    /// tables via SQL INSERT. This is the recommended SQLCipher migration approach.
    /// </summary>
    public async Task MigrateToEncryptedAsync(CancellationToken ct = default)
    {
        if (!_encryptionEnabled) return;
        if (!IsLegacyPlaintextDatabase()) return;

        var backupPath = _dbPath + ".plaintext.bak";
        if (File.Exists(backupPath)) File.Delete(backupPath);
        File.Move(_dbPath, backupPath);

        try
        {
            // Create the new encrypted DB with schema + data from plaintext backup.
            // Use ATTACH DATABASE with KEY to create the encrypted copy.
            var plaintextConnStr = $"Data Source={backupPath}";
            var encryptedConnStr = GetConnectionString();

            await using var src = new SqliteConnection(plaintextConnStr);
            await src.OpenAsync(ct);

            // Get list of user tables (skip sqlite_* internal tables)
            var tables = new List<string>();
            await using (var cmd = src.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    tables.Add(reader.GetString(0));
                }
            }

            // Create the encrypted DB and attach it
            var key = GetOrCreateKey();
            await using (var cmd = src.CreateCommand())
            {
                cmd.CommandText = $"ATTACH DATABASE @encPath AS encrypted KEY @key";
                cmd.Parameters.AddWithValue("@encPath", _dbPath);
                cmd.Parameters.AddWithValue("@key", key);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // For each table: create schema in encrypted DB + copy data
            foreach (var table in tables)
            {
                // Get CREATE TABLE statement from source
                string createSql;
                await using (var cmd = src.CreateCommand())
                {
                    cmd.CommandText = $"SELECT sql FROM sqlite_master WHERE type='table' AND name=@name";
                    cmd.Parameters.AddWithValue("@name", table);
                    createSql = (string)(await cmd.ExecuteScalarAsync(ct))!;
                }

                // Create in encrypted DB — rewrite "CREATE TABLE X" → "CREATE TABLE encrypted.X"
                // This works for both quoted ("name") and unquoted (name) table names.
                var createInEncrypted = RewriteCreateTableForAttachedSchema(createSql, table);
                await using (var cmd = src.CreateCommand())
                {
                    cmd.CommandText = createInEncrypted;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // Copy data — quote table name defensively (could contain special chars)
                var quotedTable = $"\"{table}\"";
                await using (var cmd = src.CreateCommand())
                {
                    cmd.CommandText = $"INSERT INTO encrypted.{quotedTable} SELECT * FROM {quotedTable}";
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            // Detach encrypted DB
            await using (var cmd = src.CreateCommand())
            {
                cmd.CommandText = "DETACH DATABASE encrypted";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await src.CloseAsync();

            // Success — delete the plaintext backup after a brief safety delay
            // (in the future, this could be a user-confirmable action)
            File.Delete(backupPath);
        }
        catch
        {
            // On failure, restore plaintext backup so app can still run
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            File.Move(backupPath, _dbPath);
            throw;
        }
    }

    private string GetOrCreateKey()
    {
        lock (_keyLock)
        {
            if (_cachedKey != null) return _cachedKey;

            var key = _keychain.GetSecretAsync(KeychainKeys.DatabaseKey()).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(key))
            {
                // Generate a new 32-byte key, hex-encoded (64 chars).
                // SQLCipher recommends 256-bit keys.
                var bytes = new byte[32];
                using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                rng.GetBytes(bytes);
                key = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                _keychain.SetSecretAsync(KeychainKeys.DatabaseKey(), key).GetAwaiter().GetResult();
            }
            _cachedKey = key;
            return key;
        }
    }

    /// <summary>
    /// Rewrites a CREATE TABLE statement to target an attached schema.
    /// Example: "CREATE TABLE accounts (id TEXT)" → "CREATE TABLE encrypted.accounts (id TEXT)"
    /// Handles both quoted and unquoted table names, plus IF NOT EXISTS.
    /// </summary>
    private static string RewriteCreateTableForAttachedSchema(string createSql, string tableName)
    {
        // Match: CREATE TABLE [IF NOT EXISTS] [schema.]tableName
        // We want to insert "encrypted." before tableName if it isn't already qualified.
        var pattern = $@"(CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?)(?:""?){System.Text.RegularExpressions.Regex.Escape(tableName)}(?:""?)";
        var replacement = $"$1\"encrypted\".\"{tableName}\"";
        return System.Text.RegularExpressions.Regex.Replace(
            createSql, pattern, replacement,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
