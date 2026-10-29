using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// Resolves the right <see cref="IAiGateway"/> for a configured provider and
/// hands it the keychain reference — never the key itself.
///
/// Moved here from Strider.UI (Wave 5): the AI assistant lives in the
/// Infrastructure layer and must not depend on the UI project.
/// </summary>
public sealed class AiGatewayFactory : IAiGatewayFactory
{
    private readonly IKeychainService _keychain;
    private readonly IHttpClientFactory _httpClientFactory;

    public AiGatewayFactory(IKeychainService keychain, IHttpClientFactory httpClientFactory)
    {
        _keychain = keychain;
        _httpClientFactory = httpClientFactory;
    }

    public IAiGateway Create(string provider, string? apiKeyRef, string? baseUrl = null)
    {
        Func<string?> keyRef = () => apiKeyRef;

        return (provider ?? "").ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicGateway(
                _httpClientFactory.CreateClient(nameof(AnthropicGateway)),
                _keychain,
                (baseUrl ?? "https://api.anthropic.com/v1").TrimEnd('/'),
                keyRef),

            "openrouter" => new OpenAiCompatibleGateway(
                _httpClientFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                _keychain,
                (baseUrl ?? "https://openrouter.ai/api/v1").TrimEnd('/'),
                keyRef),

            "custom" => new OpenAiCompatibleGateway(
                _httpClientFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                _keychain,
                string.IsNullOrWhiteSpace(baseUrl)
                    ? throw new ArgumentException("A custom provider needs a base URL.", nameof(baseUrl))
                    : baseUrl.TrimEnd('/'),
                keyRef),

            _ => new OpenAiCompatibleGateway(
                _httpClientFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                _keychain,
                (baseUrl ?? "https://api.openai.com/v1").TrimEnd('/'),
                keyRef),
        };
    }
}
