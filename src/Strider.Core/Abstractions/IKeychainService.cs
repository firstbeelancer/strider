namespace Strider.Core.Abstractions;

/// <summary>
/// OS keychain service for secure credential storage.
/// </summary>
public interface IKeychainService
{
    Task SetSecretAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task DeleteSecretAsync(string key, CancellationToken ct = default);
    Task<bool> HasSecretAsync(string key, CancellationToken ct = default);
}
