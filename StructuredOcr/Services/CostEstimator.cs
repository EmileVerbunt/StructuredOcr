using StructuredOcr.Models;

namespace StructuredOcr.Services;

/// <summary>
/// Estimates inference cost based on token/page usage and published pricing.
/// All estimates are guestimates — see <see cref="PricingCatalog.Disclaimer"/>.
/// </summary>
public class CostEstimator
{
    /// <summary>
    /// Estimates cost for an LLM Vision result using input/output token split.
    /// Falls back to total_tokens with a 70/30 input/output split assumption.
    /// </summary>
    public decimal? EstimateLlmVisionCost(OcrResult result, string? deploymentName)
    {
        string modelId = ResolveModelId(deploymentName);
        if (!PricingCatalog.Models.TryGetValue(modelId, out var pricing))
            return null;

        int inputTokens = result.InputTokens ?? (int)((result.TokensUsed ?? 0) * 0.7);
        int outputTokens = result.OutputTokens ?? (int)((result.TokensUsed ?? 0) * 0.3);

        if (inputTokens == 0 && outputTokens == 0)
            return null;

        decimal cost = (inputTokens / 1_000_000m * pricing.InputCostPer1M)
                     + (outputTokens / 1_000_000m * pricing.OutputCostPer1M);

        result.EstimatedCostUsd = cost;
        result.CostModelId = modelId;
        return cost;
    }

    /// <summary>
    /// Estimates cost for Mistral Document AI based on pages processed.
    /// </summary>
    public decimal? EstimateMistralCost(OcrResult result)
    {
        int pages = result.PageCount ?? result.TokensUsed ?? 0;
        if (pages == 0) return null;

        if (!PricingCatalog.Models.TryGetValue("mistral-document-ai-2512", out var pricing))
            return null;

        decimal cost = pages / 1000m * pricing.InputCostPer1M;
        result.EstimatedCostUsd = cost;
        result.CostModelId = "mistral-document-ai-2512";
        return cost;
    }

    /// <summary>
    /// Estimates cost for Azure Content Understanding based on page count and strategy.
    /// </summary>
    public decimal? EstimateContentUnderstandingCost(OcrResult result)
    {
        int pages = result.PageCount ?? result.Pages.Count;
        if (pages == 0) return null;

        string modelId = result.Strategy switch
        {
            "ExtractMarkdown" => "cu-documentSearch",
            "ExtractStructured" => "cu-structured",
            "Classify" => "cu-classify",
            _ => "cu-documentSearch"
        };

        if (!PricingCatalog.Models.TryGetValue(modelId, out var pricing))
            return null;

        decimal cost = pages / 1000m * pricing.InputCostPer1M;
        result.EstimatedCostUsd = cost;
        result.CostModelId = modelId;
        return cost;
    }

    /// <summary>
    /// Static estimation for the cost calculator page (no actual result needed).
    /// </summary>
    public static decimal EstimateForCalculator(string modelId, int pageCount, string? strategy = null)
    {
        if (!PricingCatalog.Models.TryGetValue(modelId, out var pricing))
            return 0;

        if (pricing.Unit == PricingUnit.Per1KPages)
            return pageCount / 1000m * pricing.InputCostPer1M;

        // Token-based (LLM Vision): estimate tokens from page count
        int inputTokens = PricingCatalog.SystemPromptTokens
                        + (pageCount * PricingCatalog.ImageTokensPerPage);
        int outputTokens = pageCount * PricingCatalog.OutputTokensPerPage;

        return (inputTokens / 1_000_000m * pricing.InputCostPer1M)
             + (outputTokens / 1_000_000m * pricing.OutputCostPer1M);
    }

    /// <summary>
    /// Returns the estimated cost for a single page, normalizing all pricing to per-page.
    /// For token-based models, uses the standard assumptions (image tokens + output tokens).
    /// For page-based models, divides the per-1K rate by 1000.
    /// </summary>
    public static decimal CostPerPage(string modelId)
    {
        if (!PricingCatalog.Models.TryGetValue(modelId, out var pricing))
            return 0;

        if (pricing.Unit == PricingUnit.Per1KPages)
            return pricing.InputCostPer1M / 1000m;

        // Token-based: translate 1 page to tokens, then to cost
        int inputTokens = PricingCatalog.ImageTokensPerPage;
        int outputTokens = PricingCatalog.OutputTokensPerPage;

        return (inputTokens / 1_000_000m * pricing.InputCostPer1M)
             + (outputTokens / 1_000_000m * pricing.OutputCostPer1M);
    }

    /// <summary>
    /// Maps a deployment name (e.g. "my-gpt4o-deployment") to a known model ID.
    /// Uses simple substring matching — defaults to gpt-4o if unrecognized.
    /// </summary>
    private static string ResolveModelId(string? deploymentName)
    {
        if (string.IsNullOrWhiteSpace(deploymentName))
            return "gpt-4o";

        string name = deploymentName.ToLowerInvariant();

        if (name.Contains("4o-mini") || name.Contains("4o_mini")) return "gpt-4o-mini";
        if (name.Contains("4o")) return "gpt-4o";
        if (name.Contains("4.1-nano") || name.Contains("4.1_nano")) return "gpt-4.1-nano";
        if (name.Contains("4.1-mini") || name.Contains("4.1_mini")) return "gpt-4.1-mini";
        if (name.Contains("4.1")) return "gpt-4.1";
        if (name.Contains("5-mini") || name.Contains("5_mini")) return "gpt-5-mini";
        if (name.Contains("gpt-5") || name.Contains("gpt5")) return "gpt-5";

        return "gpt-4o"; // default fallback
    }
}
