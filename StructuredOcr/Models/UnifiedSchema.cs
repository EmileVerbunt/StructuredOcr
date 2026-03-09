using System.Text.Json;
using System.Text.Json.Serialization;

namespace StructuredOcr.Models;

/// <summary>
/// Unified schema that gets translated to each OCR service's native format.
/// Uses a JSON-Schema-like field definition approach.
/// </summary>
public class UnifiedSchema
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SchemaField> Fields { get; set; } = [];

    /// <summary>
    /// Per-service overrides. Key = service name (e.g. "Mistral", "ContentUnderstanding").
    /// Value = arbitrary JSON that the service knows how to interpret.
    /// </summary>
    public Dictionary<string, JsonElement> ServiceExtensions { get; set; } = [];
}

public class SchemaField
{
    public string Name { get; set; } = string.Empty;
    public SchemaFieldType Type { get; set; } = SchemaFieldType.String;
    public string? Description { get; set; }
    public bool Required { get; set; } = true;
    public List<SchemaField>? Children { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchemaFieldType
{
    String,
    Number,
    Boolean,
    Array,
    Object
}
