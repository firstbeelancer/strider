using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the compose window. v0.1 composes plain text; the rich
/// TipTap editor (F-010 full) will fill BodyHtml later.
/// </summary>
public partial class ComposeViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;
    private readonly IAccountStore _accountStore;
    private readonly IMailActionService _actions;

    /// <summary>Raised after a successful send or on Cancel; the window closes itself.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string _to = "";
    [ObservableProperty] private string _cc = "";
    [ObservableProperty] private string _bcc = "";
    [ObservableProperty] private bool _showCcBcc;
    [ObservableProperty] private string _subject = "";
    [ObservableProperty] private string _bodyPlain = "";
    [ObservableProperty] private ObservableCollection<AttachmentData> _attachments = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private Account? _selectedAccount;

    [ObservableProperty] private ObservableCollection<Account> _accounts = new();
    [ObservableProperty] private ComposeMode _mode = ComposeMode.New;
    [ObservableProperty] private string _windowTitle = "New Message";

    /// <summary>Set by the window: opens the OS file picker, returns chosen files.</summary>
    public Func<Task<IReadOnlyList<(string Name, byte[] Data)>>>? PickFiles { get; set; }

    /// <summary>Most providers reject messages above ~25 MB.</summary>
    public const long MaxTotalAttachmentBytes = 25L * 1024 * 1024;

    // UID of the last saved draft on the server, so a re-save replaces it and Send removes it.
    private int? _draftUid;

    // Threading headers for replies.
    private string? _inReplyTo;
    private List<string> _references = new();

    public ComposeViewModel(
        IMessageStore messageStore,
        IAccountStore accountStore,
        IMailActionService actions)
    {
        _messageStore = messageStore;
        _accountStore = accountStore;
        _actions = actions;
    }

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        var accounts = await _accountStore.GetAllAccountsAsync();
        Accounts = new ObservableCollection<Account>(accounts);
        SelectedAccount ??= Accounts.FirstOrDefault();
    }

    /// <summary>Prepares a reply, reply-all or forward of <paramref name="original"/>.</summary>
    public async Task LoadFromAsync(Message original, ComposeMode mode)
    {
        await LoadAccountsAsync();
        // Answer from the account the mail arrived to.
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == original.AccountId) ?? SelectedAccount;
        Mode = mode;

        var body = await _messageStore.GetBodyAsync(original.Id);
        var originalText = body?.TextPlain
                           ?? Strider.Infrastructure.Security.HtmlText.ToPlainText(body?.TextHtml);

        switch (mode)
        {
            case ComposeMode.Reply:
                To = AddressList.ToInput(ReplyBuilder.ReplyTo(original));
                break;
            case ComposeMode.ReplyAll:
                var (to, cc) = ReplyBuilder.ReplyAll(original, SelectedAccount?.Email ?? "");
                To = AddressList.ToInput(to);
                Cc = AddressList.ToInput(cc);
                ShowCcBcc = cc.Count > 0;
                break;
        }

        if (mode == ComposeMode.Forward)
        {
            Subject = ReplyBuilder.ForwardSubject(original.Subject);
            BodyPlain = "\n\n" + ReplyBuilder.QuoteHeader(original, forward: true) + "\n" + originalText;
            WindowTitle = "Forward";
        }
        else
        {
            Subject = ReplyBuilder.ReplySubject(original.Subject);
            BodyPlain = "\n\n" + ReplyBuilder.QuoteHeader(original, forward: false) + ReplyBuilder.QuotePlain(originalText);
            _inReplyTo = ReplyBuilder.StripBrackets(original.MessageId);
            _references = ReplyBuilder.References(original);
            WindowTitle = "Reply";
        }
    }

    private bool CanSend() => !IsSending && SelectedAccount != null;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var to = AddressList.ParseInput(To);
        var cc = AddressList.ParseInput(Cc);
        var bcc = AddressList.ParseInput(Bcc);

        if (to.Count + cc.Count + bcc.Count == 0)
        {
            StatusMessage = "Add at least one recipient";
            return;
        }
        if (SelectedAccount == null)
        {
            StatusMessage = "No account selected";
            return;
        }

        IsSending = true;
        StatusMessage = "Sending...";
        try
        {
            await _actions.SendAsync(SelectedAccount.Id, BuildMessage(to, cc, bcc));
            if (_draftUid is { } uid)
            {
                try { await _actions.DeleteDraftAsync(SelectedAccount.Id, uid); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Sent, but removing the draft failed"); }
            }
            StatusMessage = "Sent";
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Send failed");
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    private OutgoingMessage BuildMessage(List<string> to, List<string> cc, List<string> bcc) => new()
    {
        From = SelectedAccount?.Email ?? "",
        FromName = SelectedAccount?.DisplayName,
        To = to,
        Cc = cc,
        Bcc = bcc,
        Subject = Subject,
        BodyPlain = BodyPlain,
        InReplyTo = _inReplyTo,
        References = _references,
        Attachments = Attachments.ToList(),
    };

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SaveDraftAsync()
    {
        if (SelectedAccount == null) return;
        IsSending = true;
        try
        {
            _draftUid = await _actions.SaveDraftAsync(SelectedAccount.Id,
                BuildMessage(AddressList.ParseInput(To), AddressList.ParseInput(Cc), AddressList.ParseInput(Bcc)),
                _draftUid);
            StatusMessage = $"Draft saved {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving draft failed");
            StatusMessage = $"Draft not saved: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        if (PickFiles == null) return;
        try
        {
            var files = await PickFiles();
            foreach (var (name, data) in files)
            {
                var total = Attachments.Sum(a => (long)a.Content.Length) + data.Length;
                if (total > MaxTotalAttachmentBytes)
                {
                    StatusMessage = $"{name} not added: attachments would exceed 25 MB";
                    continue;
                }
                Attachments.Add(new AttachmentData
                {
                    Filename = name,
                    ContentType = MimeKit.MimeTypes.GetMimeType(name),
                    Content = data,
                });
            }
            OnPropertyChanged(nameof(AttachmentsSummary));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Attach failed");
            StatusMessage = $"Could not attach: {ex.Message}";
        }
    }

    public string AttachmentsSummary => Attachments.Count == 0
        ? ""
        : $"{Attachments.Count} file(s), {Attachments.Sum(a => (long)a.Content.Length) / 1024.0:0} KB";

    [RelayCommand]
    private void ToggleCcBcc() => ShowCcBcc = !ShowCcBcc;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private void RemoveAttachment(AttachmentData attachment)
    {
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(AttachmentsSummary));
    }
}
