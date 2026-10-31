using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Services;

/// <summary>
/// Periodically syncs every account (F3.2, the polling mode). A failing account,
/// typically an offline one, is logged and retried on the next tick.
/// </summary>
public sealed class BackgroundSyncService : IDisposable
{
    private readonly IAccountStore _accounts;
    private readonly IMailSyncService _sync;
    private CancellationTokenSource? _cts;

    public BackgroundSyncService(IAccountStore accounts, IMailSyncService sync)
    {
        _accounts = accounts;
        _sync = sync;
    }

    /// <summary>Raised after an account was synced, off the UI thread: account id and new message count.</summary>
    public event Action<Guid, int>? Synced;

    public bool IsRunning => _cts is not null;

    /// <summary>(Re)starts the loop; a zero or negative interval turns it off.</summary>
    public void Start(TimeSpan interval)
    {
        Stop();
        if (interval <= TimeSpan.Zero) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoopAsync(interval, cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoopAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RunOnceAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        foreach (var account in await _accounts.GetAllAccountsAsync(ct))
        {
            try
            {
                var added = await _sync.SyncAccountAsync(account.Id, ct);
                Synced?.Invoke(account.Id, added);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Background sync failed for one account");
            }
        }
    }

    public void Dispose() => Stop();
}
