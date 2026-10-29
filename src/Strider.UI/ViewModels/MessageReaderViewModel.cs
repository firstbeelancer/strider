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
    private readonly IAiAssistant _ai;

    /// <summary>Set by the window: asks where to save a file, returns a writable stream or null if cancelled.</summary>
    public Func<string, Task<Stream?>>? PickSaveFile { get; set; }

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

    /// <summary>Sanitized HTML rendered by <c>HtmlTextBlock</c> (Wave 6) — no WebView needed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIsHtml))]
    private string _displayHtml = "";

    /// <summary>True when there is HTML to render; otherwise the plain-text view is shown.</summary>
    public bool DisplayIsHtml => !string.IsNullOrWhiteSpace(DisplayHtml);

    /// <summary>Readable plain text shown in the reader until the WebView lands.</summary>
    [ObservableProperty]
    private string _displayText = "";

    [ObservableProperty]
    private bool _showRawHeaders;

    [ObservableProperty]
    private string _pgpStatus = "";

    /// <summary>AI summary of the thread (F5.4). Persisted on the message row once generated.</summary>
    [ObservableProperty]
    private string _aiSummary = "";

    /// <summary>AI category of the current message (F5.6).</summary>
    [ObservableProperty]
    private string _aiCategory = "";

    [ObservableProperty]
    private string _aiStatus = "";

    [ObservableProperty]
    private bool _isAiBusy;

    public bool HasAiSummary => !string.IsNullOrWhiteSpace(AiSummary);

    partial void OnAiSummaryChanged(string value) => OnPropertyChanged(nameof(HasAiSummary));

    public MessageReaderViewModel(
        IMessageStore messageStore,
        HtmlSanitizer htmlSanitizer,
        IMailSyncService sync,
        IAiAssistant ai)
    {
        _messageStore = messageStore;
        _htmlSanitizer = htmlSanitizer;
        _sync = sync;
        _ai = ai;
    }

    public bool HasMessage => CurrentMessage != null;

    partial void OnCurrentMessageChanged(Message? value)
    {
        OnPropertyChanged(nameof(HasMessage));
        ReplyCommand.NotifyCanExecuteChanged();
        ReplyAllCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        SummarizeCommand.NotifyCanExecuteChanged();
        ClassifyCommand.NotifyCanExecuteChanged();
    }

    public void Clear()
    {
        CurrentMessage = null;
        CurrentBody = null;
        Attachments = new ObservableCollection<Attachment>();
        DisplayHtml = "";
        DisplayText = "";
        PgpStatus = "";
        AiSummary = "";
        AiCategory = "";
        AiStatus = "";
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

            var atts = await _sync.EnsureAttachmentsAsync(message);
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

            AiSummary = message.AiSummary ?? "";
            AiCategory = message.AiCategory ?? "";
            AiStatus = "";

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

    [ObservableProperty]
    private string _attachmentStatus = "";

    [RelayCommand]
    private async Task DownloadAttachmentAsync(Attachment attachment)
    {
        if (CurrentMessage == null || PickSaveFile == null) return;
        try
        {
            var name = string.IsNullOrWhiteSpace(attachment.Filename) ? "attachment" : attachment.Filename!;
            await using var target = await PickSaveFile(name);
            if (target == null) return;
            AttachmentStatus = $"Downloading {name}...";
            var bytes = await _sync.DownloadAttachmentAsync(CurrentMessage, attachment);
            await target.WriteAsync(bytes);
            AttachmentStatus = $"Saved {name}";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving attachment failed");
            AttachmentStatus = $"Could not save: {ex.Message}";
        }
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

    // === AI (ТЗ F5.4 / F5.6) ===

    /// <summary>
    /// Summarises the whole thread of the open message. The result is stored on
    /// the message row so it survives a restart and costs nothing to re-read.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasMessage))]
    private async Task SummarizeAsync()
    {
        if (CurrentMessage is not { } message || IsAiBusy) return;

        IsAiBusy = true;
        AiStatus = "Summarising...";
        try
        {
            var thread = await LoadThreadAsync(message);
            var result = await _ai.SummarizeThreadAsync(thread);
            AiSummary = result.Text;
            AiStatus = $"Summary ready - {result.UsageLabel}";

            message.AiSummary = result.Text;
            await _messageStore.UpdateMessageAsync(message);
        }
        catch (AiNotConfiguredException ex)
        {
            AiStatus = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Thread summary failed");
            AiStatus = $"Summary failed: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    /// <summary>Classifies the open message and stores the category (F5.6).</summary>
    [RelayCommand(CanExecute = nameof(HasMessage))]
    private async Task ClassifyAsync()
    {
        if (CurrentMessage is not { } message || IsAiBusy) return;

        IsAiBusy = true;
        AiStatus = "Classifying...";
        try
        {
            var result = await _ai.ClassifyAsync(message);
            AiCategory = result.Text;
            AiStatus = $"Category: {result.Text} - {result.UsageLabel}";

            message.AiCategory = result.Text;
            await _messageStore.UpdateMessageAsync(message);
        }
        catch (AiNotConfiguredException ex)
        {
            AiStatus = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Classification failed");
            AiStatus = $"Classification failed: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    /// <summary>
    /// Thread messages of the current mail, oldest first. Falls back to the single
    /// message when the thread id is unknown (e.g. a one-off message).
    /// </summary>
    private async Task<IReadOnlyList<Message>> LoadThreadAsync(Message message)
    {
        if (string.IsNullOrEmpty(message.ThreadId)) return new[] { message };

        var thread = await _messageStore.GetThreadMessagesAsync(message.ThreadId!);
        return thread.Count > 0 ? thread : new[] { message };
    }
}
