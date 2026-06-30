using Strider.Core.Domain;
using Strider.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Integration tests for SqliteMessageStore — verifies batch insert
/// performance and correctness (F-012 fix).
///
/// These tests use a real in-memory SQLite database (no mocking) to verify
/// that Dapper batch insert works correctly with our schema.
/// </summary>
public class SqliteMessageStoreBatchTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteMessageStore _store;

    public SqliteMessageStoreBatchTests()
    {
        // Use a unique in-memory DB per test instance, kept alive via _keepAlive
        var connStr = $"Data Source=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connStr);
        _keepAlive.Open();

        // Initialize schema
        var initializer = new DatabaseInitializer(connStr);
        initializer.InitializeAsync().GetAwaiter().GetResult();

        _store = new SqliteMessageStore(connStr);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task SaveMessagesAsync_BatchInsert_PersistsAllMessages()
    {
        // Arrange — 50 messages in a single batch
        var accountId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var messages = Enumerable.Range(1, 50).Select(i => new Message
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FolderId = folderId,
            MessageUid = i,
            MessageId = $"<msg-{i}@example.com>",
            FromAddress = "sender@example.com",
            FromName = "Sender",
            ToAddresses = "[]",
            Subject = $"Test message {i}",
            DateUtc = DateTime.UtcNow.AddMinutes(-i),
            FetchedAt = DateTime.UtcNow,
        }).ToList();

        // Act
        await _store.SaveMessagesAsync(messages);

        // Assert — all 50 should be in the DB
        var stored = await _store.GetMessagesAsync(folderId, limit: 100);
        stored.Should().HaveCount(50);
        // Verify all UIDs are present (order may vary due to date_utc sorting)
        stored.Select(m => m.MessageUid).Should().BeEquivalentTo(Enumerable.Range(1, 50));
    }

    [Fact]
    public async Task SaveMessagesAsync_BatchInsert_WithDuplicateUids_PerformsUpsert()
    {
        // Arrange — Dapper + ON CONFLICT DO UPDATE means duplicates are upserted,
        // not rejected. This test verifies that behavior: a batch with a duplicate
        // (account_id, folder_id, message_uid) tuple does not create a second row,
        // but updates the existing one.
        var accountId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { Id = Guid.NewGuid(), AccountId = accountId, FolderId = folderId, MessageUid = 1, FromAddress = "a@x", ToAddresses = "[]", Subject = "S1", FetchedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), AccountId = accountId, FolderId = folderId, MessageUid = 2, FromAddress = "b@x", ToAddresses = "[]", Subject = "S2", FetchedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), AccountId = accountId, FolderId = folderId, MessageUid = 3, FromAddress = "c@x", ToAddresses = "[]", Subject = "S3", FetchedAt = DateTime.UtcNow },
            // Same (account_id, folder_id, message_uid) as message 1 — should upsert, not duplicate
            new() { Id = Guid.NewGuid(), AccountId = accountId, FolderId = folderId, MessageUid = 1, FromAddress = "d@x", ToAddresses = "[]", Subject = "DUP", FetchedAt = DateTime.UtcNow },
        };

        // Act
        await _store.SaveMessagesAsync(messages);

        // Assert — exactly 3 rows (UID 1 was upserted, not duplicated)
        var stored = await _store.GetMessagesAsync(folderId, limit: 100);
        stored.Should().HaveCount(3, "duplicate (account_id, folder_id, message_uid) should upsert, not duplicate");

        // The upserted row should have the latest subject
        var uid1 = stored.Single(m => m.MessageUid == 1);
        uid1.Subject.Should().Be("DUP", "upsert should update the existing row");
    }

    [Fact]
    public async Task SaveMessagesAsync_Upsert_UpdatesExistingOnConflict()
    {
        // Arrange — insert message with UID 1
        var accountId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var message = new Message
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FolderId = folderId,
            MessageUid = 1,
            FromAddress = "original@example.com",
            ToAddresses = "[]",
            Subject = "Original subject",
            IsRead = false,
            FetchedAt = DateTime.UtcNow,
        };
        await _store.SaveMessageAsync(message);

        // Act — upsert with same account/folder/UID but new subject + IsRead=true
        var updated = new Message
        {
            Id = message.Id,
            AccountId = accountId,
            FolderId = folderId,
            MessageUid = 1,
            FromAddress = "original@example.com",
            ToAddresses = "[]",
            Subject = "Updated subject",
            IsRead = true,
            FetchedAt = DateTime.UtcNow,
        };
        await _store.SaveMessagesAsync(new[] { updated });

        // Assert — only 1 message, with updated values
        var stored = await _store.GetMessagesAsync(folderId, limit: 100);
        stored.Should().HaveCount(1);
        stored[0].Subject.Should().Be("Updated subject");
    }

    [Fact]
    public async Task SaveMessagesAsync_EmptyList_IsNoOp()
    {
        var act = async () => await _store.SaveMessagesAsync(Array.Empty<Message>());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveMessagesAsync_LargeBatch_Handles500Messages()
    {
        // Arrange — 500 messages (typical initial sync per spec)
        var accountId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var messages = Enumerable.Range(1, 500).Select(i => new Message
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FolderId = folderId,
            MessageUid = i,
            FromAddress = "sender@example.com",
            ToAddresses = "[]",
            Subject = $"Message {i}",
            FetchedAt = DateTime.UtcNow,
        }).ToList();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _store.SaveMessagesAsync(messages);
        sw.Stop();

        // Assert — all 500 persisted
        var stored = await _store.GetMessagesAsync(folderId, limit: 1000);
        stored.Should().HaveCount(500);

        // Performance assertion — should complete in well under 5 seconds
        // (the original per-message implementation took 10-20s for 500 messages)
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "batch insert in single transaction must be fast");
    }
}
