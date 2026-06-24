using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible AI gateway implementation.
/// Works with OpenAI, OpenRouter, and any OpenAI-compatible endpoint.
/// </summary>
public class OpenAiCompatibleGateway : IAiGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public OpenAiCompatibleGateway(string apiKey, string baseUrl = "https://api.openai.com/v1")
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<AiResponse> ChatAsync(IEnumerable<AiMessage> messages, AiRequestOptions options, CancellationToken ct = default)
    {
        var request = new
        {
            model = options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = options.Temperature,
            max_tokens = options.MaxTokens,
        };

        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/chat/completions", request, ct);
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
        var response = await _httpClient.GetAsync($"{_baseUrl}/models", ct);
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

    public Task<AiUsage> EstimateCostAsync(IEnumerable<AiMessage> messages, string model, CancellationToken ct = default)
    {
        // Estimate based on token count (rough: 4 chars = 1 token)
        var totalChars = messages.Sum(m => m.Content.Length);
        var estimatedTokens = totalChars / 4;

        // Pricing per 1K tokens (approximate)
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

        return Task.FromResult(new AiUsage
        {
            InputTokens = estimatedTokens,
            EstimatedCost = estimatedCost,
        });
    }
}
