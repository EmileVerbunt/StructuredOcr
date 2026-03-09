using System.Text.Json;

namespace StructuredOcr.Components.Shared;

/// <summary>
/// Shared JSON utility methods used across result display components.
/// </summary>
public static class JsonHelper
{
    public static string FormatJson(string json)
    {
        try
        {
            var parsed = UnwrapJsonString(json);
            var doc = JsonDocument.Parse(parsed);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    public static string UnwrapJsonString(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith('"') && s.EndsWith('"'))
        {
            try
            {
                string? unwrapped = JsonSerializer.Deserialize<string>(s);
                if (unwrapped != null) return unwrapped;
            }
            catch { }
        }
        return s;
    }

    public static Dictionary<string, string> ParseStructuredFields(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string unwrapped = UnwrapJsonString(json);
            using var doc = JsonDocument.Parse(unwrapped);
            FlattenElement(doc.RootElement, "", result);
        }
        catch { }
        return result;
    }

    public static void FlattenElement(JsonElement el, string prefix, Dictionary<string, string> result)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Object ||
                    prop.Value.ValueKind == JsonValueKind.Array)
                {
                    FlattenElement(prop.Value, key, result);
                }
                else
                {
                    result[key] = prop.Value.ToString();
                }
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var item in el.EnumerateArray())
            {
                FlattenElement(item, $"{prefix}[{i}]", result);
                i++;
            }
        }
        else
        {
            result[prefix] = el.ToString();
        }
    }

    public static string GetFieldValue(Dictionary<string, string> fields, string fieldName)
    {
        if (fields.TryGetValue(fieldName, out var exact))
            return exact;

        var subKeys = fields
            .Where(f => f.Key.StartsWith(fieldName + "[", StringComparison.OrdinalIgnoreCase) ||
                        f.Key.StartsWith(fieldName + ".", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (subKeys.Count > 0)
        {
            return string.Join("\n", subKeys.Select(kv =>
            {
                string suffix = kv.Key[fieldName.Length..];
                return $"{suffix}: {kv.Value}";
            }));
        }

        return "—";
    }

    public static List<Dictionary<string, string>>? ParseStructuredArray(string json)
    {
        try
        {
            string unwrapped = UnwrapJsonString(json);
            using var doc = JsonDocument.Parse(unwrapped);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var results = new List<Dictionary<string, string>>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (item.ValueKind == JsonValueKind.String)
                {
                    string innerJson = item.GetString() ?? "";
                    try
                    {
                        using var innerDoc = JsonDocument.Parse(innerJson);
                        FlattenElement(innerDoc.RootElement, "", fields);
                    }
                    catch
                    {
                        fields["value"] = innerJson;
                    }
                }
                else
                {
                    FlattenElement(item, "", fields);
                }
                results.Add(fields);
            }
            return results.Count > 0 ? results : null;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatFieldName(string name)
    {
        return string.Join(' ', name.Split('_', '.').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    public static string? TryExtractField(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var val) &&
                val.ValueKind == JsonValueKind.String)
                return val.GetString();
        }
        catch { }
        return null;
    }
}
