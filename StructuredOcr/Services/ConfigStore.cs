using System.Text.Json;
using StructuredOcr.Models;

namespace StructuredOcr.Services;

/// <summary>
/// In-memory store for per-service configuration (endpoint, key, model).
/// Seeded from appsettings.json, overridden by localStorage via Blazor JS interop.
/// </summary>
public class ConfigStore
{
    private readonly Dictionary<string, OcrServiceConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private const string LocalStorageKey = "ocr-benchmark-settings";

    public IReadOnlyList<OcrServiceConfig> GetAll() => _configs.Values.ToList();

    public OcrServiceConfig? Get(string serviceName) =>
        _configs.TryGetValue(serviceName, out var c) ? c : null;

    public void Set(OcrServiceConfig config) =>
        _configs[config.ServiceName] = config;

    public void SeedFromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("OcrServices");
        foreach (var child in section.GetChildren())
        {
            var config = new OcrServiceConfig
            {
                ServiceName = child.Key,
                Endpoint = child["Endpoint"] ?? string.Empty,
                ApiKey = child["ApiKey"] ?? string.Empty,
                ModelName = child["ModelName"]
            };
            var deployments = child.GetSection("DeploymentNames");
            foreach (var d in deployments.GetChildren())
            {
                if (d.Value != null) config.DeploymentNames.Add(d.Value);
            }
            var extra = child.GetSection("ExtraSettings");
            foreach (var kv in extra.GetChildren())
            {
                config.ExtraSettings[kv.Key] = kv.Value ?? string.Empty;
            }
            _configs[config.ServiceName] = config;
        }
    }

    /// <summary>
    /// Serializes all configs to JSON for localStorage persistence.
    /// </summary>
    public string SerializeAll()
    {
        return JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Loads configs from a JSON string (from localStorage), merging over existing.
    /// </summary>
    public void LoadFromJson(string json)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, OcrServiceConfig>>(json);
            if (loaded == null) return;
            foreach (var (key, config) in loaded)
            {
                // Only override if there's actual data
                if (!string.IsNullOrWhiteSpace(config.Endpoint) || !string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    _configs[key] = config;
                }
            }
        }
        catch { }
    }

    public static string StorageKey => LocalStorageKey;
}
