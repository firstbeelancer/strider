using System.Collections.Concurrent;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Mail;

/// <summary>
/// Factory for per-account IMAP gateways.
/// Each account gets its own MailKitImapGateway instance with its own ImapClient,
/// enabling parallel connections to different accounts without blocking.
/// </summary>
public interface IImapGatewayFactory
{
    /// <summary>
    /// Returns an IImapGateway bound to the given account.
    /// Subsequent calls with the same accountId return the same instance.
    /// </summary>
    IImapGateway ForAccount(Guid accountId);

    /// <summary>
    /// Disposes and removes the gateway for the given account.
    /// Call this when an account is deleted.
    /// </summary>
    void Release(Guid accountId);
}

/// <summary>
/// Factory for per-account SMTP gateways.
/// SMTP connections are short-lived (connect → send → disconnect), so we return
/// a fresh instance each time. The factory provides shared dependencies.
/// </summary>
public interface ISmtpGatewayFactory
{
    ISmtpGateway Create();
}

public sealed class MailKitImapGatewayFactory : IImapGatewayFactory, IDisposable
{
    private readonly IKeychainService _keychain;
    private readonly IAccountStore _accountStore;
    private readonly ConcurrentDictionary<Guid, MailKitImapGateway> _gateways = new();

    public MailKitImapGatewayFactory(IKeychainService keychain, IAccountStore accountStore)
    {
        _keychain = keychain;
        _accountStore = accountStore;
    }

    public IImapGateway ForAccount(Guid accountId)
    {
        return _gateways.GetOrAdd(accountId, id =>
            new MailKitImapGateway(_keychain, _accountStore, id));
    }

    public void Release(Guid accountId)
    {
        if (_gateways.TryRemove(accountId, out var gw))
        {
            gw.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _gateways)
        {
            kvp.Value.Dispose();
        }
        _gateways.Clear();
    }
}
