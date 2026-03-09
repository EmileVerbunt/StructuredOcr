using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.ContentUnderstanding;
using StructuredOcr.Models;

namespace StructuredOcr.Services;

public class ContentUnderstandingOcrService : IOcrService
{
    private readonly ConfigStore _configStore;
    private readonly SchemaService _schemaService;
    private const string Service = "ContentUnderstanding";

    // Approximate chars-per-token ratio for English text
    private const double CharsPerToken = 4.0;

    public string ServiceName => "Content Understanding";
    public string[] Strategies => ["ExtractMarkdown", "Classify", "ExtractStructured"];

    public ContentUnderstandingOcrService(ConfigStore configStore, SchemaService schemaService)
    {
        _configStore = configStore;
        _schemaService = schemaService;
    }

    public async Task<OcrResult> AnalyzeAsync(Stream file, string fileName, string strategy, UnifiedSchema? schema, CancellationToken ct = default)
    {
        var config = _configStore.Get(Service);
        if (config is null || string.IsNullOrWhiteSpace(config.Endpoint) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Error("Content Understanding is not configured. Go to Settings.");
        }

        var client = new ContentUnderstandingClient(new Uri(config.Endpoint), new AzureKeyCredential(config.ApiKey));
        string analyzerId = config.ExtraSettings.GetValueOrDefault("AnalyzerId", "prebuilt-documentSearch");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var binaryData = BinaryData.FromBytes(ms.ToArray());

        var sw = Stopwatch.StartNew();
        try
        {
            var operation = await client.AnalyzeBinaryAsync(WaitUntil.Completed, analyzerId, binaryData);
            sw.Stop();

            var result = operation.Value;

            var markdownBuilder = new StringBuilder();
            var categories = new List<string>();
            var allFields = new Dictionary<string, object>();
            var pages = new List<PageResult>();
            string? summary = null;
            int tableCount = 0;
            int figureCount = 0;
            int totalMarkdownChars = 0;

            if (result.Contents is { Count: > 0 })
            {
                int pageIdx = 0;
                foreach (var content in result.Contents)
                {
                    var page = new PageResult { PageIndex = pageIdx };

                    if (!string.IsNullOrWhiteSpace(content.Markdown))
                    {
                        if (markdownBuilder.Length > 0) markdownBuilder.AppendLine("\n---\n");
                        markdownBuilder.Append(content.Markdown);
                        page.Markdown = content.Markdown;
                        totalMarkdownChars += content.Markdown.Length;
                    }

                    // Capture the summary from prebuilt-documentSearch (returned as a field named "Summary")
                    if (content.Fields is { Count: > 0 } && content.Fields.TryGetValue("Summary", out var summaryField))
                    {
                        string? summaryValue = summaryField?.Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(summaryValue))
                            summary ??= summaryValue;
                    }

                    if (!string.IsNullOrWhiteSpace(content.Category))
                    {
                        categories.Add(content.Category);
                    }

                    if (content.Fields is { Count: > 0 })
                    {
                        foreach (var field in content.Fields)
                        {
                            allFields[field.Key] = field.Value;
                        }
                        page.StructuredJson = JsonSerializer.Serialize(
                            content.Fields.ToDictionary(f => f.Key, f => f.Value?.ToString()),
                            new JsonSerializerOptions { WriteIndented = true });
                    }

                    // Extract rich document properties when available
                    if (content is DocumentContent documentContent)
                    {
                        if (documentContent.Tables is { Count: > 0 })
                            tableCount += documentContent.Tables.Count;
                        if (documentContent.Figures is { Count: > 0 })
                            figureCount += documentContent.Figures.Count;
                    }

                    pages.Add(page);
                    pageIdx++;
                }
            }

            string markdown = markdownBuilder.ToString();
            string? docType = categories.FirstOrDefault();
            string? structuredJson = null;

            if (strategy == "Classify")
            {
                // Use native category if available, otherwise classify from content
                var classification = docType != null
                    ? new ClassificationResult(docType, "high", "Classified by Content Understanding analyzer")
                    : ClassifyFromContent(markdown, summary);
                docType = classification.DocumentType;
                structuredJson = JsonSerializer.Serialize(new
                {
                    document_type = classification.DocumentType,
                    confidence = classification.Confidence,
                    reasoning = classification.Reasoning
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            else if (strategy == "ExtractStructured" && allFields.Count > 0)
            {
                structuredJson = JsonSerializer.Serialize(allFields, new JsonSerializerOptions { WriteIndented = true });
            }
            else if (strategy == "ExtractStructured" && categories.Count > 0)
            {
                structuredJson = JsonSerializer.Serialize(new { categories }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Estimate token usage from markdown output
            int estimatedInputTokens = (int)(totalMarkdownChars / CharsPerToken);
            int estimatedSummaryTokens = summary != null ? (int)(summary.Length / CharsPerToken) : 0;
            int estimatedEmbeddingTokens = estimatedInputTokens;

            // Try to extract actual usage from the operation's raw response
            int? actualContextTokens = null;
            Dictionary<string, int>? actualTokens = null;
            int? actualPages = null;
            TryExtractUsage(operation.GetRawResponse(), out actualContextTokens, out actualTokens, out actualPages);

            // Build models-used list for transparency
            var modelsUsed = BuildModelsUsed(analyzerId, estimatedInputTokens, estimatedSummaryTokens, estimatedEmbeddingTokens, actualContextTokens, actualTokens);

            int totalEstimatedTokens = actualContextTokens ?? (estimatedInputTokens + estimatedSummaryTokens);

            // Build raw response summary for debugging
            string rawResponse = BuildRawResponseSummary(result);

            return new OcrResult
            {
                ServiceName = ServiceName,
                Strategy = strategy,
                RawMarkdown = markdown,
                Pages = pages,
                PageCount = pages.Count > 0 ? pages.Count : null,
                StructuredJson = structuredJson,
                DocumentType = docType,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                TokensUsed = totalEstimatedTokens > 0 ? totalEstimatedTokens : null,
                InputTokens = estimatedInputTokens > 0 ? estimatedInputTokens : null,
                OutputTokens = estimatedSummaryTokens > 0 ? estimatedSummaryTokens : null,
                RawServiceResponse = rawResponse,
                Summary = summary,
                TableCount = tableCount > 0 ? tableCount : null,
                FigureCount = figureCount > 0 ? figureCount : null,
                ModelsUsed = modelsUsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Error($"{ex.GetType().Name}: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static List<ModelUsageInfo> BuildModelsUsed(
        string analyzerId,
        int estimatedInputTokens,
        int estimatedSummaryTokens,
        int estimatedEmbeddingTokens,
        int? actualContextTokens,
        Dictionary<string, int>? actualTokens)
    {
        var models = new List<ModelUsageInfo>();

        if (analyzerId.Contains("documentSearch", StringComparison.OrdinalIgnoreCase)
            || analyzerId.Contains("imageSearch", StringComparison.OrdinalIgnoreCase)
            || analyzerId.Contains("audioSearch", StringComparison.OrdinalIgnoreCase)
            || analyzerId.Contains("videoSearch", StringComparison.OrdinalIgnoreCase))
        {
            // Use actual token data if available from the API response
            int? gptInput = actualTokens?.GetValueOrDefault("gpt-4.1-mini.input");
            int? gptCachedInput = actualTokens?.GetValueOrDefault("gpt-4.1-mini.cached_input");
            int? gptOutput = actualTokens?.GetValueOrDefault("gpt-4.1-mini.output");
            int? embInput = actualTokens?.GetValueOrDefault("text-embedding-3-large.input");

            models.Add(new ModelUsageInfo(
                "gpt-4.1-mini",
                "GPT-4.1 Mini",
                "Summarization & contextualization",
                EstimatedInputTokens: gptInput ?? (estimatedInputTokens > 0 ? estimatedInputTokens : null),
                EstimatedOutputTokens: gptOutput ?? (estimatedSummaryTokens > 0 ? estimatedSummaryTokens : null)));

            models.Add(new ModelUsageInfo(
                "text-embedding-3-large",
                "Text Embedding 3 Large",
                "Semantic embedding generation",
                EstimatedInputTokens: embInput ?? (estimatedEmbeddingTokens > 0 ? estimatedEmbeddingTokens : null),
                EstimatedOutputTokens: null));
        }
        else if (analyzerId.Contains("invoice", StringComparison.OrdinalIgnoreCase)
            || analyzerId.Contains("receipt", StringComparison.OrdinalIgnoreCase))
        {
            models.Add(new ModelUsageInfo(
                "gpt-4.1",
                "GPT-4.1",
                "Field extraction & analysis",
                EstimatedInputTokens: estimatedInputTokens > 0 ? estimatedInputTokens : null,
                EstimatedOutputTokens: estimatedSummaryTokens > 0 ? estimatedSummaryTokens : null));

            models.Add(new ModelUsageInfo(
                "text-embedding-3-large",
                "Text Embedding 3 Large",
                "Semantic embedding generation",
                EstimatedInputTokens: estimatedEmbeddingTokens > 0 ? estimatedEmbeddingTokens : null,
                EstimatedOutputTokens: null));
        }

        return models;
    }

    /// <summary>
    /// Attempts to extract usage details from the raw HTTP response JSON.
    /// The API returns usage.contextualizationTokens and usage.tokens (grouped by model/type).
    /// </summary>
    private static void TryExtractUsage(
        Azure.Response? response,
        out int? contextualizationTokens,
        out Dictionary<string, int>? tokens,
        out int? documentPages)
    {
        contextualizationTokens = null;
        tokens = null;
        documentPages = null;

        if (response?.Content == null) return;

        try
        {
            using var doc = JsonDocument.Parse(response.Content.ToString());
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var resultEl))
                resultEl = root;

            // Try usage at top level or nested under "result"
            JsonElement usage;
            if (root.TryGetProperty("usage", out usage) || resultEl.TryGetProperty("usage", out usage))
            {
                if (usage.TryGetProperty("contextualizationTokens", out var ctx) && ctx.ValueKind == JsonValueKind.Number)
                    contextualizationTokens = ctx.GetInt32();

                if (usage.TryGetProperty("documentPagesStandard", out var pages) && pages.ValueKind == JsonValueKind.Number)
                    documentPages = pages.GetInt32();

                if (usage.TryGetProperty("tokens", out var tokensEl) && tokensEl.ValueKind == JsonValueKind.Object)
                {
                    tokens = new Dictionary<string, int>();
                    foreach (var prop in tokensEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                            tokens[prop.Name] = prop.Value.GetInt32();
                    }
                }
            }
        }
        catch
        {
            // Silently ignore parse failures — usage is best-effort
        }
    }

    private static string BuildRawResponseSummary(AnalyzeResult result)
    {
        var summary = new
        {
            analyzerId = result.AnalyzerId,
            contentCount = result.Contents?.Count ?? 0,
            contents = result.Contents?.Select(c => new
            {
                category = c.Category,
                markdownLength = c.Markdown?.Length ?? 0,
                fieldCount = c.Fields?.Count ?? 0,
                fields = c.Fields?.ToDictionary(f => f.Key, f => f.Value?.ToString() ?? "null"),
                isDocumentContent = c is DocumentContent,
                tableCount = c is DocumentContent dc1 ? dc1.Tables?.Count : null,
                figureCount = c is DocumentContent dc2 ? dc2.Figures?.Count : null,
                pageCount = c is DocumentContent dc3 ? dc3.Pages?.Count : null
            })
        };

        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    }

    private record ClassificationResult(string DocumentType, string Confidence, string Reasoning);

    /// <summary>
    /// Heuristic classification from extracted markdown/summary content
    /// when the analyzer doesn't return a native category.
    /// Matches against known document categories using keyword scoring.
    /// </summary>
    private ClassificationResult ClassifyFromContent(string markdown, string? summary)
    {
        string text = $"{summary} {markdown}".ToLowerInvariant();
        var categories = _schemaService.GetDocumentCategories();

        var scoredCategories = categories
            .Where(c => c.Key != "Unknown")
            .Select(c => new
            {
                Category = c,
                Score = ScoreCategory(text, c.Key, c.Description)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scoredCategories.FirstOrDefault();
        if (best == null || best.Score == 0)
        {
            return new ClassificationResult("Unknown", "low", "No strong keyword matches found in extracted content.");
        }

        string confidence = best.Score >= 5 ? "high" : best.Score >= 2 ? "medium" : "low";
        return new ClassificationResult(
            best.Category.Key,
            confidence,
            $"Keyword analysis of extracted content matched '{best.Category.Key}' (score: {best.Score}). Classification is heuristic-based on document content."
        );
    }

    private static int ScoreCategory(string text, string key, string description)
    {
        int score = 0;
        var keywords = GetCategoryKeywords(key);

        foreach (var keyword in keywords)
        {
            int idx = 0;
            while ((idx = text.IndexOf(keyword, idx, StringComparison.Ordinal)) != -1)
            {
                score++;
                idx += keyword.Length;
            }
        }

        return score;
    }

    private static string[] GetCategoryKeywords(string category) => category.ToLowerInvariant() switch
    {
        "invoice" => ["invoice", "bill to", "ship to", "subtotal", "total due", "payment terms", "invoice number", "invoice date", "due date", "unit price", "qty"],
        "insurance claim" => ["claim", "policy", "claimant", "loss", "insured", "deductible", "coverage", "premium", "adjuster", "first notice"],
        "balance sheet" => ["balance sheet", "total assets", "total liabilities", "shareholders equity", "retained earnings", "current assets", "current liabilities", "financial position"],
        "letter" => ["dear", "sincerely", "regards", "to whom it may concern", "enclosed", "attached"],
        "contract" => ["agreement", "parties", "whereas", "hereby", "obligations", "termination", "indemnification", "governing law"],
        "report" => ["executive summary", "findings", "recommendations", "analysis", "methodology", "conclusion"],
        "form" => ["please fill", "applicant", "signature", "date of birth", "check one", "form number"],
        "receipt" => ["receipt", "thank you", "paid", "change due", "subtotal", "total", "payment method", "transaction"],
        _ => [category.ToLowerInvariant()]
    };

    private OcrResult Error(string msg, double ms = 0) => new()
    {
        ServiceName = ServiceName,
        Strategy = string.Empty,
        RawMarkdown = string.Empty,
        ElapsedMs = ms,
        Error = msg
    };
}
