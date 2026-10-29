using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Ai;

/// <summary>
/// Rough per-1K-token prices used for the cost readout (ТЗ F5.8).
/// Kept deliberately simple: the user sees an order of magnitude, not an invoice.
/// </summary>
public static class ModelPricing
{
    private static readonly (string Prefix, decimal Input, decimal Output)[] Table =
    {
        ("gpt-4o-mini", 0.00015m, 0.0006m),
        ("gpt-4o", 0.0025m, 0.010m),
        ("gpt-4.1-mini", 0.0004m, 0.0016m),
        ("gpt-4.1", 0.002m, 0.008m),
        ("claude-3-5-haiku", 0.0008m, 0.004m),
        ("claude-haiku", 0.0008m, 0.004m),
        ("claude-sonnet", 0.003m, 0.015m),
        ("claude-3-5-sonnet", 0.003m, 0.015m),
        ("claude-opus", 0.015m, 0.075m),
    };

    public static decimal Estimate(string? model, int inputTokens, int outputTokens)
    {
        var name = (model ?? "").ToLowerInvariant();
        foreach (var (prefix, inputPrice, outputPrice) in Table)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            return inputTokens / 1000m * inputPrice + outputTokens / 1000m * outputPrice;
        }

        // Unknown model: assume a mid-range price so the number is not misleadingly zero.
        return inputTokens / 1000m * 0.001m + outputTokens / 1000m * 0.003m;
    }
}
