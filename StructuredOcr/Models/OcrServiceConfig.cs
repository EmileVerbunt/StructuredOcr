namespace StructuredOcr.Models;

public class OcrServiceConfig
{
    public string ServiceName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public List<string> DeploymentNames { get; set; } = [];
    public Dictionary<string, string> ExtraSettings { get; set; } = [];
}
