using System.Text;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Security;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// The AI features from the ТЗ (F5.4–F5.8), built on top of the provider
/// gateways: summarise a thread, draft a reply, classify a message, polish a
/// draft. Settings come from <see cref="IAiSettingsStore"/>, the API key from
/// the OS keychain — nothing is cached in memory and no mail leaves the
/// machine unless the user configured a provider.
/// </summary>
public sealed class AiAssistant : IAiAssistant
{
    /// <summary>Cap on how much mail text goes into one prompt.</summary>
    public const int MaxPromptChars = 12_000;

    /// <summary>How many messages of a thread take part in a summary.</summary>
    public const int MaxThreadMessages = 12;

    private readonly IAiSettingsStore _settingsStore;
    private readonly IAiGatewayFactory _gatewayFactory;
    private readonly IMessageStore _messages;

    public AiAssistant(
        IAiSettingsStore settingsStore,
        IAiGatewayFactory gatewayFactory,
        IMessageStore messages)
    {
        _settingsStore = settingsStore;
        _gatewayFactory = gatewayFactory;
        _messages = messages;
    }

    public async Task<AiSettings?> GetSettingsAsync(CancellationToken ct = default) =>
        await _settingsStore.GetDefaultAsync(ct);

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var settings = await _settingsStore.GetDefaultAsync(ct);
        return settings is not null && !string.IsNullOrWhiteSpace(settings.Model);
    }

    public async Task<AiResult> SummarizeThreadAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) throw new ArgumentException("Nothing to summarise.", nameof(messages));

        var thread = await BuildThreadTextAsync(messages, MaxThreadMessages, ct);
        return await CompleteAsync(PromptTemplates.SummarizeThread(thread), maxTokens: 700, ct);
    }

    public async Task<AiResult> DraftReplyAsync(Message original, string? style = null, CancellationToken ct = default)
    {
        var body = await _messages.GetBodyAsync(original.Id, ct);
        var text = BodyText(body) ?? original.Subject;
        var prompt = PromptTemplates.DraftReply(
            $"""
             From: {original.FromName ?? original.FromAddress}
             Subject: {original.Subject}
             Date: {original.DateUtc:u}

             {Clip(text)}
             """,
            string.IsNullOrWhiteSpace(style) ? "professional, concise" : style);

        return await CompleteAsync(prompt, maxTokens: 800, ct);
    }

    public async Task<AiResult> ClassifyAsync(Message message, CancellationToken ct = default)
    {
        var body = await _messages.GetBodyAsync(message.Id, ct);
        var prompt = PromptTemplates.ClassifyEmail(
            message.Subject,
            message.FromName is { Length: > 0 } name ? $"{name} <{message.FromAddress}>" : message.FromAddress,
            BodyText(body) ?? string.Empty);

        var result = await CompleteAsync(prompt, maxTokens: 20, ct);
        return new AiResult
        {
            Text = NormalizeCategory(result.Text),
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            EstimatedCostUsd = result.EstimatedCostUsd,
        };
    }

    public async Task<AiResult> ImproveWritingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Nothing to improve.", nameof(text));
        return await CompleteAsync(PromptTemplates.ImproveWriting(Clip(text)), maxTokens: 1200, ct);
    }

    public async Task<AiConnectionTest> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await _settingsStore.GetDefaultAsync(ct);
        if (settings is null)
            return new AiConnectionTest { Success = false, Message = "No AI provider configured." };

        try
        {
            var gateway = _gatewayFactory.Create(settings);
            var response = await gateway.ChatAsync(
                new[]
                {
                    new AiMessage { Role = "user", Content = "Reply with the single word: ok" },
                },
                new AiRequestOptions { Model = settings.Model, MaxTokens = 8, Temperature = 0 },
                ct);

            var ok = !string.IsNullOrWhiteSpace(response.Content);
            return new AiConnectionTest
            {
                Success = ok,
                Model = settings.Model,
                Message = ok
                    ? $"Connected to {settings.Provider} ({settings.Model})."
                    : "Provider answered with an empty response.",
            };
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "AI connection test failed");
            return new AiConnectionTest { Success = false, Message = $"Connection failed: {ex.Message}" };
        }
    }

    // === Internals ===

    private async Task<AiResult> CompleteAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        var settings = await _settingsStore.GetDefaultAsync(ct)
                       ?? throw new AiNotConfiguredException();

        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new AiNotConfiguredException("Pick a default model in Settings → AI.");

        var gateway = _gatewayFactory.Create(settings);
        var response = await gateway.ChatAsync(
            new[]
            {
                new AiMessage { Role = "system", Content = "You are the assistant inside the Strider Mail desktop email client. Answer in the language of the email." },
                new AiMessage { Role = "user", Content = prompt },
            },
            new AiRequestOptions { Model = settings.Model, MaxTokens = maxTokens, Temperature = 0.4 },
            ct);

        return new AiResult
        {
            Text = response.Content.Trim(),
            InputTokens = response.Usage.InputTokens,
            OutputTokens = response.Usage.OutputTokens,
            EstimatedCostUsd = ModelPricing.Estimate(
                settings.Model, response.Usage.InputTokens, response.Usage.OutputTokens),
        };
    }

    /// <summary>Oldest-first transcript of the thread, oldest messages dropped first.</summary>
    private async Task<string> BuildThreadTextAsync(
        IReadOnlyList<Message> messages, int maxMessages, CancellationToken ct)
    {
        var ordered = messages
            .OrderBy(m => m.DateUtc ?? DateTime.MinValue)
            .TakeLast(maxMessages)
            .ToList();

        var builder = new StringBuilder();
        foreach (var message in ordered)
        {
            var body = await _messages.GetBodyAsync(message.Id, ct);
            builder.AppendLine($"--- {message.DateUtc:u} | {message.FromName ?? message.FromAddress} ---");
            builder.AppendLine($"Subject: {message.Subject}");
            builder.AppendLine(Clip(BodyText(body) ?? "(body not downloaded)"));
            builder.AppendLine();
        }

        return Clip(builder.ToString(), MaxPromptChars);
    }

    private static string? BodyText(MessageBody? body)
    {
        if (body is null) return null;
        if (!string.IsNullOrWhiteSpace(body.TextPlain)) return body.TextPlain;
        if (!string.IsNullOrWhiteSpace(body.TextHtml)) return HtmlText.ToPlainText(body.TextHtml);
        return null;
    }

    private static string Clip(string? text, int max = 4_000)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max] + "\n[...truncated]";
    }

    private static readonly string[] KnownCategories =
    {
        "Work", "Personal", "Newsletter", "Transactional", "Action Required", "Spam-like",
    };

    /// <summary>
    /// Models like to answer "Category: Work" or "**Work**"; keep only the label
    /// we know, otherwise pass the answer through untouched.
    /// </summary>
    public static string NormalizeCategory(string raw)
    {
        var text = (raw ?? string.Empty).Trim().Trim('*', '.', '"', '\'', '\n', ' ');
        foreach (var category in KnownCategories)
        {
            if (text.Contains(category, StringComparison.OrdinalIgnoreCase)) return category;
        }
        return text.Length > 40 ? text[..40] : text;
    }
}
