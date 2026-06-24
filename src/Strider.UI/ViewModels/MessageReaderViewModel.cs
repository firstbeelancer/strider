using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the message reader panel.
/// </summary>
public partial class MessageReaderViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;

    [ObservableProperty]
    private Message? _currentMessage;

    [ObservableProperty]
    private MessageBody? _currentBody;

    [ObservableProperty]
    private ObservableCollection<Attachment> _attachments = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _displayHtml = "";

    [ObservableProperty]
    private bool _showRawHeaders;

    [ObservableProperty]
    private string _pgpStatus = "";

    public MessageReaderViewModel(IMessageStore messageStore)
    {
        _messageStore = messageStore;
    }

    [RelayCommand]
    public async Task LoadMessageAsync(Message message)
    {
        CurrentMessage = message;
        IsLoading = true;

        try
        {
            // Load body
            CurrentBody = await _messageStore.GetBodyAsync(message.Id);

            // Load attachments
            var atts = await _messageStore.GetAttachmentsAsync(message.Id);
            Attachments = new ObservableCollection<Attachment>(atts);

            // Prepare display HTML
            if (CurrentBody != null)
            {
                DisplayHtml = CurrentBody.TextHtml ?? 
                    $"<pre>{System.Net.WebUtility.HtmlEncode(CurrentBody.TextPlain ?? "")}</pre>";
            }

            // Update PGP status
            PgpStatus = message.PgpStatus switch
            {
                Core.Domain.PgpStatus.Encrypted => "[Encrypted]",
                Core.Domain.PgpStatus.Signed => message.PgpVerified == Core.Domain.PgpVerification.Valid 
                    ? "[Signed - Valid]" : "[Signed - Invalid]",
                Core.Domain.PgpStatus.SignedAndEncrypted => "[Encrypted + Signed]",
                _ => "",
            };

            // Mark as read
            if (!message.IsRead)
            {
                message.IsRead = true;
                await _messageStore.UpdateMessageAsync(message);
            }
        }
        catch (Exception ex)
        {
            DisplayHtml = $"<p style='color:red'>Error loading message: {System.Net.WebUtility.HtmlEncode(ex.Message)}</p>";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task DownloadAttachmentAsync(Attachment attachment)
    {
        // TODO: Implement file save dialog
        if (!string.IsNullOrEmpty(attachment.LocalPath))
        {
            System.Diagnostics.Debug.WriteLine($"Attachment already downloaded: {attachment.LocalPath}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Need to download: {attachment.Filename}");
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleRawHeaders()
    {
        ShowRawHeaders = !ShowRawHeaders;
    }

    [RelayCommand]
    private Task ReplyAsync()
    {
        // TODO: Open compose with reply
        if (CurrentMessage == null) return Task.CompletedTask;
        System.Diagnostics.Debug.WriteLine($"Reply to: {CurrentMessage.FromAddress}");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ReplyAllAsync()
    {
        // TODO: Open compose with reply-all
        if (CurrentMessage == null) return Task.CompletedTask;
        System.Diagnostics.Debug.WriteLine($"Reply all to: {CurrentMessage.FromAddress}");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ForwardAsync()
    {
        // TODO: Open compose with forward
        if (CurrentMessage == null) return Task.CompletedTask;
        System.Diagnostics.Debug.WriteLine($"Forward: {CurrentMessage.Subject}");
        return Task.CompletedTask;
    }
}
