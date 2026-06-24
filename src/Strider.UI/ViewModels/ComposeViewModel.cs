using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the compose window.
/// </summary>
public partial class ComposeViewModel : ObservableObject
{
    // private readonly ISmtpGateway? _smtpGateway; // TODO: wire up when implemented
    private readonly IMessageStore _messageStore;
    private readonly IAccountStore _accountStore;

    [ObservableProperty]
    private string _from = "";

    [ObservableProperty]
    private string _to = "";

    [ObservableProperty]
    private string _cc = "";

    [ObservableProperty]
    private string _bcc = "";

    [ObservableProperty]
    private string _subject = "";

    [ObservableProperty]
    private string _bodyHtml = "";

    [ObservableProperty]
    private string _bodyPlain = "";

    [ObservableProperty]
    private ObservableCollection<AttachmentData> _attachments = new();

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private bool _isEncrypted;

    [ObservableProperty]
    private bool _isSigned;

    // Reply/Forward context
    [ObservableProperty]
    private Message? _replyToMessage;

    [ObservableProperty]
    private string _composeMode = "new"; // new, reply, reply-all, forward

    public ComposeViewModel(
        IMessageStore messageStore,
        IAccountStore accountStore)
    {
        _messageStore = messageStore;
        _accountStore = accountStore;
    }

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        var accounts = await _accountStore.GetAllAccountsAsync();
        Accounts = new ObservableCollection<Account>(accounts);
        if (Accounts.Count > 0)
        {
            SelectedAccount = Accounts[0];
            From = SelectedAccount.Email;
        }
    }

    [RelayCommand]
    public async Task LoadReplyAsync(Message message)
    {
        ReplyToMessage = message;
        ComposeMode = "reply";
        To = message.FromAddress;
        Subject = message.Subject.StartsWith("Re:") ? message.Subject : $"Re: {message.Subject}";

        // Load original message body for quote
        var body = await _messageStore.GetBodyAsync(message.Id);
        if (body != null)
        {
            var quote = body.TextPlain ?? "";
            BodyPlain = $"\n\n--- Original Message ---\nFrom: {message.FromName} <{message.FromAddress}>\nDate: {message.DateUtc:dd MMM yyyy HH:mm}\nSubject: {message.Subject}\n\n{quote}";
        }

        await LoadAccountsAsync();
    }

    [RelayCommand]
    public async Task LoadReplyAllAsync(Message message)
    {
        await LoadReplyAsync(message);
        ComposeMode = "reply-all";
        // Add other recipients to Cc
        if (!string.IsNullOrEmpty(message.CcAddresses))
        {
            Cc = message.CcAddresses;
        }
    }

    [RelayCommand]
    public async Task LoadForwardAsync(Message message)
    {
        ReplyToMessage = message;
        ComposeMode = "forward";
        Subject = message.Subject.StartsWith("Fwd:") ? message.Subject : $"Fwd: {message.Subject}";

        var body = await _messageStore.GetBodyAsync(message.Id);
        if (body != null)
        {
            BodyPlain = $"\n\n--- Forwarded Message ---\nFrom: {message.FromName} <{message.FromAddress}>\nDate: {message.DateUtc:dd MMM yyyy HH:mm}\nSubject: {message.Subject}\n\n{body.TextPlain ?? ""}";
        }

        await LoadAccountsAsync();
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(To))
        {
            StatusMessage = "Recipient (To) is required";
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
            // TODO: Implement actual SMTP sending
            // var toList = To.Split(',', ';').Select(s => s.Trim()).ToList();
            // await _smtpGateway.SendAsync(SelectedAccount, From, toList, ...);

            // Simulate send delay
            await Task.Delay(1000);

            StatusMessage = "Message sent!";
            
            // Save to Sent folder
            // TODO: Save to Sent via IMessageStore
        }
        catch (Exception ex)
        {
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private Task SaveDraftAsync()
    {
        // TODO: Save draft to Drafts folder
        StatusMessage = "Draft saved";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void AddAttachment()
    {
        // TODO: Open file picker
        StatusMessage = "Attach file (not yet implemented)";
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentData attachment)
    {
        Attachments.Remove(attachment);
    }
}
