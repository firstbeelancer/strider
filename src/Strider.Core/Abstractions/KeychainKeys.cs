using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Conventions for IKeychainService key names.
///
/// All Strider Mail secrets live in the OS keychain under deterministic keys
/// derived from the account ID. The keychain stores the actual secret value;
/// the database only stores the key name (a "reference") — never the secret.
///
/// This convention closes finding F-008 from the architecture review:
/// "OAuth2TokenRef stores the token itself, not a reference to keychain".
/// </summary>
public static class KeychainKeys
{
    /// <summary>Keychain key for an account's IMAP/SMTP password (plain auth).</summary>
    public static string Password(Guid accountId) => $"strider:{accountId}:password";

    /// <summary>Keychain key for an account's OAuth2 access token.</summary>
    public static string OAuthAccessToken(Guid accountId) => $"strider:{accountId}:oauth_token";

    /// <summary>Keychain key for an account's OAuth2 refresh token.</summary>
    public static string OAuthRefreshToken(Guid accountId) => $"strider:{accountId}:oauth_refresh";

    /// <summary>Keychain key for an AI provider's API key.</summary>
    public static string AiApiKey(Guid aiSettingsId) => $"strider:ai:{aiSettingsId}:api_key";

    /// <summary>Keychain key for the SQLite database encryption key (when SQLCipher is enabled).</summary>
    public static string DatabaseKey() => "strider:database:key";

    /// <summary>Keychain key for the PGP private key passphrase (session cache, optional).</summary>
    public static string PgpPassphrase(Guid pgpKeyId) => $"strider:pgp:{pgpKeyId}:passphrase";
}

/// <summary>
/// Helpers for setting up OAuth2 credentials on an Account.
/// </summary>
public static class AccountKeychainExtensions
{
    /// <summary>
    /// Configures this account to use OAuth2 by setting the OAuth2TokenRef
    /// to the canonical keychain key and storing the access token in the keychain.
    /// The refresh token (if any) is stored under a separate key.
    /// </summary>
    public static async Task SetOAuth2CredentialsAsync(
        this Account account,
        IKeychainService keychain,
        string accessToken,
        string? refreshToken = null,
        CancellationToken ct = default)
    {
        account.OAuth2TokenRef = KeychainKeys.OAuthAccessToken(account.Id);
        await keychain.SetSecretAsync(account.OAuth2TokenRef, accessToken, ct);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await keychain.SetSecretAsync(
                KeychainKeys.OAuthRefreshToken(account.Id), refreshToken, ct);
        }
    }

    /// <summary>
    /// Configures this account to use plain password auth by storing the
    /// password in the keychain under the canonical key. Clears any
    /// previously set OAuth2 reference.
    /// </summary>
    public static async Task SetPasswordCredentialsAsync(
        this Account account,
        IKeychainService keychain,
        string password,
        CancellationToken ct = default)
    {
        account.OAuth2TokenRef = null; // plain password, no OAuth2
        await keychain.SetSecretAsync(KeychainKeys.Password(account.Id), password, ct);
    }

    /// <summary>
    /// Removes all credentials (password + OAuth2 tokens) for this account
    /// from the keychain. Call this when deleting the account.
    /// </summary>
    public static async Task ClearAllCredentialsAsync(
        this Account account,
        IKeychainService keychain,
        CancellationToken ct = default)
    {
        await keychain.DeleteSecretAsync(KeychainKeys.Password(account.Id), ct);
        await keychain.DeleteSecretAsync(KeychainKeys.OAuthAccessToken(account.Id), ct);
        await keychain.DeleteSecretAsync(KeychainKeys.OAuthRefreshToken(account.Id), ct);
        account.OAuth2TokenRef = null;
    }
}
