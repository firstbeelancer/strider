using FluentAssertions;
using Microsoft.Data.Sqlite;
using MimeKit;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;
using Strider.Infrastructure.Persistence;
using Strider.Infrastructure.Security;
using Strider.Infrastructure.Services;

namespace Strider.Infrastructure.Tests;

public class MimeMessageFactoryTests
{
    [Fact]
    public void Build_SetsRecipientsSubjectAndThreadingHeaders()
    {
        var mime = MimeMessageFactory.Build(new OutgoingMessage
        {
            From = "me@mine.com",
            FromName = "Me",
            To = { "\"Alice\" <alice@x.com>" },
            Cc = { "bob@y.com" },
            Bcc = { "secret@z.com" },
            Subject = "Re: Lunch",
            BodyPlain = "Sure",
            InReplyTo = "<orig@x.com>",
            References = { "root@x.com", "orig@x.com" },
        });

        mime.From.Mailboxes.Single().Address.Should().Be("me@mine.com");
        mime.To.Mailboxes.Single().Address.Should().Be("alice@x.com");
        mime.Cc.Mailboxes.Single().Address.Should().Be("bob@y.com");
        mime.Bcc.Mailboxes.Single().Address.Should().Be("secret@z.com");
        mime.Subject.Should().Be("Re: Lunch");
        mime.InReplyTo.Should().Be("orig@x.com");
        mime.References.Should().Equal("root@x.com", "orig@x.com");
        mime.MessageId.Should().EndWith("mine.com");
        mime.TextBody.Should().Be("Sure");
    }

