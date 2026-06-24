namespace Strider.Core.Abstractions;

/// <summary>
/// AI gateway for LLM requests.
/// </summary>
public interface IAiGateway
{
    Task<AiResponse> ChatAsync(IEnumerable<AiMessage> messages, AiRequestOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
    Task<AiUsage> EstimateCostAsync(IEnumerable<AiMessage> messages, string model, CancellationToken ct = default);
}

public class AiMessage
{
    public string Role { get; set; } = "user"; // system/user/assistant
    public string Content { get; set; } = string.Empty;
}

public class AiRequestOptions
{
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
}

public class AiResponse
{
    public string Content { get; set; } = string.Empty;
    public AiUsage Usage { get; set; } = new();
}

public class AiModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal InputPricePer1k { get; set; }
    public decimal OutputPricePer1k { get; set; }
}

public class AiUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCost { get; set; }
}
