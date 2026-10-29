using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Services;

/// <summary>
/// Send, archive and trash — the actions that change mail on the server.
/// </summary>
public sealed class MailActionService : IMailActionService
{
    private readonly IAccountStore _accounts;
    private readonly IMessageStore _messages;
    private readonly ImapSessionRunner _imap;
    private readonly Func<ISmtpGateway> _smtpFactory;

    public MailActionService(
        IAccountStore accounts,
        IMessageStore messages,
        ImapSessionRunner imap,
        Func<ISmtpGateway> smtpFactory)
    {
        _accounts = accounts;
        _messages = messages;
        _imap = imap;
        _smtpFactory = smtpFactory;
    }

    public async Task SendAsync(Guid accountId, OutgoingMessage message, CancellationToken ct = default)
    {
        var account = await _accounts.GetAccountAsync(accountId, ct)
                      ?? throw new InvalidOperationException("Account not found");
        if (string.IsNullOrWhiteSpace(message.From)) message.From = account.Email;
        if (string.IsNullOrWhiteSpace(message.FromName)) message.FromName = account.DisplayName;

        var smtp = _smtpFactory();
        byte[] raw;
        try
        {
            await smtp.ConnectAsync(account, ct);
            raw = await smtp.SendAsync(account, message, ct);
            await smtp.DisconnectAsync(ct);
        }
        finally
        {
            (smtp as IDisposable)?.Dispose();
        }

        // The mail is already out. Failing to file a copy in Sent must not
        // look like a failed send to the user.
        if (ServerSavesSentCopy(account)) return;
        try
        {
            var sent = (await _accounts.GetFoldersAsync(account.Id, ct)).FirstOrDefault(f => f.Type == FolderType.Sent);
            if (sent != null)
                await _imap.RunAsync(account, gw => gw.AppendMessageAsync(account, sent.RemoteName, raw, seen: true, ct), ct);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Message sent, but saving a copy to Sent failed");
        }
    }

    public async Task ArchiveAsync(Message message, CancellationToken ct = default)
    {
        var (account, folders, source) = await LoadContextAsync(message, ct);
        var archive = folders.FirstOrDefault(f => f.Type == FolderType.Archive)
                      ?? throw new InvalidOperationException("This account has no Archive folder");
        if (archive.Id == source.Id) return;

        await _imap.RunAsync(account,
            gw => gw.MoveMessageAsync(account, source.RemoteName, message.MessageUid, archive.RemoteName, ct), ct);
        await _messages.DeleteMessageAsync(message.Id, ct);
    }

    public async Task TrashAsync(Message message, CancellationToken ct = default)
    {
        var (account, folders, source) = await LoadContextAsync(message, ct);
        var trash = folders.FirstOrDefault(f => f.Type == FolderType.Trash);

        if (trash == null || trash.Id == source.Id)
        {
            // Already in Trash (or the server has none): delete for good.
            await _imap.RunAsync(account,
                gw => gw.DeleteMessageAsync(account, source.RemoteName, message.MessageUid, ct), ct);
        }
        else
        {
            await _imap.RunAsync(account,
                gw => gw.MoveMessageAsync(account, source.RemoteName, message.MessageUid, trash.RemoteName, ct), ct);
        }
        await _messages.DeleteMessageAsync(message.Id, ct);
    }

    /// <summary>
    /// Gmail and Outlook store a copy of SMTP-sent mail in Sent themselves;
    /// appending another one would duplicate it.
    /// </summary>
    public static bool ServerSavesSentCopy(Account account)
    {
        var host = (account.SmtpHost ?? string.Empty).ToLowerInvariant();
        return host.EndsWith("gmail.com") || host.EndsWith("googlemail.com")
               || host.EndsWith("office365.com") || host.EndsWith("outlook.com");
    }

    private async Task<(Account, IReadOnlyList<Folder>, Folder)> LoadContextAsync(Message message, CancellationToken ct)
    {
        var account = await _accounts.GetAccountAsync(message.AccountId, ct)
                      ?? throw new InvalidOperationException("Account not found");
        var folders = await _accounts.GetFoldersAsync(account.Id, ct);
        var source = folders.FirstOrDefault(f => f.Id == message.FolderId)
                     ?? throw new InvalidOperationException("Folder not found");
        return (account, folders, source);
    }
}
