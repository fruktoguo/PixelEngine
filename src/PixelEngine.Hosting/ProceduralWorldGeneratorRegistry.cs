namespace PixelEngine.Hosting;

/// <summary>
/// 程序化世界生成器的键值注册表，供 <see cref="SceneSourceKind.Procedural" /> 场景装配时查找。
/// </summary>
internal sealed class ProceduralWorldGeneratorRegistry
{
    private readonly Dictionary<string, IProceduralWorldGenerator> _generators = new(StringComparer.Ordinal);

    /// <summary>
    /// 按稳定键注册程序化世界生成器；同键重复注册会覆盖旧实例。
    /// </summary>
    /// <param name="key">场景描述中的生成器键。</param>
    /// <param name="generator">程序化世界生成器实例。</param>
    internal void Register(string key, IProceduralWorldGenerator generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(generator);
        _generators[key] = generator;
    }

    /// <summary>
    /// 按键查找已注册的程序化世界生成器。
    /// </summary>
    /// <param name="key">场景描述中的生成器键。</param>
    /// <param name="generator">找到时写入生成器实例。</param>
    /// <returns>键存在时返回 true。</returns>
    internal bool TryGet(string key, out IProceduralWorldGenerator generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _generators.TryGetValue(key, out generator!);
    }
}
