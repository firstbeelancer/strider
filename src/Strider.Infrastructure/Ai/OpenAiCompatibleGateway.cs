using System.Net.Http.Json;
using System.Text.Json;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible AI gateway implementation.
/// Works with OpenAI, OpenRouter, and any OpenAI-compatible endpoint.
///
/// F-011 fix: HttpClient is now injected via DI (IHttpClientFactory), not
/// created with `new HttpClient()`. This prevents socket exhaustion under
/// load. The API key is read from IKeychainService on each request — never
/// cached in memory, never in a constructor parameter.
/// </summary>
public class OpenAiCompatibleGateway : IAiGateway
{
    private readonly HttpClient _httpClient;
    private readonly IKeychainService _keychain;
    private readonly string _baseUrl;
    private readonly Func<string?> _apiKeyRefProvider;

    /// <summary>
    /// Constructor for DI. The apiKeyRefProvider returns the keychain key
    /// under which the actual API key is stored (e.g., "strider:ai:{guid}:api_key").
    /// This indirection lets the user change the API key in Settings without
    /// recreating the gateway.
    /// </summary>
    public OpenAiCompatibleGateway(
        HttpClient httpClient,
        IKeychainService keychain,
        string baseUrl = "https://api.openai.com/v1",
        Func<string?>? apiKeyRefProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _keychain = keychain ?? throw new ArgumentNullException(nameof(keychain));
        _baseUrl = (baseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        _apiKeyRefProvider = apiKeyRefProvider ?? (() => null);
    }

    public async Task<AiResponse> ChatAsync(IEnumerable<AiMessage> messages, AiRequestOptions options, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");

        // Attach API key per-request (read from keychain on demand)
        var apiKey = await GetApiKeyAsync(ct);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        var body = new
        {
            model = options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = options.Temperature,
            max_tokens = options.MaxTokens,
        };
        request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var content = json
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        var usage = json.GetProperty("usage");

        return new AiResponse
        {
            Content = content,
            Usage = new AiUsage
            {
                InputTokens = usage.GetProperty("prompt_tokens").GetInt32(),
                OutputTokens = usage.GetProperty("completion_tokens").GetInt32(),
            },
        };
    }

    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
        var apiKey = await GetApiKeyAsync(ct);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var models = new List<AiModel>();

        foreach (var model in json.GetProperty("data").EnumerateArray())
        {
            models.Add(new AiModel
            {
                Id = model.GetProperty("id").GetString() ?? "",
                Name = model.GetProperty("id").GetString() ?? "",
            });
        }

        return models;
    }

    public async Task<AiUsage> EstimateCostAsync(IEnumerable<AiMessage> messages, string model, CancellationToken ct = default)
    {
        // Estimate based on token count (rough: 4 chars = 1 token)
        var totalChars = messages.Sum(m => m.Content.Length);
        var estimatedTokens = totalChars / 4;

        // Pricing per 1K tokens (approximate, as of 2026-06)
        var (inputPrice, outputPrice) = model.ToLowerInvariant() switch
        {
            var m when m.Contains("gpt-4o-mini") => (0.00015m, 0.0006m),
            var m when m.Contains("gpt-4o") => (0.005m, 0.015m),
            var m when m.Contains("gpt-4") => (0.03m, 0.06m),
            var m when m.Contains("claude-haiku") => (0.00025m, 0.00125m),
            var m when m.Contains("claude-sonnet") => (0.003m, 0.015m),
            _ => (0.001m, 0.002m),
        };

        var estimatedCost = (estimatedTokens / 1000m) * inputPrice;

        return await Task.FromResult(new AiUsage
        {
            InputTokens = estimatedTokens,
            EstimatedCost = estimatedCost,
        });
    }

    private async Task<string?> GetApiKeyAsync(CancellationToken ct)
    {
        var keyRef = _apiKeyRefProvider();
        if (string.IsNullOrEmpty(keyRef)) return null;
        return await _keychain.GetSecretAsync(keyRef, ct);
    }
}
