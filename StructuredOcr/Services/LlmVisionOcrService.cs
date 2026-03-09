using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StructuredOcr.Models;

namespace StructuredOcr.Services;

public class LlmVisionOcrService : IOcrService
{
    private readonly ConfigStore _configStore;
    private readonly HttpClient _httpClient;
    private readonly SchemaService _schemaService;
    private const string Service = "LlmVision";

    /// <summary>
    /// The currently selected deployment name, set by the service page before running analysis.
    /// </summary>
    public string? SelectedDeployment { get; set; }

    public string ServiceName => "LLM Vision";
    public string[] Strategies => ["ExtractMarkdown", "ExtractStructured", "Classify"];

    public LlmVisionOcrService(ConfigStore configStore, HttpClient httpClient, SchemaService schemaService)
    {
        _configStore = configStore;
        _httpClient = httpClient;
        _schemaService = schemaService;
    }

    public List<string> GetAvailableDeployments()
    {
        var config = _configStore.Get(Service);
        return config?.DeploymentNames ?? [];
    }

    public async Task<OcrResult> AnalyzeAsync(Stream file, string fileName, string strategy, UnifiedSchema? schema, CancellationToken ct = default)
    {
        var config = _configStore.Get(Service);
        if (config is null || string.IsNullOrWhiteSpace(config.Endpoint) || string.IsNullOrWhiteSpace(config.ApiKey))
            return Error("LLM Vision is not configured. Go to Settings.");

        string deployment = SelectedDeployment ?? config.DeploymentNames.FirstOrDefault() ?? config.ModelName ?? "";
        if (string.IsNullOrWhiteSpace(deployment))
            return Error("No deployment selected. Select a model on the service page.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        string base64 = Convert.ToBase64String(ms.ToArray());

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        string mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };

        var categories = strategy == "Classify" ? _schemaService.GetDocumentCategories() : null;
        string systemPrompt = BuildSystemPrompt(strategy, schema, categories);
        string dataUrl = $"data:{mime};base64,{base64}";

        // Azure OpenAI chat completions with vision
        string apiVersion = config.ExtraSettings.GetValueOrDefault("ApiVersion", "2025-01-01-preview");
        // Strip any trailing /openai path segments so users can paste the full URI from the Azure portal
        string baseUrl = config.Endpoint.TrimEnd('/');
        int openaiIdx = baseUrl.IndexOf("/openai", StringComparison.OrdinalIgnoreCase);
        if (openaiIdx > 0)
            baseUrl = baseUrl[..openaiIdx];
        string url = $"{baseUrl}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        var requestBody = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Analyze this document." },
                        new { type = "image_url", image_url = new { url = dataUrl, detail = "high" } }
                    }
                }
            },
            max_tokens = 4096,
            temperature = 0.1
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", config.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
                return Error($"HTTP {(int)response.StatusCode} [{url}]: {responseContent}", sw.Elapsed.TotalMilliseconds);

            string content = ExtractChatContent(responseContent);
            int? tokens = ExtractTokens(responseContent);
            var (promptTokens, completionTokens) = ExtractTokensSplit(responseContent);

            bool isStructured = strategy is "ExtractStructured" or "Classify";

            return new OcrResult
            {
                ServiceName = $"{ServiceName} ({deployment})",
                Strategy = strategy,
                RawMarkdown = isStructured ? string.Empty : content,
                StructuredJson = isStructured ? content : null,
                DocumentType = strategy == "Classify" ? TryExtractDocType(content) : null,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                TokensUsed = tokens,
                InputTokens = promptTokens,
                OutputTokens = completionTokens,
                RawServiceResponse = responseContent
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Error($"{ex.GetType().Name}: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static string BuildSystemPrompt(string strategy, UnifiedSchema? schema, IReadOnlyList<DocumentCategory>? categories)
    {
        return strategy switch
        {
            "ExtractMarkdown" => """
                You are a document OCR assistant. Extract all text content from the provided document image 
                and return it as well-structured Markdown. Preserve headings, tables, lists, and formatting.
                Return ONLY the markdown content, no explanations.
                """,

            "ExtractStructured" when schema != null => $"""
                You are a document extraction assistant. Analyze the provided document image and extract 
                structured data according to this schema:
                
                Schema: {schema.Name}
                Description: {schema.Description}
                Fields:
                {string.Join("\n", schema.Fields.Select(f => $"- {f.Name} ({f.Type}): {f.Description}"))}
                
                Return a JSON object with exactly these fields. Use null for fields you cannot determine.
                Return ONLY valid JSON, no markdown fences or explanations.
                """,

            "ExtractStructured" => """
                You are a document extraction assistant. Analyze the provided document image and extract 
                structured data. Return a JSON object with fields: document_type, language, summary, 
                and any key data points you can identify.
                Return ONLY valid JSON, no markdown fences or explanations.
                """,

            "Classify" when categories is { Count: > 0 } => $$"""
                You are a document classifier. Classify the provided document image into exactly one of these categories:
                {{string.Join("\n", categories.Select(c => $"- {c.Key}: {c.Description}"))}}
                
                Pick the single best matching category. If none match well, use "Unknown".
                Return a JSON object with exactly these fields:
                {"document_type": "...", "confidence": "high|medium|low", "reasoning": "Brief explanation of why this category was chosen"}
                Return ONLY valid JSON, no markdown fences or explanations.
                """,

            "Classify" => """
                You are a document classifier. Determine what type of document this is.
                Return a JSON object: {"document_type": "...", "confidence": "high|medium|low", "reasoning": "..."}
                Return ONLY valid JSON, no markdown fences or explanations.
                """,

            _ => "Analyze the provided document image and describe its contents."
        };
    }

    private static string ExtractChatContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            var first = choices[0];
            var message = first.GetProperty("message");
            string content = message.GetProperty("content").GetString() ?? "";

            // Strip markdown code fences if present
            content = content.Trim();
            if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                content = content[7..];
            else if (content.StartsWith("```"))
                content = content[3..];
            if (content.EndsWith("```"))
                content = content[..^3];

            return content.Trim();
        }
        catch
        {
            return json;
        }
    }

    private static int? ExtractTokens(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("total_tokens", out var total))
                return total.GetInt32();
        }
        catch { }
        return null;
    }

    private static (int? prompt, int? completion) ExtractTokensSplit(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                int? prompt = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : null;
                int? completion = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : null;
                return (prompt, completion);
            }
        }
        catch { }
        return (null, null);
    }

    private static string? TryExtractDocType(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("document_type", out var dt))
                return dt.GetString();
        }
        catch { }
        return null;
    }

    private OcrResult Error(string msg, double ms = 0) => new()
    {
        ServiceName = ServiceName,
        Strategy = string.Empty,
        RawMarkdown = string.Empty,
        ElapsedMs = ms,
        Error = msg
    };
}