    [Fact]
    public void Build_WithoutRecipients_Throws()
    {
        var act = () => MimeMessageFactory.Build(new OutgoingMessage { From = "me@mine.com" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_InvalidAddress_ThrowsFormatException()
    {
        var act = () => MimeMessageFactory.Build(new OutgoingMessage { From = "me@mine.com", To = { "not an address <<" } });
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToBytes_RoundTrips()
    {
        var mime = MimeMessageFactory.Build(new OutgoingMessage { From = "me@mine.com", To = { "a@x.com" }, Subject = "Привет" });
        var parsed = MimeMessage.Load(new MemoryStream(MimeMessageFactory.ToBytes(mime)));
        parsed.Subject.Should().Be("Привет");
    }
}

public class HtmlTextTests
{
    [Fact]
    public void ToPlainText_KeepsParagraphBreaks_DropsMarkup()
    {
        var text = HtmlText.ToPlainText("<p>Hello <b>world</b></p><p>Second</p><ul><li>one</li></ul>");
        text.Should().Be("Hello world\n\nSecond\n\n• one");
    }

    [Fact]
    public void ToPlainText_ShowsLinkTargets_SkipsStyle()
    {
        HtmlText.ToPlainText("<style>p{}</style><a href=\"https://x.com\">site</a>")
            .Should().Be("site (https://x.com)");
    }
}

/// <summary>
/// Sync and actions against real SQLite stores and a scripted fake IMAP server.
/// </summary>
public class MailLoopTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteAccountStore _accounts;
    private readonly SqliteMessageStore _messages;
    private readonly FakeImap _imap = new();
    private readonly FakeSmtp _smtp = new();
    private readonly Account _account;
    private readonly Folder _inbox, _archive, _trash, _sent;

    public MailLoopTests()
    {
        var cs = $"Data Source=file:loop{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();
        new DatabaseInitializer(cs).InitializeAsync().GetAwaiter().GetResult();
        _accounts = new SqliteAccountStore(cs);
        _messages = new SqliteMessageStore(cs);

        _account = new Account { Email = "me@mine.com", DisplayName = "Me", ImapHost = "imap.mine.com", SmtpHost = "smtp.mine.com" };
        _accounts.SaveAccountAsync(_account).GetAwaiter().GetResult();
        _inbox = new Folder { AccountId = _account.Id, RemoteName = "INBOX", Type = FolderType.Inbox };
        _archive = new Folder { AccountId = _account.Id, RemoteName = "Archive", Type = FolderType.Archive };
        _trash = new Folder { AccountId = _account.Id, RemoteName = "Trash", Type = FolderType.Trash };
        _sent = new Folder { AccountId = _account.Id, RemoteName = "Sent", Type = FolderType.Sent };
        _accounts.SaveFoldersAsync(new[] { _inbox, _archive, _trash, _sent }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _keepAlive.Dispose();
    }

    private ImapSessionRunner Runner() => new(new FakeFactory(_imap));
    private MailSyncService Sync() => new(_accounts, _messages, Runner(), NoFlags());

    /// <summary>Sync tests do not exercise flag rules; the real service is covered in FlagServiceTests.</summary>
    private static IFlagService NoFlags() => new NullFlagService();

    private sealed class NullFlagService : IFlagService
    {
        public Task<int> ApplyRulesAsync(IReadOnlyList<Message> messages, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<int> ApplyRulesToAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task SetFlagAsync(Message message, FlagColor color, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
    private MailActionService Actions() => new(_accounts, _messages, Runner(), () => _smtp);

    private static Message Remote(int uid, bool read = false) => new()
    {
        MessageUid = uid,
        MessageId = $"m{uid}@x.com",
        FromAddress = "alice@x.com",
        Subject = $"Hello {uid}",
        DateUtc = DateTime.UtcNow.AddMinutes(uid),
        IsRead = read,
    };

    [Fact]
    public async Task SyncAccount_FirstRun_StoresInboxAndAddsNewFolders()
    {
        _imap.Folders.Add(new Folder { RemoteName = "INBOX" });
        _imap.Folders.Add(new Folder { RemoteName = "Projects" });
        _imap.Mail["INBOX"] = new() { Remote(1), Remote(2, read: true), Remote(3) };

        var added = await Sync().SyncAccountAsync(_account.Id);

        added.Should().Be(3);
        (await _messages.GetMessagesAsync(_inbox.Id)).Should().HaveCount(3);
        var folders = await _accounts.GetFoldersAsync(_account.Id);
        folders.Should().Contain(f => f.RemoteName == "Projects");
        folders.Count(f => f.RemoteName == "INBOX").Should().Be(1, "known folders are not duplicated");
        var inbox = await _accounts.GetFolderAsync(_inbox.Id);
        inbox!.LastSyncUid.Should().Be(3);
        inbox.UnreadCount.Should().Be(2);
        _imap.Connects.Should().Be(1);
    }

    [Fact]
    public async Task SyncAccount_SecondRun_FetchesOnlyNewerUids()
    {
        _imap.Mail["INBOX"] = new() { Remote(1), Remote(2) };
        await Sync().SyncAccountAsync(_account.Id);
        _imap.Mail["INBOX"].Add(Remote(3));

        var added = await Sync().SyncAccountAsync(_account.Id);

        added.Should().Be(1);
        _imap.LastAfterUid.Should().Be(2);
        (await _messages.GetMessagesAsync(_inbox.Id)).Should().HaveCount(3);
    }

    [Fact]
    public async Task EnsureBody_DownloadsOnce_ThenUsesCache()
    {
        _imap.Mail["INBOX"] = new() { Remote(7) };
        await Sync().SyncAccountAsync(_account.Id);
        var msg = (await _messages.GetMessagesAsync(_inbox.Id)).Single();

        var first = await Sync().EnsureBodyAsync(msg);
        var second = await Sync().EnsureBodyAsync(msg);

        first!.TextPlain.Should().Be("body 7");
        second!.TextPlain.Should().Be("body 7");
        _imap.BodyFetches.Should().Be(1);
    }

    [Fact]
    public async Task Archive_MovesOnServer_AndRemovesLocally()
    {
        _imap.Mail["INBOX"] = new() { Remote(5) };
        await Sync().SyncAccountAsync(_account.Id);
        var msg = (await _messages.GetMessagesAsync(_inbox.Id)).Single();

        await Actions().ArchiveAsync(msg);

        _imap.Moves.Should().Equal(("INBOX", 5, "Archive"));
        (await _messages.GetMessageAsync(msg.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Trash_FromInbox_Moves_FromTrash_DeletesForGood()
    {
        var inInbox = new Message { AccountId = _account.Id, FolderId = _inbox.Id, MessageUid = 1, Subject = "a" };
        var inTrash = new Message { AccountId = _account.Id, FolderId = _trash.Id, MessageUid = 2, Subject = "b" };
        await _messages.SaveMessagesAsync(new[] { inInbox, inTrash });

        await Actions().TrashAsync(inInbox);
        await Actions().TrashAsync(inTrash);

        _imap.Moves.Should().Equal(("INBOX", 1, "Trash"));
        _imap.Deletes.Should().Equal(("Trash", 2));
    }

    [Fact]
    public async Task Archive_WithoutArchiveFolder_ThrowsAndKeepsMessage()
    {
        await _accounts.DeleteFolderAsync(_archive.Id);
        var msg = new Message { AccountId = _account.Id, FolderId = _inbox.Id, MessageUid = 9, Subject = "x" };
        await _messages.SaveMessageAsync(msg);

        var act = () => Actions().ArchiveAsync(msg);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _messages.GetMessageAsync(msg.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Send_SendsViaSmtp_AndFilesCopyInSent()
    {
        await Actions().SendAsync(_account.Id, new OutgoingMessage { To = { "a@x.com" }, Subject = "Hi" });

        _smtp.Sent.Should().ContainSingle().Which.From.Should().Be("me@mine.com");
        _smtp.Disconnected.Should().BeTrue();
        _imap.Appends.Should().ContainSingle().Which.Should().Be("Sent");
    }

    [Fact]
    public async Task Send_Gmail_DoesNotDuplicateSentCopy()
    {
        _account.SmtpHost = "smtp.gmail.com";
        await _accounts.UpdateAccountAsync(_account);

        await Actions().SendAsync(_account.Id, new OutgoingMessage { To = { "a@x.com" } });

        _imap.Appends.Should().BeEmpty();
    }

    [Fact]
    public async Task Attachments_ListedOnce_DownloadedByFileName()
    {
        var msg = new Message { AccountId = _account.Id, FolderId = _inbox.Id, MessageUid = 4, Subject = "a", HasAttachments = true };
        await _messages.SaveMessageAsync(msg);

        var first = await Sync().EnsureAttachmentsAsync(msg);
        var second = await Sync().EnsureAttachmentsAsync(msg);
        var bytes = await Sync().DownloadAttachmentAsync(msg, second.Single());

        first.Should().ContainSingle().Which.Filename.Should().Be("report.pdf");
        second.Should().ContainSingle();
        _imap.AttachmentListFetches.Should().Be(1);
        _imap.LastDownloadKey.Should().Be("report.pdf");
        bytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task SaveDraft_AppendsWithDraftFlag_AndReplacesPrevious()
    {
        var drafts = new Folder { AccountId = _account.Id, RemoteName = "Drafts", Type = FolderType.Drafts };
        await _accounts.SaveFolderAsync(drafts);

        var uid1 = await Actions().SaveDraftAsync(_account.Id, new OutgoingMessage { Subject = "wip" }, null);
        var uid2 = await Actions().SaveDraftAsync(_account.Id, new OutgoingMessage { Subject = "wip 2" }, uid1);

        _imap.Appends.Should().Equal("Drafts", "Drafts");
        _imap.AppendDraftFlags.Should().AllBeEquivalentTo(true);
        _imap.Deletes.Should().Equal(("Drafts", uid1!.Value));
        uid2.Should().NotBe(uid1);
    }

    [Fact]
    public void MergeFolders_ReturnsOnlyUnknown_ClassifiesCustom()
    {
        var local = new[] { new Folder { RemoteName = "INBOX", Type = FolderType.Inbox } };
        var remote = new[] { new Folder { RemoteName = "INBOX" }, new Folder { RemoteName = "[Gmail]/Sent Mail" } };

        var added = MailSyncService.MergeFolders(Guid.NewGuid(), local, remote);

        added.Should().ContainSingle().Which.Type.Should().Be(FolderType.Sent);
    }

    // ---- fakes ----

    private sealed class FakeFactory : IImapGatewayFactory
    {
        private readonly FakeImap _gw;
        public FakeFactory(FakeImap gw) => _gw = gw;
        public IImapGateway ForAccount(Guid accountId) => _gw;
        public void Release(Guid accountId) { }
    }

    private sealed class FakeSmtp : ISmtpGateway
    {
        public List<OutgoingMessage> Sent { get; } = new();
        public bool Disconnected { get; private set; }
        public Task ConnectAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) { Disconnected = true; return Task.CompletedTask; }
        public Task<byte[]> SendAsync(Account account, OutgoingMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(MimeMessageFactory.ToBytes(MimeMessageFactory.Build(message)));
        }
        public Task SendAsync(Account a, string f, IReadOnlyList<string> t, IReadOnlyList<string>? c, IReadOnlyList<string>? b,
            string s, string h, string? p, IReadOnlyList<AttachmentData>? at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendEncryptedAsync(Account a, string f, IReadOnlyList<string> t, string s, string h, string? p,
            IReadOnlyList<string>? k, string? pk, string? pp, IReadOnlyList<AttachmentData>? at, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeImap : IImapGateway
    {
        private bool _connected;
        public int Connects, BodyFetches, LastAfterUid = -1;
        public List<Folder> Folders { get; } = new();
        public Dictionary<string, List<Message>> Mail { get; } = new();
        public List<(string, int, string)> Moves { get; } = new();
        public List<(string, int)> Deletes { get; } = new();
        public List<string> Appends { get; } = new();

        public Task ConnectAsync(Account account, CancellationToken ct = default) { Connects++; _connected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken ct = default) { _connected = false; return Task.CompletedTask; }
        public Task<bool> IsConnectedAsync(CancellationToken ct = default) => Task.FromResult(_connected);
        public Task<IReadOnlyList<Folder>> GetFoldersAsync(Account account, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Folder>>(Folders.ToList());

        public Task<IReadOnlyList<Message>> FetchNewMessagesAsync(Account account, string folderName, int afterUid, int maxCount, CancellationToken ct = default)
        {
            LastAfterUid = afterUid;
            var list = Mail.TryGetValue(folderName, out var l) ? l : new List<Message>();
            // Fresh copies, as a real server would return.
            IReadOnlyList<Message> result = list.Where(m => m.MessageUid > afterUid).Select(m => new Message
            {
                MessageUid = m.MessageUid, MessageId = m.MessageId, FromAddress = m.FromAddress,
                Subject = m.Subject, DateUtc = m.DateUtc, IsRead = m.IsRead,
            }).ToList();
            return Task.FromResult(result);
        }

        public Task<MessageBody> FetchBodyAsync(Account account, string folderName, int messageUid, CancellationToken ct = default)
        {
            BodyFetches++;
            return Task.FromResult(new MessageBody { TextPlain = $"body {messageUid}" });
        }

        public Task MoveMessageAsync(Account account, string fromFolder, int messageUid, string toFolder, CancellationToken ct = default)
        { Moves.Add((fromFolder, messageUid, toFolder)); return Task.CompletedTask; }
        public Task DeleteMessageAsync(Account account, string folderName, int messageUid, CancellationToken ct = default)
        { Deletes.Add((folderName, messageUid)); return Task.CompletedTask; }
        public int NextUid = 100;
        public List<bool> AppendDraftFlags { get; } = new();
        public Task<int?> AppendMessageAsync(Account account, string folderName, byte[] rawMime, bool seen, bool draft = false, CancellationToken ct = default)
        { Appends.Add(folderName); AppendDraftFlags.Add(draft); return Task.FromResult<int?>(NextUid++); }

        public Task<IReadOnlyList<Message>> FetchMessagesAsync(Account a, string f, int from, int to, CancellationToken ct = default) => throw new NotSupportedException();
        public int AttachmentListFetches;
        public Task<IReadOnlyList<Attachment>> FetchAttachmentsAsync(Account a, string f, int uid, CancellationToken ct = default)
        {
            AttachmentListFetches++;
            return Task.FromResult<IReadOnlyList<Attachment>>(new List<Attachment> { new() { Filename = "report.pdf", ContentType = "application/pdf", Size = 3 } });
        }
        public string? LastDownloadKey;
        public Task<Stream> DownloadAttachmentAsync(Account a, string f, int uid, string key, CancellationToken ct = default)
        { LastDownloadKey = key; return Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })); }
        public Task MarkAsReadAsync(Account a, string f, int uid, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkAsUnreadAsync(Account a, string f, int uid, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddFlagAsync(Account a, string f, int uid, string flag, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFlagAsync(Account a, string f, int uid, string flag, CancellationToken ct = default) => Task.CompletedTask;
        public Task IdleAsync(Account a, string f, Action onNew, CancellationToken ct = default) => Task.CompletedTask;
    }
}
