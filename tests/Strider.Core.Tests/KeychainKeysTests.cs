using Strider.Core.Abstractions;
using Strider.Core.Domain;
using FluentAssertions;

namespace Strider.Core.Tests;

/// <summary>
/// Tests for KeychainKeys conventions and Account keychain extensions.
/// Validates that secrets flow through the keychain, never through the
/// database (F-008 fix).
/// </summary>
public class KeychainKeysTests
{
    [Fact]
    public void Password_Key_HasCanonicalFormat()
    {
        var id = Guid.Parse("aabbccdd-0000-0000-0000-000000000001");
        KeychainKeys.Password(id).Should().Be("strider:aabbccdd-0000-0000-0000-000000000001:password");
    }

    [Fact]
    public void OAuthAccessToken_Key_HasCanonicalFormat()
    {
        var id = Guid.Parse("aabbccdd-0000-0000-0000-000000000002");
        KeychainKeys.OAuthAccessToken(id).Should().Be("strider:aabbccdd-0000-0000-0000-000000000002:oauth_token");
    }

    [Fact]
    public void OAuthRefreshToken_Key_HasCanonicalFormat()
    {
        var id = Guid.Parse("aabbccdd-0000-0000-0000-000000000003");
        KeychainKeys.OAuthRefreshToken(id).Should().Be("strider:aabbccdd-0000-0000-0000-000000000003:oauth_refresh");
    }

    [Fact]
    public void AiApiKey_Key_HasCanonicalFormat()
    {
        var id = Guid.Parse("aabbccdd-0000-0000-0000-000000000004");
        KeychainKeys.AiApiKey(id).Should().Be("strider:ai:aabbccdd-0000-0000-0000-000000000004:api_key");
    }

    [Fact]
    public void DatabaseKey_IsConstant()
    {
        KeychainKeys.DatabaseKey().Should().Be("strider:database:key");
    }

    [Fact]
    public void PgpPassphrase_Key_HasCanonicalFormat()
    {
        var id = Guid.Parse("aabbccdd-0000-0000-0000-000000000005");
        KeychainKeys.PgpPassphrase(id).Should().Be("strider:pgp:aabbccdd-0000-0000-0000-000000000005:passphrase");
    }

    [Fact]
    public async Task SetOAuth2CredentialsAsync_SetsRefToKeychainKey_NotTokenItself()
    {
        // Arrange — capture all SetSecretAsync calls
        var capturedCalls = new List<(string Key, string Value)>();
        var keychain = new CapturingKeychainService(
            onSet: (k, v) => capturedCalls.Add((k, v)));
        var account = new Account { Id = Guid.NewGuid(), Email = "alice@example.com" };

        // Act
        await account.SetOAuth2CredentialsAsync(keychain, "ACCESS_TOKEN_VALUE", "REFRESH_TOKEN_VALUE");

        // Assert — the OAuth2TokenRef field stores the KEYCHAIN KEY, not the token
        account.OAuth2TokenRef.Should().Be(KeychainKeys.OAuthAccessToken(account.Id));
        account.OAuth2TokenRef.Should().NotBe("ACCESS_TOKEN_VALUE");

        // The actual access token is stored in keychain under that key
        capturedCalls.Should().Contain(c =>
            c.Key == KeychainKeys.OAuthAccessToken(account.Id) && c.Value == "ACCESS_TOKEN_VALUE");
        capturedCalls.Should().Contain(c =>
            c.Key == KeychainKeys.OAuthRefreshToken(account.Id) && c.Value == "REFRESH_TOKEN_VALUE");
    }

    [Fact]
    public async Task SetPasswordCredentialsAsync_ClearsOAuth2Ref()
    {
        var keychain = new CapturingKeychainService();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "alice@example.com",
            OAuth2TokenRef = "strider:old:oauth_token", // was using OAuth2
        };

        await account.SetPasswordCredentialsAsync(keychain, "my_password");

        account.OAuth2TokenRef.Should().BeNull("plain password mode should clear OAuth2 ref");
    }

    [Fact]
    public async Task ClearAllCredentialsAsync_RemovesAllKeychainEntries()
    {
        var deletedKeys = new List<string>();
        var keychain = new CapturingKeychainService(onDelete: k => deletedKeys.Add(k));
        var account = new Account { Id = Guid.NewGuid(), Email = "alice@example.com" };

        await account.ClearAllCredentialsAsync(keychain);

        deletedKeys.Should().Contain(KeychainKeys.Password(account.Id));
        deletedKeys.Should().Contain(KeychainKeys.OAuthAccessToken(account.Id));
        deletedKeys.Should().Contain(KeychainKeys.OAuthRefreshToken(account.Id));
        account.OAuth2TokenRef.Should().BeNull();
    }

    /// <summary>
    /// IKeychainService implementation that captures all calls for assertions.
    /// </summary>
    private sealed class CapturingKeychainService : IKeychainService
    {
        private readonly Action<string, string>? _onSet;
        private readonly Action<string>? _onDelete;
        private readonly Dictionary<string, string> _store = new();

        public CapturingKeychainService(
            Action<string, string>? onSet = null,
            Action<string>? onDelete = null)
        {
            _onSet = onSet;
            _onDelete = onDelete;
        }

        public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
        {
            _store[key] = value;
            _onSet?.Invoke(key, value);
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        }

        public Task DeleteSecretAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            _onDelete?.Invoke(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSecretAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_store.ContainsKey(key));
        }
    }
}
