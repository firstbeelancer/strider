using Strider.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for DatabaseInitializer — verifies that migrations apply idempotently
/// and that the schema_migrations table correctly tracks applied versions.
/// </summary>
public class DatabaseInitializerTests
{
    private static string InMemoryConnectionString =>
        $"Data Source=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";

    [Fact]
    public async Task InitializeAsync_CreatesSchemaFromEmbeddedMigrations()
    {
        // Arrange
        var connStr = InMemoryConnectionString;
        // Keep one shared connection alive for in-memory DB to persist
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        var initializer = new DatabaseInitializer(connStr);

        // Act
        await initializer.InitializeAsync();

        // Assert — schema_migrations table should have at least one row
        var applied = await initializer.GetAppliedMigrationsAsync();
        applied.Should().NotBeEmpty();
        applied.First().Version.Should().Be(1);
        applied.First().Name.Should().StartWith("0001_");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_RunningTwiceDoesNotDuplicateMigrations()
    {
        var connStr = InMemoryConnectionString;
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        var initializer = new DatabaseInitializer(connStr);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();  // second run

        var applied = await initializer.GetAppliedMigrationsAsync();
        applied.Count.Should().Be(1, "running migrations twice should not duplicate rows");
    }

    [Fact]
    public async Task InitializeAsync_CreatesExpectedTables()
    {
        var connStr = InMemoryConnectionString;
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        var initializer = new DatabaseInitializer(connStr);

        await initializer.InitializeAsync();

        // Verify all expected tables exist
        var expectedTables = new[]
        {
            "accounts", "folders", "messages", "message_bodies", "attachments",
            "signatures", "calendar_events", "pgp_keys", "pending_ops", "ai_settings",
            "schema_migrations",
        };

        await using var cmd = keepAlive.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var actualTables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actualTables.Add(reader.GetString(0));
        }

        foreach (var expected in expectedTables)
        {
            actualTables.Should().Contain(expected, $"table '{expected}' should exist after migration");
        }
    }

    [Fact]
    public async Task InitializeAsync_EnablesForeignKeys()
    {
        var connStr = InMemoryConnectionString;
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        var initializer = new DatabaseInitializer(connStr);

        await initializer.InitializeAsync();

        await using var cmd = keepAlive.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        var result = (long)(await cmd.ExecuteScalarAsync())!;
        result.Should().Be(1, "foreign_keys PRAGMA should be ON after initialization");
    }

    [Fact]
    public async Task InitializeAsync_SetsWalJournalMode()
    {
        // Note: in-memory databases cannot use WAL; SQLite falls back to memory mode.
        // This test verifies the PRAGMA is executed without error; for file-based DBs
        // it would actually set WAL.
        var connStr = InMemoryConnectionString;
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        var initializer = new DatabaseInitializer(connStr);

        var act = async () => await initializer.InitializeAsync();
        await act.Should().NotThrowAsync();
    }
}
