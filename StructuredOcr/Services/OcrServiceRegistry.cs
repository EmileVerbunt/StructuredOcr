namespace StructuredOcr.Services;

/// <summary>
/// Discovers registered IOcrService implementations and exposes them by name.
/// </summary>
public class OcrServiceRegistry
{
    private readonly Dictionary<string, IOcrService> _services;

    public OcrServiceRegistry(IEnumerable<IOcrService> services)
    {
        _services = services.ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IOcrService> GetAll() => _services.Values.ToList();

    public IOcrService? GetByName(string name) =>
        _services.TryGetValue(name, out var s) ? s : null;
}
