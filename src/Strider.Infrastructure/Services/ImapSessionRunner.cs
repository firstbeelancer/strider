using System.Collections.Concurrent;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;

namespace Strider.Infrastructure.Services;

/// <summary>
/// Serialises all IMAP work per account. The per-account gateway owns a single
/// ImapClient, so two callers using it at once (sync + mark-as-read) used to
/// replace each other's connection mid-command. Every IMAP call goes through here.
/// </summary>
public sealed class ImapSessionRunner
{
    private readonly IImapGatewayFactory _factory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public ImapSessionRunner(IImapGatewayFactory factory)
    {
        _factory = factory;
    }

    public async Task<T> RunAsync<T>(Account account, Func<IImapGateway, Task<T>> work, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var gw = _factory.ForAccount(account.Id);
            if (!await gw.IsConnectedAsync(ct))
                await gw.ConnectAsync(account, ct);

            try
            {
                return await work(gw);
            }
            catch (Exception ex) when (ex is IOException or MailKit.ServiceNotConnectedException
                                       or MailKit.Net.Imap.ImapProtocolException)
            {
                // Server dropped an idle connection: reconnect once and retry.
                await gw.ConnectAsync(account, ct);
                return await work(gw);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task RunAsync(Account account, Func<IImapGateway, Task> work, CancellationToken ct = default) =>
        RunAsync<bool>(account, async gw => { await work(gw); return true; }, ct);
}
