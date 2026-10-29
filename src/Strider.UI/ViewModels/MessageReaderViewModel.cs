using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Security;

namespace Strider.UI.ViewModels;

public enum ComposeMode
{
    New,
    Reply,
    ReplyAll,
    Forward,
}

/// <summary>
/// ViewModel for the message reader panel.
/// </summary>
public partial class MessageReaderViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;
    private readonly HtmlSanitizer _htmlSanitizer;
    private readonly IMailSyncService _sync;

    /// <summary>Raised by Reply / Reply All / Forward; the main window opens the composer.</summary>
    public event Action<Message, ComposeMode>? ComposeRequested;

    [ObservableProperty]
    private Message? _currentMessage;

    [ObservableProperty]
    private MessageBody? _currentBody;

    [ObservableProperty]
    private ObservableCollection<Attachment> _attachments = new();

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Sanitized HTML, kept for the future WebView renderer.</summary>
    [ObservableProperty]
    private string _displayHtml = "";

    /// <summary>Readable plain text shown in the reader until the WebView lands.</summary>
    [ObservableProperty]
    private string _displayText = "";

    [ObservableProperty]
    private bool _showRawHeaders;

    [ObservableProperty]
    private string _pgpStatus = "";

    public MessageReaderViewModel(
        IMessageStore messageStore,
        HtmlSanitizer htmlSanitizer,
        IMailSyncService sync)
    {
        _messageStore = messageStore;
        _htmlSanitizer = htmlSanitizer;
        _sync = sync;
    }

    public bool HasMessage => CurrentMessage != null;

    partial void OnCurrentMessageChanged(Message? value)
    {
        OnPropertyChanged(nameof(HasMessage));
        ReplyCommand.NotifyCanExecuteChanged();
        ReplyAllCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    public void Clear()
    {
        CurrentMessage = null;
        CurrentBody = null;
        Attachments = new ObservableCollection<Attachment>();
        DisplayHtml = "";
        DisplayText = "";
        PgpStatus = "";
    }

    [RelayCommand]
    public async Task LoadMessageAsync(Message message)
    {
        CurrentMessage = message;
        IsLoading = true;
        DisplayText = "Loading...";

        try
        {
            // Body is downloaded from IMAP on first open, then served from cache.
            CurrentBody = await _sync.EnsureBodyAsync(message);

            var atts = await _messageStore.GetAttachmentsAsync(message.Id);
            Attachments = new ObservableCollection<Attachment>(atts);

            if (CurrentBody != null)
            {
                var rawHtml = !string.IsNullOrWhiteSpace(CurrentBody.TextHtml)
                    ? CurrentBody.TextHtml
                    : $"<pre>{WebUtility.HtmlEncode(CurrentBody.TextPlain ?? "")}</pre>";
                DisplayHtml = _htmlSanitizer.Sanitize(rawHtml);
                DisplayText = !string.IsNullOrWhiteSpace(CurrentBody.TextPlain)
                    ? CurrentBody.TextPlain!
                    : HtmlText.ToPlainText(DisplayHtml);
            }
            else
            {
                DisplayText = "";
            }

            PgpStatus = message.PgpStatus switch
            {
                Core.Domain.PgpStatus.Encrypted => "[Encrypted]",
                Core.Domain.PgpStatus.Signed => message.PgpVerified == PgpVerification.Valid
                    ? "[Signed - Valid]" : "[Signed - Invalid]",
                Core.Domain.PgpStatus.SignedAndEncrypted => "[Encrypted + Signed]",
                _ => "",
            };

            if (!message.IsRead)
            {
                message.IsRead = true;
                await _messageStore.UpdateMessageAsync(message);
                _ = MarkAsReadOnServerAsync(message);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load message body");
            DisplayText = $"Could not load this message: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task MarkAsReadOnServerAsync(Message message)
    {
        try
        {
            await _sync.MarkAsReadOnServerAsync(message);
        }
        catch (Exception ex)
        {
            // Local state is already correct; next sync reconciles.
            Serilog.Log.Warning(ex, "MarkAsRead on server failed");
        }
    }

    [RelayCommand]
    private Task DownloadAttachmentAsync(Attachment attachment)
    {
        // TODO: file save dialog (Avalonia StorageProvider) — next milestone.
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleRawHeaders()
    {
        ShowRawHeaders = !ShowRawHeaders;
    }

    [RelayCommand(CanExecute = nameof(HasMessage))]
    private void Reply() => ComposeRequested?.Invoke(CurrentMessage!, ComposeMode.Reply);

    [RelayCommand(CanExecute = nameof(HasMessage))]
    private void ReplyAll() => ComposeRequested?.Invoke(CurrentMessage!, ComposeMode.ReplyAll);

    [RelayCommand(CanExecute = nameof(HasMessage))]
    private void Forward() => ComposeRequested?.Invoke(CurrentMessage!, ComposeMode.Forward);
}
