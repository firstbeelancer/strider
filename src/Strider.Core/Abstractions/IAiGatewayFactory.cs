using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Creates an <see cref="IAiGateway"/> for a configured provider.
/// The gateway reads the API key from the keychain on every request via
/// <paramref name="apiKeyRef"/>, so rotating a key needs no new gateway.
/// </summary>
public interface IAiGatewayFactory
{
    IAiGateway Create(string provider, string? apiKeyRef, string? baseUrl = null);

    IAiGateway Create(AiSettings settings) =>
        Create(settings.Provider, settings.ApiKeyRef, settings.BaseUrl);
}
