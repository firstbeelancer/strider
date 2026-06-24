using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Account persistence store.
/// </summary>
public interface IAccountStore
{
    Task SaveAccountAsync(Account account, CancellationToken ct = default);
    Task<Account?> GetAccountAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> GetAllAccountsAsync(CancellationToken ct = default);
    Task UpdateAccountAsync(Account account, CancellationToken ct = default);
    Task DeleteAccountAsync(Guid id, CancellationToken ct = default);

    // Folders
    Task SaveFolderAsync(Folder folder, CancellationToken ct = default);
    Task SaveFoldersAsync(IEnumerable<Folder> folders, CancellationToken ct = default);
    Task<Folder?> GetFolderAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> GetFoldersAsync(Guid accountId, CancellationToken ct = default);
    Task UpdateFolderAsync(Folder folder, CancellationToken ct = default);
    Task DeleteFolderAsync(Guid id, CancellationToken ct = default);
}
