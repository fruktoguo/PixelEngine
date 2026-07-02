namespace PixelEngine.Hosting;

internal sealed class ProceduralWorldGeneratorRegistry
{
    private readonly Dictionary<string, IProceduralWorldGenerator> _generators = new(StringComparer.Ordinal);

    internal void Register(string key, IProceduralWorldGenerator generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(generator);
        _generators[key] = generator;
    }

    internal bool TryGet(string key, out IProceduralWorldGenerator generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _generators.TryGetValue(key, out generator!);
    }
}
