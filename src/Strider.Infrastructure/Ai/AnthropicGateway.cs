using System.Net.Http.Json;
using System.Text.Json;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// Anthropic Claude API gateway implementation.
/// </summary>
public class AnthropicGateway : IAiGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public AnthropicGateway(string apiKey, string baseUrl = "https://api.anthropic.com/v1")
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<AiResponse> ChatAsync(IEnumerable<AiMessage> messages, AiRequestOptions options, CancellationToken ct = default)
    {
        // Convert messages to Anthropic format
        var systemMessage = messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
        var chatMessages = messages
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var request = new
        {
            model = options.Model,
            max_tokens = options.MaxTokens,
            system = systemMessage,
            messages = chatMessages,
        };

        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/messages", request, ct);
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
        // Anthropic doesn't have a models endpoint, return known models
        var models = new List<AiModel>
        {
            new() { Id = "claude-3-5-sonnet-20241022", Name = "Claude 3.5 Sonnet" },
            new() { Id = "claude-3-5-haiku-20241022", Name = "Claude 3.5 Haiku" },
            new() { Id = "claude-3-opus-20240229", Name = "Claude 3 Opus" },
        };

        return Task.FromResult<IReadOnlyList<AiModel>>(models);
    }

    public Task<AiUsage> EstimateCostAsync(IEnumerable<AiMessage> messages, string model, CancellationToken ct = default)
    {
        var totalChars = messages.Sum(m => m.Content.Length);
        var estimatedTokens = totalChars / 4;

        var (inputPrice, outputPrice) = model.ToLowerInvariant() switch
        {
            var m when m.Contains("sonnet") => (0.003m, 0.015m),
            var m when m.Contains("haiku") => (0.00025m, 0.00125m),
            var m when m.Contains("opus") => (0.015m, 0.075m),
            _ => (0.003m, 0.015m),
        };

        var estimatedCost = (estimatedTokens / 1000m) * inputPrice;

        return Task.FromResult(new AiUsage
        {
            InputTokens = estimatedTokens,
            EstimatedCost = estimatedCost,
        });
    }
}
