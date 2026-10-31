using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Security;
using Strider.UI;
using Strider.UI.ViewModels;
using Strider.UI.Views;

namespace Strider.UI.Tests;

/// <summary>
/// Wave 7 regressions, each one a bug a user could hit: duplicated signature, lost quote,
/// a list refresh closing the open message, a slow load overwriting the next one.
/// </summary>
public class Wave7FeaturesTests
{
    private static IServiceProvider Services => ((App)Application.Current!).Services;
    private static IMessageStore Store => Services.GetRequiredService<IMessageStore>();

    private static ComposeViewModel Composer(IAiAssistant? ai = null, IMailActionService? actions = null) => new(
        Store,
        Services.GetRequiredService<IAccountStore>(),
        actions ?? new FakeActions(),
        ai ?? Services.GetRequiredService<IAiAssistant>(),
        Services.GetRequiredService<ISignatureStore>(),
        Services.GetRequiredService<IAppSettingsStore>(),
        Services.GetRequiredService<IFontLibrary>());

    private static async Task<(Guid Folder, List<Message> Rows)> SeedAsync(int count)
    {
        var folder = Guid.NewGuid();
        var account = Guid.NewGuid();
        var rows = Enumerable.Range(1, count).Select(i => new Message
        {
            AccountId = account,
            FolderId = folder,
            MessageUid = i,
            Subject = $"Mail {i}",
            FromAddress = "a@x.com",
            DateUtc = DateTime.UtcNow.AddMinutes(i),
            Size = i,
        }).ToList();
        await Store.SaveMessagesAsync(rows);
        return (folder, rows);
    }

    // === Signature ===

    [AvaloniaFact]
    public void Signature_ReachesTheMessageExactlyOnce()
    {
        var vm = Composer();
        vm.BodyPlain = "Hello";
        vm.SelectedSignature = new Signature { Name = "Work", ContentPlain = "Anna Petrova" };

        var occurrences = vm.BuildBodyHtml().Split("Anna Petrova").Length - 1;

        occurrences.Should().Be(1);
        vm.BodyPlain.Should().Be("Hello", "the signature is not part of the edited text");
    }

    [AvaloniaFact]
    public void Signature_CanBeLeftOut()
    {
        var vm = Composer();
        vm.BodyPlain = "Hello";
        vm.SelectedSignature = new Signature { Name = "Work", ContentPlain = "Anna Petrova" };

        vm.IncludeSignature = false;

        vm.BuildBodyHtml().Should().NotContain("Anna Petrova");
    }

    // === Formatting survives typing ===

    [AvaloniaFact]
    public void Formatting_FollowsTheWordWhenTextIsTypedBeforeIt()
    {
        var vm = Composer();
        vm.BodyPlain = "Hello world";
        vm.SelectionStart = 6;
        vm.SelectionLength = 5;
        vm.ApplyColorCommand.Execute("#E5484D");

        vm.BodyPlain = "Oh, Hello world";

        vm.PreviewHtml.Should().Contain("<span style=\"color:#E5484D;\">world</span>");
    }

    [AvaloniaFact]
    public async Task CustomColour_AppliesAndJoinsThePalette()
    {
        var vm = Composer();
        vm.BodyPlain = "Hello";
        vm.SelectionStart = 0;
        vm.SelectionLength = 5;
        vm.CustomColor = "#123ABC";

        await vm.ApplyCustomColorCommand.ExecuteAsync(null);

        vm.PreviewHtml.Should().Contain("color:#123ABC");
        vm.Palette.Should().Contain("#123ABC");
    }

    [AvaloniaFact]
    public async Task CustomColour_RejectsGarbage()
    {
        var vm = Composer();
        vm.BodyPlain = "Hello";
        vm.CustomColor = "red;background:url(x)";

        await vm.ApplyCustomColorCommand.ExecuteAsync(null);

        vm.PreviewHtml.Should().NotContain("url(x)");
        vm.StatusMessage.Should().Contain("#RRGGBB");
    }

    // === AI draft keeps the quote ===

