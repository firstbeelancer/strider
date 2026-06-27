using Strider.Core.Abstractions;
using Strider.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for EncryptedSqliteConnectionFactory — verifies SQLCipher at-rest
/// encryption of the SQLite database (F-009 fix).
/// </summary>
public class EncryptedSqliteConnectionFactoryTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly InMemoryKeychain _keychain = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"strider_test_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void GetConnectionString_WithEncryptionDisabled_ReturnsPlainConnectionString()
    {
        var factory = new EncryptedSqliteConnectionFactory(
            _keychain, TempDbPath(), encryptionEnabled: false);

        var connStr = factory.GetConnectionString();
        connStr.Should().StartWith("Data Source=");
        connStr.Should().NotContain("Password=");
    }

    [Fact]
    public void GetConnectionString_WithEncryption_GeneratesAndStoresKeyInKeychain()
    {
        var path = TempDbPath();
        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        var connStr = factory.GetConnectionString();

        connStr.Should().StartWith("Data Source=");
        connStr.Should().Contain("Password=");
        // Key was stored in keychain
        _keychain.HasSecretAsync(KeychainKeys.DatabaseKey()).Result.Should().BeTrue();
    }

    [Fact]
    public void GetConnectionString_ReusesSameKeyOnSubsequentCalls()
    {
        var path = TempDbPath();
        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        var connStr1 = factory.GetConnectionString();
        var connStr2 = factory.GetConnectionString();

        connStr1.Should().Be(connStr2, "key should be cached, not regenerated");
    }

    [Fact]
    public async Task OpenConnectionAsync_WithEncryption_CreatesEncryptedDatabase()
    {
        var path = TempDbPath();
        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        await using var conn = await factory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, value TEXT); INSERT INTO test VALUES (1, 'hello');";
        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();

        // File exists
        File.Exists(path).Should().BeTrue();

        // File is encrypted — first 16 bytes should NOT be "SQLite format 3"
        var header = new byte[16];
        using (var fs = File.OpenRead(path))
        {
            await fs.ReadAsync(header, 0, 16);
        }
        var magic = System.Text.Encoding.ASCII.GetString(header, 0, 15);
        magic.Should().NotBe("SQLite format 3", "SQLCipher file should not start with plaintext magic");
    }

    [Fact]
    public async Task OpenConnectionAsync_WithEncryption_CanReadAndWriteData()
    {
        var path = TempDbPath();
        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        // Write
        await using (var conn = await factory.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, value TEXT); INSERT INTO test VALUES (1, 'encrypted hello');";
            await cmd.ExecuteNonQueryAsync();
        }

        // Read back
        await using (var conn = await factory.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM test WHERE id = 1";
            var value = (string)(await cmd.ExecuteScalarAsync())!;
            value.Should().Be("encrypted hello");
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_WithoutKey_CannotReadEncryptedDatabase()
    {
        var path = TempDbPath();
        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        // Create encrypted DB
        await using (var conn = await factory.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE secret (data TEXT); INSERT INTO secret VALUES ('classified');";
            await cmd.ExecuteNonQueryAsync();
        }

        // Try to open WITHOUT the password — should fail or return garbled data
        var plainConnStr = $"Data Source={path}";
        await using var plainConn = new SqliteConnection(plainConnStr);
        var act = async () =>
        {
            await plainConn.OpenAsync();
            await using var cmd = plainConn.CreateCommand();
            cmd.CommandText = "SELECT data FROM secret";
            await cmd.ExecuteScalarAsync();
        };
        await act.Should().ThrowAsync<SqliteException>(
            "encrypted DB cannot be read without the SQLCipher key");
    }

    [Fact]
    public void IsLegacyPlaintextDatabase_ReturnsFalseForNonExistentFile()
    {
        var factory = new EncryptedSqliteConnectionFactory(
            _keychain, Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.db"),
            encryptionEnabled: true);
        factory.IsLegacyPlaintextDatabase().Should().BeFalse();
    }

    [Fact]
    public async Task IsLegacyPlaintextDatabase_ReturnsTrueForSqliteFile()
    {
        var path = TempDbPath();
        // Create a plaintext SQLite database
        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE legacy (x INTEGER)";
        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();

        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);
        factory.IsLegacyPlaintextDatabase().Should().BeTrue();
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_ConvertsPlaintextToEncryptedPreservingData()
    {
        var path = TempDbPath();
        // Create plaintext DB with data
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE accounts (id TEXT PRIMARY KEY, email TEXT);
                INSERT INTO accounts VALUES ('abc-123', 'alice@example.com');
                INSERT INTO accounts VALUES ('def-456', 'bob@example.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        // Verify it's plaintext before migration
        factory.IsLegacyPlaintextDatabase().Should().BeTrue();

        // Migrate
        await factory.MigrateToEncryptedAsync();

        // After migration, file is no longer plaintext
        factory.IsLegacyPlaintextDatabase().Should().BeFalse();

        // Data is preserved
        await using (var conn = await factory.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM accounts";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(2);
        }
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_OnAlreadyEncryptedDb_IsNoOp()
    {
        var path = TempDbPath();
        var factory = new EncryptedSqliteConnectionFactory(_keychain, path, encryptionEnabled: true);

        // Create an encrypted DB from the start
        await using (var conn = await factory.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE test (id INTEGER)";
            await cmd.ExecuteNonQueryAsync();
        }

        // Migrate should be a no-op (file is already encrypted)
        var act = async () => await factory.MigrateToEncryptedAsync();
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// In-memory IKeychainService implementation for tests.
    /// </summary>
    private sealed class InMemoryKeychain : IKeychainService
    {
        private readonly Dictionary<string, string> _store = new();

        public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        }

        public Task DeleteSecretAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSecretAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_store.ContainsKey(key));
        }
    }
}
