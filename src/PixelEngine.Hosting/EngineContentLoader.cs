using PixelEngine.Content;

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
}
