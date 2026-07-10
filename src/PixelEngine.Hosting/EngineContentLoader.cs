using PixelEngine.Content;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 内容包加载器，负责把项目内容根目录转换成引擎运行时表。
/// </summary>
public static class EngineContentLoader
{
    /// <summary>
    /// materials.json 文件名。
    /// </summary>
    public const string MaterialsFileName = "materials.json";

    /// <summary>
    /// reactions.json 文件名。
    /// </summary>
    public const string ReactionsFileName = "reactions.json";

    /// <summary>
    /// 判断指定内容根目录是否具备材质与反应 JSON 文件。
    /// </summary>
    /// <param name="contentRoot">内容根目录。</param>
    /// <returns>两个内容文件都存在时返回 true。</returns>
    public static bool HasMaterialPackage(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        string root = Path.GetFullPath(contentRoot);
        return File.Exists(Path.Combine(root, MaterialsFileName)) &&
            File.Exists(Path.Combine(root, ReactionsFileName));
    }

    /// <summary>
    /// 从内容根目录加载材质与反应 JSON。
    /// </summary>
    /// <param name="contentRoot">内容根目录。</param>
    /// <returns>加载后的内容包。</returns>
    public static EngineContentPackage LoadMaterialPackage(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        string root = Path.GetFullPath(contentRoot);
        string materialsPath = Path.Combine(root, MaterialsFileName);
        string reactionsPath = Path.Combine(root, ReactionsFileName);
        if (!File.Exists(materialsPath))
        {
            throw new FileNotFoundException("缺少 materials.json。", materialsPath);
        }

        if (!File.Exists(reactionsPath))
        {
            throw new FileNotFoundException("缺少 reactions.json。", reactionsPath);
        }

        string materialsJson = File.ReadAllText(materialsPath);
        string reactionsJson = File.ReadAllText(reactionsPath);
        MaterialContentLoadResult result = MaterialContentLoader.Load(materialsJson, reactionsJson);
        return new EngineContentPackage(root, result.Materials, result.Reactions);
    }

    /// <summary>
    /// 经 Hosting 公开门面从内容根目录加载一个 JSON 配置文件，供 Demo/玩法层避免直接依赖 JSON 解析细节。
    /// </summary>
    /// <typeparam name="TConfig">配置文档类型。</typeparam>
    /// <param name="contentRoot">内容根目录。</param>
    /// <param name="relativePath">相对内容根目录的配置路径。</param>
    /// <param name="typeInfo">由调用方提供的 source-generated JSON 类型元数据。</param>
    /// <returns>解析后的配置文档。</returns>
    public static TConfig LoadConfig<TConfig>(string contentRoot, string relativePath, JsonTypeInfo<TConfig> typeInfo)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        string path = ResolveContentFile(contentRoot, relativePath);
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, typeInfo) ??
            throw new InvalidDataException($"配置文件为空或无法解析：{path}");
    }

    /// <summary>
    /// 经 Hosting 公开门面读取内容根目录中的配置文本，保持路径包含校验且允许玩法层显式 AOT-safe 解析。
    /// </summary>
    /// <param name="contentRoot">内容根目录。</param>
    /// <param name="relativePath">相对内容根目录的配置路径。</param>
    /// <returns>配置文件 UTF-8 文本。</returns>
    public static string ReadConfigText(string contentRoot, string relativePath)
    {
        return File.ReadAllText(ResolveContentFile(contentRoot, relativePath));
    }

    private static string ResolveContentFile(string contentRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string root = Path.GetFullPath(contentRoot);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootWithSeparator = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        _ = path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? path
            : throw new ArgumentException("配置路径必须位于内容根目录内。", nameof(relativePath));

        return File.Exists(path) ? path : throw new FileNotFoundException("缺少配置文件。", path);
    }
}
