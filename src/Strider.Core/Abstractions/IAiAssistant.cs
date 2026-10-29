using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// High-level AI features (ТЗ §5.1 F5.4–F5.8): thread summary, reply drafting,
/// classification, rewriting. Reads provider settings from
/// <see cref="IAiSettingsStore"/> and the API key from the keychain.
///
/// Every call throws <see cref="AiNotConfiguredException"/> when no provider
/// is configured, so the UI can degrade gracefully instead of hanging.
/// </summary>
public interface IAiAssistant
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<AiSettings?> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Summarises a thread (2–4 bullets: decisions, action items, deadlines).</summary>
    Task<AiResult> SummarizeThreadAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);

    /// <summary>Drafts a reply to <paramref name="original"/> in the given style.</summary>
    Task<AiResult> DraftReplyAsync(Message original, string? style = null, CancellationToken ct = default);

    /// <summary>Returns one of: Work / Personal / Newsletter / Transactional / Action Required / Spam-like.</summary>
    Task<AiResult> ClassifyAsync(Message message, CancellationToken ct = default);

    /// <summary>Grammar/clarity pass over a draft.</summary>
    Task<AiResult> ImproveWritingAsync(string text, CancellationToken ct = default);

    /// <summary>Sends the smallest possible request to verify provider + key.</summary>
    Task<AiConnectionTest> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>AI text plus the token/cost accounting shown in the UI (F5.8).</summary>
public sealed class AiResult
{
    public string Text { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal EstimatedCostUsd { get; init; }

    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Short one-line usage label, e.g. "1 234 tok · $0.0004".</summary>
    public string UsageLabel =>
        EstimatedCostUsd > 0
            ? $"{TotalTokens} tok · ${EstimatedCostUsd:0.0000}"
            : $"{TotalTokens} tok";
}

public sealed class AiConnectionTest
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Model { get; init; }
}

/// <summary>Thrown when the user has not configured an AI provider yet.</summary>
public class AiNotConfiguredException : Exception
{
    public AiNotConfiguredException()
        : base("AI is not configured. Add a provider and API key in Settings → AI.")
    {
    }

    public AiNotConfiguredException(string message) : base(message)
    {
    }
}
