using FluentAssertions;
using Microsoft.Data.Sqlite;
using Strider.Core.Domain;
using Strider.Core.Search;
using Strider.Infrastructure.Persistence;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Structured search against a real SQLite database (F2.8): field filters,
/// flags, body matching and LIKE-wildcard escaping.
/// </summary>
public class SqliteMessageSearchTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteMessageStore _store;
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _inboxId = Guid.NewGuid();
    private readonly Guid _sentId = Guid.NewGuid();

    public SqliteMessageSearchTests()
    {
        var connStr = $"Data Source=file:search{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connStr);
        _keepAlive.Open();

        new DatabaseInitializer(connStr).InitializeAsync().GetAwaiter().GetResult();
        _store = new SqliteMessageStore(connStr);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        var messages = new[]
        {
            NewMessage(1, _inboxId, "Alice Smith", "alice@example.com", "Quarterly report Q3",
                read: false, attachment: true, body: "Please review the payment terms before Friday."),
            NewMessage(2, _inboxId, "Bob Jones", "bob@example.com", "Lunch tomorrow?",
                read: true, attachment: false, body: "How about 13:00 at the usual place?"),
            NewMessage(3, _sentId, "Me", "me@example.com", "Re: Quarterly report Q3",
                read: true, attachment: false, body: "Numbers look fine, 100% growth."),
            NewMessage(4, _inboxId, "Newsletter", "news@example.com", "Weekly digest 50% off",
                read: true, attachment: false, body: "Unsubscribe any time."),
        };

        await _store.SaveMessagesAsync(messages);

        foreach (var message in messages)
        {
            await _store.SaveBodyAsync(new MessageBody
            {
                MessageId = message.Id,
                TextPlain = Seed[message.MessageUid - 1].Body,
            });
        }
    }

    private static readonly (string From, string Subject, string Body)[] Seed =
    {
        ("alice@example.com", "Quarterly report Q3", "Please review the payment terms before Friday."),
        ("bob@example.com", "Lunch tomorrow?", "How about 13:00 at the usual place?"),
        ("me@example.com", "Re: Quarterly report Q3", "Numbers look fine, 100% growth."),
        ("news@example.com", "Weekly digest 50% off", "Unsubscribe any time."),
    };

    private Message NewMessage(
        int uid, Guid folderId, string fromName, string fromAddress, string subject,
        bool read, bool attachment, string body)
    {
        _ = body;
        return new Message
        {
            Id = Guid.NewGuid(),
            AccountId = _accountId,
            FolderId = folderId,
            MessageUid = uid,
            MessageId = $"<m{uid}@example.com>",
            FromAddress = fromAddress,
            FromName = fromName,
            ToAddresses = "[\"me@example.com\"]",
            Subject = subject,
            DateUtc = DateTime.UtcNow.AddMinutes(-uid),
            IsRead = read,
            HasAttachments = attachment,
        };
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task Search_ByFrom_MatchesAddressAndDisplayName()
    {
        var byAddress = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("from:alice"));
        byAddress.Should().ContainSingle().Which.MessageUid.Should().Be(1);

        var byName = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("from:Jones"));
        byName.Should().ContainSingle().Which.MessageUid.Should().Be(2);
    }

    [Fact]
    public async Task Search_BySubject_IsCaseInsensitive()
    {
        var result = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("subject:quarterly"));

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_ByBody_FindsPhraseInsideCachedBody()
    {
        var result = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("body:\"payment terms\""));

        result.Should().ContainSingle().Which.MessageUid.Should().Be(1);
    }

    [Fact]
    public async Task Search_FreeText_SearchesSubjectAndBody()
    {
        var result = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("unsubscribe"));

        result.Should().ContainSingle().Which.MessageUid.Should().Be(4);
    }

    [Fact]
    public async Task Search_IsUnread_FiltersReadMessages()
    {
        var result = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("is:unread"));

        result.Should().ContainSingle().Which.MessageUid.Should().Be(1);
    }

    [Fact]
    public async Task Search_HasAttachment_Filters()
    {
        var result = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("has:attachment"));

        result.Should().ContainSingle().Which.MessageUid.Should().Be(1);
    }

    [Fact]
    public async Task Search_ScopedToFolder_ExcludesOtherFolders()
    {
        var inbox = await _store.SearchMessagesAsync(_accountId, _inboxId, MessageQuery.Parse("subject:quarterly"));
        var all = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("subject:quarterly"));

        inbox.Should().ContainSingle().Which.FolderId.Should().Be(_inboxId);
        all.Should().HaveCount(2, "the Sent copy carries the same subject");
    }

    [Fact]
    public async Task Search_PercentSign_IsTreatedLiterally()
    {
        // "50%" must not behave like a wildcard, and "100%" must still match.
        var fifty = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("subject:\"50%\""));
        var hundred = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("body:\"100%\""));

        fifty.Should().ContainSingle().Which.MessageUid.Should().Be(4);
        hundred.Should().ContainSingle().Which.MessageUid.Should().Be(3);
    }

    [Fact]
    public async Task Search_SeveralFields_AreAnded()
    {
        var result = await _store.SearchMessagesAsync(
            _accountId, null, MessageQuery.Parse("from:alice subject:quarterly has:attachment"));

        result.Should().ContainSingle().Which.MessageUid.Should().Be(1);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmpty()
    {
        var result = await _store.SearchMessagesAsync(_accountId, null, MessageQuery.Parse("subject:nothinghere"));

        result.Should().BeEmpty();
    }
}
