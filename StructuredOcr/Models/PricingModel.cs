namespace StructuredOcr.Models;

/// <summary>
/// Pricing rate for an AI model, expressed per 1 million tokens or per 1,000 pages.
/// </summary>
public record ModelPricing(
    string ModelId,
    string DisplayName,
    string Service,
    PricingUnit Unit,
    decimal InputCostPer1M,
    decimal OutputCostPer1M = 0m,
    string? Notes = null);

public enum PricingUnit
{
    Per1MTokens,
    Per1KPages
}

/// <summary>
/// Static pricing catalog — prices are guestimates as of March 2026.
/// Actual costs vary by region, commitment tier, and API version.
/// </summary>
public static class PricingCatalog
{
    public const string Disclaimer =
        "Estimated cost based on published pay-as-you-go rates (March 2026). " +
        "Actual costs may vary by region, commitment tier, caching, and API version.";

    // Assumptions for LLM Vision cost estimation
    public const int ImageTokensPerPage = 1105;   // High-detail: 85 + 170×6 tiles for 1024×1536
    public const int SystemPromptTokens = 200;     // Schema injection overhead
    public const int OutputTokensPerPage = 500;    // Average structured extraction response

    public static readonly Dictionary<string, ModelPricing> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        // Azure OpenAI — per 1M tokens
        ["gpt-4o"] = new("gpt-4o", "GPT-4o", "LLM Vision",
            PricingUnit.Per1MTokens, 2.50m, 10.00m),
        ["gpt-4o-mini"] = new("gpt-4o-mini", "GPT-4o Mini", "LLM Vision",
            PricingUnit.Per1MTokens, 0.15m, 0.60m),
        ["gpt-4.1"] = new("gpt-4.1", "GPT-4.1", "LLM Vision",
            PricingUnit.Per1MTokens, 2.00m, 8.00m),
        ["gpt-4.1-mini"] = new("gpt-4.1-mini", "GPT-4.1 Mini", "LLM Vision",
            PricingUnit.Per1MTokens, 0.40m, 1.60m),
        ["gpt-4.1-nano"] = new("gpt-4.1-nano", "GPT-4.1 Nano", "LLM Vision",
            PricingUnit.Per1MTokens, 0.10m, 0.40m),
        ["gpt-5"] = new("gpt-5", "GPT-5", "LLM Vision",
            PricingUnit.Per1MTokens, 1.25m, 10.00m),
        ["gpt-5-mini"] = new("gpt-5-mini", "GPT-5 Mini", "LLM Vision",
            PricingUnit.Per1MTokens, 0.25m, 2.00m),

        // Mistral Document AI — per 1K pages
        ["mistral-document-ai-2512"] = new("mistral-document-ai-2512", "Mistral Document AI", "Mistral",
            PricingUnit.Per1KPages, 2.00m, Notes: "Standard API, $2/1K pages"),

        // Azure Content Understanding — per 1K pages (combined components)
        ["cu-documentSearch"] = new("cu-documentSearch", "Content Understanding (documentSearch)", "Content Understanding",
            PricingUnit.Per1KPages, 6.00m, Notes: "prebuilt-documentSearch: extraction $5 + contextualization $1"),
        ["cu-markdown"] = new("cu-markdown", "Content Understanding (Markdown)", "Content Understanding",
            PricingUnit.Per1KPages, 6.00m, Notes: "Extraction $5 + contextualization $1"),
        ["cu-structured"] = new("cu-structured", "Content Understanding (Structured)", "Content Understanding",
            PricingUnit.Per1KPages, 20.14m, Notes: "Extraction $5 + ctx $1 + field extraction $14.14"),
        ["cu-classify"] = new("cu-classify", "Content Understanding (Classify)", "Content Understanding",
            PricingUnit.Per1KPages, 6.00m, Notes: "Extraction $5 + contextualization $1"),
    };

    /// <summary>Returns all models for a given service name.</summary>
    public static IEnumerable<ModelPricing> GetModelsForService(string serviceName)
        => Models.Values.Where(m => m.Service.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns all distinct Azure OpenAI models for display in the calculator.</summary>
    public static IEnumerable<ModelPricing> GetLlmVisionModels()
        => Models.Values.Where(m => m.Service == "LLM Vision");
}
