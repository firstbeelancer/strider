namespace Strider.Core.Domain;

/// <summary>
/// AI provider settings.
/// </summary>
public class AiSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty; // openai/anthropic/openrouter/custom
    public string Model { get; set; } = string.Empty;
    public string? ApiKeyRef { get; set; } // reference to keychain
    public string? BaseUrl { get; set; } // for custom endpoints
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