    [AvaloniaFact]
    public async Task AiDraft_KeepsTheQuotedOriginal()
    {
        var (_, rows) = await SeedAsync(1);
        await Store.SaveBodyAsync(new MessageBody { MessageId = rows[0].Id, TextPlain = "Original text" });
        var vm = Composer(ai: new FakeAi("Thanks, will do."));

        await vm.LoadFromAsync(rows[0], ComposeMode.Reply);
        await vm.AiDraftCommand.ExecuteAsync(null);

        vm.BodyPlain.Should().StartWith("Thanks, will do.");
        vm.BodyPlain.Should().Contain("> Original text");
    }

    // === Undo Send, unsaved work ===

    [AvaloniaFact]
    public async Task UndoSend_NothingGoesOut()
    {
        var actions = new FakeActions();
        var vm = Composer(actions: actions);
        vm.SelectedAccount = new Account { Email = "me@x.com" };
        vm.To = "a@x.com";
        vm.UndoSendDelay = TimeSpan.FromSeconds(30);

        var sending = vm.SendCommand.ExecuteAsync(null);
        vm.IsSendPending.Should().BeTrue();
        vm.UndoSendCommand.Execute(null);
        await sending;

        actions.Sent.Should().BeEmpty();
        vm.IsSendPending.Should().BeFalse();
        vm.IsSending.Should().BeFalse("the message can be edited and sent again");
    }

