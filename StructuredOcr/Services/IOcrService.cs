namespace StructuredOcr.Services;

using StructuredOcr.Models;

public interface IOcrService
{
    string ServiceName { get; }
    string[] Strategies { get; }
    Task<OcrResult> AnalyzeAsync(Stream file, string fileName, string strategy, UnifiedSchema? schema, CancellationToken ct = default);
}
