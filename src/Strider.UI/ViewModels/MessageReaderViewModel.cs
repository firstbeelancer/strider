using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;
using Strider.Infrastructure.Security;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the message reader panel.
/// </summary>
public partial class MessageReaderViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;
    private readonly HtmlSanitizer _htmlSanitizer;
    private readonly IImapGatewayFactory? _imapGatewayFactory;
    private readonly IAccountStore? _accountStore;

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

    public MessageReaderViewModel(
        IMessageStore messageStore,
        HtmlSanitizer htmlSanitizer,
        IImapGatewayFactory? imapGatewayFactory = null,
        IAccountStore? accountStore = null)
    {
        _messageStore = messageStore;
        _htmlSanitizer = htmlSanitizer;
        _imapGatewayFactory = imapGatewayFactory;
        _accountStore = accountStore;
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

            // Prepare display HTML — sanitized through AngleSharp allowlist
            if (CurrentBody != null)
            {
                var rawHtml = !string.IsNullOrWhiteSpace(CurrentBody.TextHtml)
                    ? CurrentBody.TextHtml
                    : $"<pre>{WebUtility.HtmlEncode(CurrentBody.TextPlain ?? "")}</pre>";
                DisplayHtml = _htmlSanitizer.Sanitize(rawHtml);
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

            // F-024: Mark as read on BOTH local DB AND IMAP server.
            // Without the server-side update, the read flag would be lost on
            // next sync (server would override local state). If offline or
            // IMAP unavailable, the local update still happens and the server
            // update is deferred to the pending_ops queue (future enhancement).
            if (!message.IsRead)
            {
                message.IsRead = true;
                await _messageStore.UpdateMessageAsync(message);
                await MarkAsReadOnServerAsync(message);
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

    /// <summary>
    /// Marks the message as read on the IMAP server. Silently fails if IMAP
    /// unavailable (offline, gateway factory not configured) — local DB update
    /// already happened, server sync will reconcile on next reconnect.
    /// </summary>
    private async Task MarkAsReadOnServerAsync(Message message)
    {
        if (_imapGatewayFactory == null || _accountStore == null) return;

        try
        {
            // Look up the account to get connection settings
            var account = await _accountStore.GetAccountAsync(message.AccountId);
            if (account == null) return;

            // Look up the folder to get its remote name
            var folders = await _accountStore.GetFoldersAsync(account.Id);
            var folder = folders.FirstOrDefault(f => f.Id == message.FolderId);
            if (folder == null) return;

            // Connect, mark as read, disconnect (short-lived connection)
            var imap = _imapGatewayFactory.ForAccount(account.Id);
            try
            {
                await imap.ConnectAsync(account);
                await imap.MarkAsReadAsync(account, folder.RemoteName, message.MessageUid);
                await imap.DisconnectAsync();
            }
            finally
            {
                _imapGatewayFactory.Release(account.Id);
            }
        }
        catch (Exception ex)
        {
            // Don't crash the reader if server update fails — local DB is correct,
            // and the next sync will reconcile. Log for debugging.
            System.Diagnostics.Debug.WriteLine($"MarkAsRead on server failed: {ex.Message}");
        }
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
