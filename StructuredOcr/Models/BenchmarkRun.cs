namespace StructuredOcr.Models;

public class BenchmarkRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public List<OcrResult> Results { get; set; } = [];
}