    [AvaloniaFact]
    public async Task Send_AfterTheWindow_GoesOutOnce()
    {
        var actions = new FakeActions();
        var vm = Composer(actions: actions);
        vm.SelectedAccount = new Account { Email = "me@x.com" };
        vm.To = "a@x.com";
        vm.UndoSendDelay = TimeSpan.FromMilliseconds(1);

        await vm.SendCommand.ExecuteAsync(null);

        actions.Sent.Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Cancel_WithUnsavedText_AsksFirst()
    {
        var vm = Composer();
        var closed = 0;
        vm.CloseRequested += () => closed++;
        vm.BodyPlain = "Important letter";

        vm.CancelCommand.Execute(null);
        closed.Should().Be(0, "one stray Esc must not lose the text");

        vm.CancelCommand.Execute(null);
        closed.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task AutoSave_StoresOnlyWhenSomethingChanged()
    {
        var actions = new FakeActions();
        var vm = Composer(actions: actions);
        vm.SelectedAccount = new Account { Email = "me@x.com" };

        await vm.AutoSaveAsync();
        actions.Drafts.Should().BeEmpty();

        vm.BodyPlain = "wip";
        await vm.AutoSaveAsync();
        await vm.AutoSaveAsync();
        actions.Drafts.Should().ContainSingle("the second tick has nothing new to save");
        vm.IsDirty.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task EditDraft_PrefillsAndReplacesTheSameDraft()
    {
        var (_, rows) = await SeedAsync(1);
        var draft = rows[0];
        draft.ToAddresses = "[\"bob@x.com\"]";
        await Store.SaveBodyAsync(new MessageBody { MessageId = draft.Id, TextPlain = "Half written" });
        var actions = new FakeActions();
        var vm = Composer(actions: actions);

        await vm.LoadFromAsync(draft, ComposeMode.EditDraft);
        vm.SelectedAccount ??= new Account { Email = "me@x.com" };
        vm.To.Should().Contain("bob@x.com");
        vm.BodyPlain.Should().Be("Half written");

        vm.BodyPlain = "Finished";
        await vm.SaveDraftCommand.ExecuteAsync(null);

        actions.ReplacedDraftUids.Should().Equal(draft.MessageUid);
    }

    // === Message list ===

    [AvaloniaFact]
    public async Task Refresh_KeepsTheOpenMessage()
    {
        var (folder, _) = await SeedAsync(3);
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.MessageList.LoadMessagesAsync(folder);
        Dispatcher.UIThread.RunJobs();

        var open = vm.MessageList.Messages[1];
        vm.MessageList.SelectedMessage = open;
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        vm.MessageReader.CurrentMessage!.Id.Should().Be(open.Id);

        await vm.MessageList.RefreshAsync();
        Dispatcher.UIThread.RunJobs();

        vm.MessageList.SelectedMessage.Should().NotBeNull("a background sync must not close what is being read");
        vm.MessageList.SelectedMessage!.Id.Should().Be(open.Id);
        vm.MessageReader.CurrentMessage.Should().NotBeNull();
        vm.MessageReader.CurrentMessage!.Id.Should().Be(open.Id);
    }

    [AvaloniaFact]
    public async Task List_PagesThroughAFolderBiggerThanOnePage()
    {
        var (folder, _) = await SeedAsync(MessageListViewModel.PageSize + 5);
        var vm = Services.GetRequiredService<MessageListViewModel>();

        await vm.LoadMessagesAsync(folder);
        vm.Messages.Should().HaveCount(MessageListViewModel.PageSize);
        vm.TotalCount.Should().Be(MessageListViewModel.PageSize + 5);
        vm.HasMore.Should().BeTrue();

        await vm.LoadMoreCommand.ExecuteAsync(null);
        vm.Messages.Should().HaveCount(MessageListViewModel.PageSize + 5);
        vm.HasMore.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Sort_SurvivesARefresh()
    {
        var (folder, _) = await SeedAsync(3);
        var vm = Services.GetRequiredService<MessageListViewModel>();
        await vm.LoadMessagesAsync(folder);

        vm.SortBySizeCommand.Execute(null); // descending
        vm.SortBySizeCommand.Execute(null); // ascending
        await vm.RefreshAsync();

        vm.Messages.Select(m => m.Size).Should().Equal(1, 2, 3);
    }

    [AvaloniaFact]
    public async Task ToggleRead_ServerRefuses_RowGoesBack()
    {
        var actions = new FakeActions { Refuse = true };
        var vm = new MessageListViewModel(Store, actions, new FakeSync());
        var message = new Message { IsRead = true };
        vm.SelectedMessage = message;

        await vm.ToggleReadCommand.ExecuteAsync(null);

        message.IsRead.Should().BeTrue();
        vm.StatusMessage.Should().Contain("not saved");
    }

    [AvaloniaFact]
    public async Task Delete_GoesToTheNeighbour_AndHandlesAStaleInstance()
    {
        var (folder, _) = await SeedAsync(3);
        var vm = new MessageListViewModel(Store, new FakeActions(), new FakeSync());
        await vm.LoadMessagesAsync(folder);
        var row = vm.Messages[1];
        var next = vm.Messages[2];
        vm.SelectedMessage = row;
        var stale = new Message { Id = row.Id, AccountId = row.AccountId, FolderId = folder };

        await vm.DeleteCommand.ExecuteAsync(stale);

        vm.Messages.Should().HaveCount(2).And.NotContain(row);
        vm.SelectedMessage.Should().BeSameAs(next);
    }

    // === Reader ===

    [AvaloniaFact]
    public async Task Reader_SlowMessage_DoesNotOverwriteTheOneOpenedAfterIt()
    {
        var sync = new FakeSync();
        var reader = new MessageReaderViewModel(Store, new HtmlSanitizer(), sync, Services.GetRequiredService<IAiAssistant>());
        var slow = new Message { Subject = "slow", IsRead = true };
        var fast = new Message { Subject = "fast", IsRead = true };
        var gate = new TaskCompletionSource<MessageBody?>();
        sync.Bodies[slow.Id] = gate.Task;
        sync.Bodies[fast.Id] = Task.FromResult<MessageBody?>(new MessageBody { TextPlain = "fast body" });

        var first = reader.LoadMessageAsync(slow);
        await reader.LoadMessageAsync(fast);
        gate.SetResult(new MessageBody { TextPlain = "slow body" });
        await first;

        reader.CurrentMessage.Should().BeSameAs(fast);
        reader.DisplayText.Should().Be("fast body");
        reader.DisplayHtml.Should().Contain("fast body").And.NotContain("slow body");
        reader.IsLoading.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Reader_FailedLoad_DoesNotKeepThePreviousBody()
    {
        var sync = new FakeSync();
        var reader = new MessageReaderViewModel(Store, new HtmlSanitizer(), sync, Services.GetRequiredService<IAiAssistant>());
        var good = new Message { IsRead = true };
        var bad = new Message { IsRead = true };
        sync.Bodies[good.Id] = Task.FromResult<MessageBody?>(new MessageBody { TextPlain = "secret" });
        sync.Bodies[bad.Id] = Task.FromException<MessageBody?>(new IOException("offline"));

        await reader.LoadMessageAsync(good);
        await reader.LoadMessageAsync(bad);

        reader.DisplayIsHtml.Should().BeFalse("the error text has to be what is shown");
        reader.DisplayText.Should().Contain("offline");
    }

    // === Calendar outside UTC ===

    [AvaloniaFact]
    public void EventEditor_ShowsTheLocalDayOfTheEvent()
    {
        var local = new DateTime(2026, 3, 15, 23, 30, 0, DateTimeKind.Local);
        var vm = new EventEditorViewModel(new CalendarEvent { Title = "x", StartUtc = local.ToUniversalTime(), EndUtc = local.AddHours(1).ToUniversalTime() });

        vm.StartDate!.Value.LocalDateTime.Date.Should().Be(new DateTime(2026, 3, 15));
        vm.StartTime.Should().Be(new TimeSpan(23, 30, 0));
    }

    // === Shortcuts ===

    [AvaloniaFact]
    public async Task Shortcuts_MoveThroughTheList_ButNotWhileTyping()
    {
        var (folder, _) = await SeedAsync(3);
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        await vm.MessageList.LoadMessagesAsync(folder);

        vm.HandleShortcut(Key.J, KeyModifiers.None, typing: true).Should().BeFalse("the letter belongs to the text box");
        vm.MessageList.SelectedMessage.Should().BeNull();

        vm.HandleShortcut(Key.J, KeyModifiers.None, typing: false).Should().BeTrue();
        var first = vm.MessageList.SelectedMessage;
        first.Should().BeSameAs(vm.MessageList.Messages[0]);
        vm.HandleShortcut(Key.J, KeyModifiers.None, typing: false);
        vm.MessageList.SelectedMessage.Should().BeSameAs(vm.MessageList.Messages[1]);
        vm.HandleShortcut(Key.K, KeyModifiers.None, typing: false);
        vm.MessageList.SelectedMessage.Should().BeSameAs(first);
    }

    [AvaloniaFact]
    public void Shortcuts_CtrlK_AsksForTheSearchBox_EvenWhileTyping()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var asked = false;
        vm.FocusSearchRequested += () => asked = true;

        vm.HandleShortcut(Key.K, KeyModifiers.Control, typing: true).Should().BeTrue();

        asked.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Composer_CtrlB_BoldsTheSelection()
    {
        var vm = Composer();
        vm.BodyPlain = "Hello";
        vm.SelectionStart = 0;
        vm.SelectionLength = 5;

        vm.HandleShortcut(Key.B, KeyModifiers.Control).Should().BeTrue();

        vm.PreviewHtml.Should().Contain("font-weight:bold");
    }

    // === Settings persist, and the windows really show the new controls ===

    [AvaloniaFact]
    public async Task General_SurviveReopeningSettings()
    {
        var first = Services.GetRequiredService<SettingsViewModel>();
        await first.LoadCommand.ExecuteAsync(null);
        first.SyncIntervalMinutes = 15;
        first.UndoSendSeconds = 10;
        first.Theme = "light";
        await first.SaveGeneralCommand.ExecuteAsync(null);

        var second = Services.GetRequiredService<SettingsViewModel>();
        await second.LoadCommand.ExecuteAsync(null);

        second.SyncIntervalMinutes.Should().Be(15);
        second.UndoSendSeconds.Should().Be(10);
        second.Theme.Should().Be("light");

        second.SyncIntervalMinutes = 5;
        second.UndoSendSeconds = 5;
        second.Theme = "system";
        await second.SaveGeneralCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task Logo_GoesIntoTheSignatureAsHtml_AndIsSizeLimited()
    {
        var vm = Services.GetRequiredService<SettingsViewModel>();
        vm.PickImageFile = () => Task.FromResult<(string, byte[])?>(("logo.png", new byte[] { 1, 2, 3 }));

        await vm.InsertLogoCommand.ExecuteAsync(null);

        vm.SignatureIsHtml.Should().BeTrue();
        vm.SignatureText.Should().Contain("<img src=\"data:image/png;base64,AQID\"");

        vm.SignatureText = "";
        vm.PickImageFile = () => Task.FromResult<(string, byte[])?>(("big.png", new byte[SettingsViewModel.MaxLogoBytes + 1]));
        await vm.InsertLogoCommand.ExecuteAsync(null);
        vm.SignatureText.Should().BeEmpty();
        vm.SignatureStatus.Should().Contain("512 KB");
    }

    [AvaloniaFact]
    public void MainWindow_HasAccountSwitcherAndSortButtons()
    {
        var vm = Services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<ComboBox>().Should().NotBeEmpty("a second mailbox has to be reachable");
        var buttons = window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        buttons.Should().Contain(new[] { "Date", "Sender", "Subject", "Size", "Star", "Read / Unread" });
    }

    // ---- fakes ----

    private sealed class FakeActions : IMailActionService
    {
        public bool Refuse;
        public List<OutgoingMessage> Sent { get; } = new();
        public List<OutgoingMessage> Drafts { get; } = new();
        public List<int> ReplacedDraftUids { get; } = new();

        public Task SendAsync(Guid a, OutgoingMessage m, CancellationToken ct = default) { Sent.Add(m); return Task.CompletedTask; }
        public Task<int?> SaveDraftAsync(Guid a, OutgoingMessage d, int? previous, CancellationToken ct = default)
        {
            Drafts.Add(d);
            if (previous is { } uid) ReplacedDraftUids.Add(uid);
            return Task.FromResult<int?>(1000 + Drafts.Count);
        }
        public Task DeleteDraftAsync(Guid a, int uid, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetReadAsync(Message m, bool read, CancellationToken ct = default) =>
            Refuse ? throw new IOException("refused") : Task.CompletedTask;
        public Task SetStarredAsync(Message m, bool starred, CancellationToken ct = default) =>
            Refuse ? throw new IOException("refused") : Task.CompletedTask;
        public Task ArchiveAsync(Message m, CancellationToken ct = default) => Task.CompletedTask;
        public Task TrashAsync(Message m, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAi : IAiAssistant
    {
        private readonly string _text;
        public FakeAi(string text) => _text = text;
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<AiSettings?> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult<AiSettings?>(null);
        public Task<AiResult> SummarizeThreadAsync(IReadOnlyList<Message> m, CancellationToken ct = default) => Task.FromResult(new AiResult { Text = _text });
        public Task<AiResult> DraftReplyAsync(Message o, string? s = null, CancellationToken ct = default) => Task.FromResult(new AiResult { Text = _text });
        public Task<AiResult> ClassifyAsync(Message m, CancellationToken ct = default) => Task.FromResult(new AiResult { Text = _text });
        public Task<AiResult> ImproveWritingAsync(string t, CancellationToken ct = default) => Task.FromResult(new AiResult { Text = _text });
        public Task<AiConnectionTest> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(new AiConnectionTest());
    }

    private sealed class FakeSync : IMailSyncService
    {
        public Dictionary<Guid, Task<MessageBody?>> Bodies { get; } = new();
        public Task<int> SyncAccountAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> SyncFolderAsync(Guid a, Guid f, CancellationToken ct = default) => Task.FromResult(0);
        public Task<MessageBody?> EnsureBodyAsync(Message m, CancellationToken ct = default) =>
            Bodies.TryGetValue(m.Id, out var body) ? body : Task.FromResult<MessageBody?>(null);
        public Task<IReadOnlyList<Attachment>> EnsureAttachmentsAsync(Message m, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Attachment>>(Array.Empty<Attachment>());
        public Task<byte[]> DownloadAttachmentAsync(Message m, Attachment a, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<byte>());
        public Task MarkAsReadOnServerAsync(Message m, CancellationToken ct = default) => Task.CompletedTask;
    }
}
