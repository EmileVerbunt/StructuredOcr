namespace StructuredOcr.Models;

public class OcrResult
{
    public string ServiceName { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string RawMarkdown { get; set; } = string.Empty;
    public List<PageResult> Pages { get; set; } = [];
    public string? StructuredJson { get; set; }
    public string? DocumentType { get; set; }
    public double ElapsedMs { get; set; }
    public int? TokensUsed { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? PageCount { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string? CostModelId { get; set; }
    public string? RawServiceResponse { get; set; }
    public string? Error { get; set; }
    public bool IsSuccess => Error is null;

    // Enhanced fields for prebuilt-documentSearch transparency
    public string? Summary { get; set; }
    public int? TableCount { get; set; }
    public int? FigureCount { get; set; }
    public List<ModelUsageInfo> ModelsUsed { get; set; } = [];
}

public class PageResult
{
    public int PageIndex { get; set; }
    public string Markdown { get; set; } = string.Empty;
    public string? StructuredJson { get; set; }
}

/// <summary>
/// Describes an AI model used behind the scenes during analysis,
/// with its role and estimated token consumption.
/// </summary>
public record ModelUsageInfo(
    string ModelId,
    string DisplayName,
    string Role,
    int? EstimatedInputTokens = null,
    int? EstimatedOutputTokens = null);
