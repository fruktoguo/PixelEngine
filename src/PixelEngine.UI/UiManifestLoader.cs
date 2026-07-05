using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.UI;

/// <summary>
/// 加载 content/ui/ui-manifest.json 的 trim/AOT 友好加载器。
/// </summary>
public static class UiManifestLoader
{
    /// <summary>
    /// 默认 UI 清单文件名。
    /// </summary>
    public const string ManifestFileName = "ui-manifest.json";

    /// <summary>
    /// 从 content/ui 目录加载默认清单。
    /// </summary>
    /// <param name="uiRootDirectory">content/ui 根目录。</param>
    /// <returns>已校验清单。</returns>
    public static UiManifest LoadFromDirectory(string uiRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uiRootDirectory);
        string root = Path.GetFullPath(uiRootDirectory);
        return Directory.Exists(root)
            ? Load(Path.Combine(root, ManifestFileName))
            : throw new DirectoryNotFoundException($"找不到 UI 根目录：{root}");
    }

    /// <summary>
    /// 从指定 manifest 文件加载 UI 清单。
    /// </summary>
    /// <param name="manifestPath">manifest 文件路径。</param>
    /// <returns>已校验清单。</returns>
    public static UiManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到 UI manifest。", fullPath);
        }

        string root = Path.GetDirectoryName(fullPath) ??
            throw new InvalidDataException("UI manifest 路径缺少父目录。");
        string json = File.ReadAllText(fullPath);
        return LoadFromJson(json, root);
    }

    /// <summary>
    /// 从 JSON 文本加载 UI 清单。
    /// </summary>
    /// <param name="json">manifest JSON 文本。</param>
    /// <param name="uiRootDirectory">content/ui 根目录。</param>
    /// <returns>已校验清单。</returns>
    public static UiManifest LoadFromJson(string json, string uiRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiRootDirectory);
        string root = Path.GetFullPath(uiRootDirectory);
        UiManifestJson document = JsonSerializer.Deserialize(json, UiManifestJsonContext.Default.UiManifestJson) ??
            throw new InvalidDataException("UI manifest 为空。");
        return Build(document, root);
    }

    /// <summary>
    /// 从 DTO 构建已校验清单。
    /// </summary>
    /// <param name="document">manifest DTO。</param>
    /// <param name="uiRootDirectory">content/ui 根目录。</param>
    /// <returns>已校验清单。</returns>
    public static UiManifest Build(UiManifestJson document, string uiRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiRootDirectory);
        string root = Path.GetFullPath(uiRootDirectory);
        UiManifestScreenJson[] screenJson = document.Screens is { Length: > 0 }
            ? document.Screens
            : throw new InvalidDataException("UI manifest 至少需要一个 screens 条目。");

        UiManifestScreen[] screens = new UiManifestScreen[screenJson.Length];
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int i = 0; i < screenJson.Length; i++)
        {
            UiManifestScreenJson item = screenJson[i] ??
                throw new InvalidDataException($"UI manifest screens[{i}] 为空。");
            string id = RequireToken(item.Id, $"screens[{i}].id");
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"UI manifest 存在重复屏幕 id：{id}");
            }

            string relativePath = NormalizeRelativePath(RequireToken(item.Path, $"screens[{i}].path"));
            string fullPath = ResolveAssetPath(root, relativePath, $"screens[{i}].path");
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"UI 屏幕 '{id}' 指向的文档不存在。", fullPath);
            }

            UiScreenId screenId = new(UiStableId.Hash(id));
            screens[i] = new UiManifestScreen(id, relativePath, fullPath, item.Preload, screenId);
        }

        return new UiManifest(NormalizeRoot(root), screens);
    }

    private static string RequireToken(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"UI manifest 字段 {fieldName} 不能为空。")
            : value.Trim();
    }

    private static string NormalizeRelativePath(string path)
    {
        return Path.IsPathRooted(path)
            ? throw new InvalidDataException($"UI manifest 路径必须相对 content/ui 根目录：{path}")
            : path.Replace('\\', '/');
    }

    private static string ResolveAssetPath(string root, string relativePath, string fieldName)
    {
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        return IsUnderRoot(root, fullPath)
            ? fullPath
            : throw new InvalidDataException($"UI manifest 字段 {fieldName} 逃逸 content/ui 根目录：{relativePath}");
    }

    private static bool IsUnderRoot(string root, string fullPath)
    {
        string normalizedRoot = NormalizeRoot(root);
        string normalizedPath = Path.GetFullPath(fullPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return normalizedPath.StartsWith(normalizedRoot, comparison);
    }

    private static string NormalizeRoot(string root)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullRoot + Path.DirectorySeparatorChar;
    }
}

/// <summary>
/// ui-manifest.json 根 DTO。
/// </summary>
public sealed class UiManifestJson
{
    /// <summary>
    /// 屏幕定义数组。
    /// </summary>
    public UiManifestScreenJson[]? Screens { get; init; }
}

/// <summary>
/// ui-manifest.json 屏幕 DTO。
/// </summary>
public sealed class UiManifestScreenJson
{
    /// <summary>
    /// 屏幕稳定字符串 id。
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// 相对 content/ui 根目录的文档路径。
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 是否预载。
    /// </summary>
    public bool Preload { get; init; }
}

/// <summary>
/// System.Text.Json source-generation 上下文。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(UiManifestJson))]
[JsonSerializable(typeof(UiManifestScreenJson[]))]
public sealed partial class UiManifestJsonContext : JsonSerializerContext
{
}
