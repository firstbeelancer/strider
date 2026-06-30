using System.Net.Http.Json;
using System.Text.Json;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// Anthropic Claude API gateway implementation.
///
/// F-011 fix: HttpClient is now injected via DI (IHttpClientFactory), not
/// created with `new HttpClient()`. The API key is read from IKeychainService
/// on each request.
/// </summary>
public class AnthropicGateway : IAiGateway
{
    private readonly HttpClient _httpClient;
    private readonly IKeychainService _keychain;
    private readonly string _baseUrl;
    private readonly Func<string?> _apiKeyRefProvider;

    public AnthropicGateway(
        HttpClient httpClient,
        IKeychainService keychain,
        string baseUrl = "https://api.anthropic.com/v1",
        Func<string?>? apiKeyRefProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _keychain = keychain ?? throw new ArgumentNullException(nameof(keychain));
        _baseUrl = (baseUrl ?? "https://api.anthropic.com/v1").TrimEnd('/');
        _apiKeyRefProvider = apiKeyRefProvider ?? (() => null);
    }

    public async Task<AiResponse> ChatAsync(IEnumerable<AiMessage> messages, AiRequestOptions options, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/messages");

        // Attach Anthropic headers per-request
        var apiKey = await GetApiKeyAsync(ct);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("x-api-key", apiKey);
        }
        request.Headers.Add("anthropic-version", "2023-06-01");

        // Convert messages to Anthropic format
        var systemMessage = messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
        var chatMessages = messages
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var body = new
        {
            model = options.Model,
            max_tokens = options.MaxTokens,
            system = systemMessage,
            messages = chatMessages,
        };
        request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var content = json
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        var usage = json.GetProperty("usage");

        return new AiResponse
        {
            Content = content,
            Usage = new AiUsage
            {
                InputTokens = usage.GetProperty("input_tokens").GetInt32(),
                OutputTokens = usage.GetProperty("output_tokens").GetInt32(),
            },
        };
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        // Anthropic doesn't have a public models list endpoint; return hardcoded list.
        var models = new List<AiModel>
        {
            new() { Id = "claude-3-5-sonnet-20241022", Name = "Claude 3.5 Sonnet" },
            new() { Id = "claude-3-5-haiku-20241022", Name = "Claude 3.5 Haiku" },
            new() { Id = "claude-3-opus-20240229", Name = "Claude 3 Opus" },
        };
        return Task.FromResult<IReadOnlyList<AiModel>>(models);
    }

    public async Task<AiUsage> EstimateCostAsync(IEnumerable<AiMessage> messages, string model, CancellationToken ct = default)
    {
        var totalChars = messages.Sum(m => m.Content.Length);
        var estimatedTokens = totalChars / 4;

        var (inputPrice, _) = model.ToLowerInvariant() switch
        {
            var m when m.Contains("claude-haiku") => (0.00025m, 0.00125m),
            var m when m.Contains("claude-sonnet") => (0.003m, 0.015m),
            var m when m.Contains("claude-opus") => (0.015m, 0.075m),
            _ => (0.001m, 0.002m),
        };

        return await Task.FromResult(new AiUsage
        {
            InputTokens = estimatedTokens,
            EstimatedCost = (estimatedTokens / 1000m) * inputPrice,
        });
    }

    private async Task<string?> GetApiKeyAsync(CancellationToken ct)
    {
        var keyRef = _apiKeyRefProvider();
        if (string.IsNullOrEmpty(keyRef)) return null;
        return await _keychain.GetSecretAsync(keyRef, ct);
    }
}
