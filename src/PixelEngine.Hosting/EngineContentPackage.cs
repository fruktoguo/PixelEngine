using PixelEngine.Simulation;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 加载后的引擎内容包，聚合材质表与反应表。
/// </summary>
public sealed class EngineContentPackage
{
    /// <summary>
    /// 创建内容包实例。
    /// </summary>
    /// <param name="contentRoot">内容根目录。</param>
    /// <param name="materials">运行时材质表。</param>
    /// <param name="reactions">运行时反应表。</param>
    internal EngineContentPackage(string contentRoot, MaterialTable materials, ReactionTable reactions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(reactions);
        ContentRoot = Path.GetFullPath(contentRoot);
        MaterialTable = materials;
        ReactionTable = reactions;
        MaterialRegistry = new EngineMaterialRegistry(materials);
    }

    /// <summary>
    /// 内容根目录。
    /// </summary>
    public string ContentRoot { get; }

    /// <summary>
    /// 材质槽数量。
    /// </summary>
    public int MaterialCount => MaterialTable.Count;

    /// <summary>
    /// packed 反应数量。
    /// </summary>
    public int ReactionCount => ReactionTable.Count;

    /// <summary>
    /// Demo 与脚本可用的材质查询接口。
    /// </summary>
    public IMaterialQuery Materials => MaterialRegistry;

    internal MaterialTable MaterialTable { get; }

    internal ReactionTable ReactionTable { get; }

    internal EngineMaterialRegistry MaterialRegistry { get; }

    /// <summary>
    /// 按稳定材质 name 查询 runtime id。
    /// </summary>
    /// <param name="name">稳定材质 name。</param>
    /// <param name="id">runtime id。</param>
    /// <returns>若材质存在则返回 true。</returns>
    public bool TryResolveMaterial(string name, out MaterialId id)
    {
        return MaterialRegistry.TryResolve(name, out id);
    }

    /// <summary>
    /// 按稳定材质 name 解析 runtime id；失败时返回 <see cref="MaterialId.Invalid" />。
    /// </summary>
    /// <param name="name">稳定材质 name。</param>
    /// <returns>runtime 材质句柄。</returns>
    public MaterialId ResolveMaterial(string name)
    {
        return MaterialRegistry.Resolve(name);
    }

    /// <summary>
    /// 读取材质属性摘要。
    /// </summary>
    /// <param name="id">runtime 材质句柄。</param>
    /// <returns>材质属性摘要。</returns>
    public MaterialInfo GetMaterialInfo(MaterialId id)
    {
        return MaterialRegistry.GetInfo(id);
    }
}
