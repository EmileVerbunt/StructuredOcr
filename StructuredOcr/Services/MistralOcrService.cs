using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StructuredOcr.Models;

namespace StructuredOcr.Services;

public class MistralOcrService : IOcrService
{
    private readonly ConfigStore _configStore;
    private readonly HttpClient _httpClient;
    private readonly SchemaService _schemaService;
    private const string Service = "Mistral";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ServiceName => "Mistral";
    public string[] Strategies => ["ExtractMarkdown", "ExtractStructured", "Classify"];

    public MistralOcrService(ConfigStore configStore, HttpClient httpClient, SchemaService schemaService)
    {
        _configStore = configStore;
        _httpClient = httpClient;
        _schemaService = schemaService;
    }

    public async Task<OcrResult> AnalyzeAsync(Stream file, string fileName, string strategy, UnifiedSchema? schema, CancellationToken ct = default)
    {
        var config = _configStore.Get(Service);
        if (config is null || string.IsNullOrWhiteSpace(config.Endpoint) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Error("Mistral is not configured. Go to Settings.");
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        string base64Content = Convert.ToBase64String(ms.ToArray());

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        string mimeType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/pdf"
        };

        string dataUrl = $"data:{mimeType};base64,{base64Content}";
        bool isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        string modelName = config.ModelName ?? "mistral-document-ai-2512";

        MistralAnnotationFormat? docAnnotation = null;
        MistralAnnotationFormat? bboxAnnotation = null;

        if (strategy == "ExtractStructured" && schema != null)
        {
            docAnnotation = BuildAnnotationFromSchema(schema);
        }
        else if (strategy == "Classify")
        {
            docAnnotation = BuildClassificationAnnotation();
        }
        else if (strategy == "ExtractStructured")
        {
            // Default generic annotation when no schema provided
            docAnnotation = BuildGenericAnnotation();
        }

        var requestBody = new MistralOcrRequest
        {
            Model = modelName,
            Document = new MistralDocument
            {
                Type = isImage ? "image_url" : "document_url",
                ImageUrl = isImage ? dataUrl : null,
                DocumentUrl = isImage ? null : dataUrl
            },
            IncludeImageBase64 = false,
            DocumentAnnotationFormat = docAnnotation,
            BboxAnnotationFormat = bboxAnnotation
        };

        var sw = Stopwatch.StartNew();
        try
        {
            string url = config.Endpoint.TrimEnd('/');
            if (!url.EndsWith("/ocr", StringComparison.OrdinalIgnoreCase))
            {
                // Azure AI Foundry serverless deployments use a different path than the direct Mistral API
                bool isAzure = url.Contains(".azure.com", StringComparison.OrdinalIgnoreCase)
                            || url.Contains(".azure.us", StringComparison.OrdinalIgnoreCase);
                url += isAzure ? "/providers/mistral/azure/ocr" : "/v1/ocr";
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, SerializerOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return Error($"HTTP {(int)response.StatusCode}: {responseContent}", sw.Elapsed.TotalMilliseconds);
            }

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                return Error("Mistral API returned an empty response body.", sw.Elapsed.TotalMilliseconds);
            }

            string markdown = ExtractMarkdownFromPages(responseContent);
            var pages = ExtractPerPageResults(responseContent);
            string? structured = strategy != "ExtractMarkdown" ? ExtractResultContent(responseContent) : null;
            int? tokens = ExtractTokenUsage(responseContent);
            int? pagesProcessed = ExtractPagesProcessed(responseContent);
            int? pageCount = pages.Count > 0 ? pages.Count : pagesProcessed;

            if (structured != null && structured == responseContent)
                structured = null;

            return new OcrResult
            {
                ServiceName = ServiceName,
                Strategy = strategy,
                RawMarkdown = markdown,
                Pages = pages,
                PageCount = pageCount,
                StructuredJson = structured,
                DocumentType = strategy == "Classify" ? TryExtractDocType(structured) : null,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                TokensUsed = tokens,
                RawServiceResponse = responseContent
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Error($"{ex.GetType().Name}: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    private MistralAnnotationFormat BuildAnnotationFromSchema(UnifiedSchema schema)
    {
        var properties = new Dictionary<string, MistralJsonSchemaNode>();
        var required = new List<string>();

        foreach (var field in schema.Fields)
        {
            properties[field.Name] = ConvertField(field);
            if (field.Required) required.Add(field.Name);
        }

        return new MistralAnnotationFormat
        {
            Type = "json_schema",
            JsonSchema = new MistralJsonSchemaEnvelope
            {
                Name = schema.Name.Replace(" ", "_").ToLowerInvariant(),
                Strict = true,
                Schema = new MistralJsonSchemaNode
                {
                    Type = "object",
                    Title = schema.Name,
                    Description = schema.Description ?? "Extract structured data from the document.",
                    AdditionalProperties = false,
                    Properties = properties,
                    Required = required.Count > 0 ? required : null
                }
            }
        };
    }

    private MistralJsonSchemaNode ConvertField(SchemaField field)
    {
        var node = new MistralJsonSchemaNode
        {
            Title = field.Name,
            Description = field.Description
        };

        if (field.Type == SchemaFieldType.Array)
        {
            node.Type = "array";
            if (field.Children is { Count: 1 })
            {
                node.Items = ConvertField(field.Children[0]);
            }
            else if (field.Children is { Count: > 1 })
            {
                node.Items = new MistralJsonSchemaNode
                {
                    Type = "object",
                    Properties = field.Children.ToDictionary(c => c.Name, ConvertField),
                    Required = field.Children.Where(c => c.Required).Select(c => c.Name).ToList(),
                    AdditionalProperties = false
                };
            }
            else
            {
                node.Items = new MistralJsonSchemaNode { Type = "string" };
            }
        }
        else if (field.Type == SchemaFieldType.Object && field.Children is { Count: > 0 })
        {
            node.Type = "object";
            node.Properties = field.Children.ToDictionary(c => c.Name, ConvertField);
            node.Required = field.Children.Where(c => c.Required).Select(c => c.Name).ToList();
            node.AdditionalProperties = false;
        }
        else
        {
            node.Type = field.Type switch
            {
                SchemaFieldType.Number => "number",
                SchemaFieldType.Boolean => "boolean",
                _ => "string"
            };
        }

        return node;
    }

    private MistralAnnotationFormat BuildClassificationAnnotation()
    {
        var categories = _schemaService.GetDocumentCategories();
        string categoryList = string.Join(", ", categories.Select(c => c.Key));
        string categoryDescriptions = string.Join("\n", categories.Select(c => $"- {c.Key}: {c.Description}"));

        return new MistralAnnotationFormat
        {
            Type = "json_schema",
            JsonSchema = new MistralJsonSchemaEnvelope
            {
                Name = "document_classification",
                Strict = true,
                Schema = new MistralJsonSchemaNode
                {
                    Type = "object",
                    Title = "DocumentClassification",
                    Description = $"Classify this document into exactly one of these categories:\n{categoryDescriptions}\n\nPick the single best match. Use 'Unknown' if none fit.",
                    AdditionalProperties = false,
                    Properties = new Dictionary<string, MistralJsonSchemaNode>
                    {
                        ["document_type"] = new() { Type = "string", Title = "Document Type", Description = $"One of: {categoryList}" },
                        ["confidence"] = new() { Type = "string", Title = "Confidence", Description = "high, medium, or low" },
                        ["reasoning"] = new() { Type = "string", Title = "Reasoning", Description = "Brief explanation of why this category was chosen" }
                    },
                    Required = ["document_type", "confidence", "reasoning"]
                }
            }
        };
    }

    private MistralAnnotationFormat BuildGenericAnnotation()
    {
        return new MistralAnnotationFormat
        {
            Type = "json_schema",
            JsonSchema = new MistralJsonSchemaEnvelope
            {
                Name = "document_annotation",
                Strict = true,
                Schema = new MistralJsonSchemaNode
                {
                    Type = "object",
                    Title = "DocumentAnnotation",
                    Description = "Analyze the document and return language, chapter titles, and URLs.",
                    AdditionalProperties = false,
                    Properties = new Dictionary<string, MistralJsonSchemaNode>
                    {
                        ["language"] = new() { Type = "string", Title = "Language" },
                        ["chapter_titles"] = new() { Type = "string", Title = "Chapter_Titles" },
                        ["urls"] = new() { Type = "string", Title = "urls" }
                    },
                    Required = ["language", "chapter_titles", "urls"]
                }
            }
        };
    }

    private static string ExtractResultContent(string responseContent)
    {
        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;

        // Top-level annotations
        if (TryGetNonNullProperty(root, "document_annotation", out var annotation))
            return annotation.GetRawText();
        if (TryGetNonNullProperty(root, "bbox_annotation", out var bbox))
            return bbox.GetRawText();
        if (TryGetNonNullProperty(root, "bbox_annotations", out var bboxes))
            return bboxes.GetRawText();

        // Per-page annotations (pages[].document_annotation / bbox_annotation)
        if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            var annotations = new List<string>();
            foreach (var page in pages.EnumerateArray())
            {
                if (TryGetNonNullProperty(page, "document_annotation", out var pageDocAnn))
                    annotations.Add(pageDocAnn.GetRawText());
                else if (TryGetNonNullProperty(page, "bbox_annotation", out var pageBboxAnn))
                    annotations.Add(pageBboxAnn.GetRawText());

                // Also check image-level annotations
                if (page.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var img in images.EnumerateArray())
                    {
                        if (TryGetNonNullProperty(img, "image_annotation", out var imgAnn))
                            annotations.Add(imgAnn.GetRawText());
                    }
                }
            }

            if (annotations.Count == 1)
                return annotations[0];
            if (annotations.Count > 1)
                return $"[{string.Join(",", annotations)}]";
        }

        if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString() ?? string.Empty;

        return responseContent;
    }

    private static bool TryGetNonNullProperty(JsonElement el, string name, out JsonElement value)
    {
        if (el.TryGetProperty(name, out value) &&
            value.ValueKind != JsonValueKind.Null &&
            value.ValueKind != JsonValueKind.Undefined)
            return true;
        value = default;
        return false;
    }

    /// <summary>
    /// Extracts and concatenates markdown from all pages in the Mistral response.
    /// </summary>
    private static string ExtractMarkdownFromPages(string responseContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var page in pages.EnumerateArray())
                {
                    if (page.TryGetProperty("markdown", out var md) && md.ValueKind == JsonValueKind.String)
                    {
                        if (sb.Length > 0) sb.AppendLine("\n---\n");
                        sb.Append(md.GetString());
                    }
                }
                if (sb.Length > 0) return sb.ToString();
            }

            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString() ?? string.Empty;
        }
        catch { }

        return responseContent;
    }

    /// <summary>
    /// Extracts per-page results (markdown + structured annotation per page).
    /// </summary>
    private static List<PageResult> ExtractPerPageResults(string responseContent)
    {
        var results = new List<PageResult>();
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var page in pages.EnumerateArray())
                {
                    var pr = new PageResult { PageIndex = idx };

                    if (page.TryGetProperty("markdown", out var md) && md.ValueKind == JsonValueKind.String)
                        pr.Markdown = md.GetString() ?? string.Empty;

                    // Check for per-page annotations
                    if (TryGetNonNullProperty(page, "document_annotation", out var pageDocAnn))
                        pr.StructuredJson = pageDocAnn.GetRawText();
                    else if (TryGetNonNullProperty(page, "bbox_annotation", out var pageBboxAnn))
                        pr.StructuredJson = pageBboxAnn.GetRawText();

                    // Also check image-level annotations
                    if (pr.StructuredJson == null &&
                        page.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                    {
                        var imgAnnotations = new List<string>();
                        foreach (var img in images.EnumerateArray())
                        {
                            if (TryGetNonNullProperty(img, "image_annotation", out var imgAnn))
                                imgAnnotations.Add(imgAnn.GetRawText());
                        }
                        if (imgAnnotations.Count == 1)
                            pr.StructuredJson = imgAnnotations[0];
                        else if (imgAnnotations.Count > 1)
                            pr.StructuredJson = $"[{string.Join(",", imgAnnotations)}]";
                    }

                    results.Add(pr);
                    idx++;
                }
            }
        }
        catch { }
        return results;
    }

    private static int? ExtractTokenUsage(string responseContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("total_tokens", out var total))
                    return total.GetInt32();
            }
        }
        catch { }
        return null;
    }

    private static int? ExtractPagesProcessed(string responseContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage_info", out var usageInfo))
            {
                if (usageInfo.TryGetProperty("pages_processed", out var pagesProcessed))
                    return pagesProcessed.GetInt32();
            }
        }
        catch { }
        return null;
    }

    private static string? TryExtractDocType(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
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

#region Mistral DTOs

internal class MistralOcrRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("document")]
    public MistralDocument Document { get; set; } = new();

    [JsonPropertyName("include_image_base64")]
    public bool IncludeImageBase64 { get; set; }

    [JsonPropertyName("document_annotation_format")]
    public MistralAnnotationFormat? DocumentAnnotationFormat { get; set; }

    [JsonPropertyName("bbox_annotation_format")]
    public MistralAnnotationFormat? BboxAnnotationFormat { get; set; }
}

internal class MistralDocument
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("document_url")]
    public string? DocumentUrl { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }
}

internal class MistralAnnotationFormat
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("json_schema")]
    public MistralJsonSchemaEnvelope JsonSchema { get; set; } = new();
}

internal class MistralJsonSchemaEnvelope
{
    [JsonPropertyName("schema")]
    public MistralJsonSchemaNode Schema { get; set; } = new();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("strict")]
    public bool Strict { get; set; }
}

internal class MistralJsonSchemaNode
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("additionalProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdditionalProperties { get; set; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, MistralJsonSchemaNode>? Properties { get; set; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MistralJsonSchemaNode? Items { get; set; }
}

#endregion
