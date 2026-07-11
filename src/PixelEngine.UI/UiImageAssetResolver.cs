namespace PixelEngine.UI;

/// <summary>
/// UI 图片资产路径解析器，将 RmlUi 文档中的 <c>src</c> 或 <c>data-image</c> 解析为 content/ui/images 下的 PNG 绝对路径。
/// </summary>
internal static class UiImageAssetResolver
{
    /// <summary>
    /// 解析 UI 图片路径；要求最终文件位于 <c>content/ui/images</c> 目录下且存在。
    /// </summary>
    /// <param name="documentPath">引用该图片的 RmlUi 文档路径。</param>
    /// <param name="imageId"><c>data-image</c> 标识；与 <paramref name="source" /> 二选一。</param>
    /// <param name="source"><c>src</c> 相对或绝对路径；与 <paramref name="imageId" /> 二选一。</param>
    /// <returns>已验证的 PNG 绝对路径。</returns>
    internal static string ResolveImagePath(string documentPath, string? imageId, string? source)
    {
        if (string.IsNullOrWhiteSpace(imageId) && string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidDataException("<img> 必须声明 src 或 data-image。");
        }

        string documentFullPath = Path.GetFullPath(documentPath);
        string documentDirectory = Path.GetDirectoryName(documentFullPath) ??
            throw new InvalidDataException("UI 文档路径缺少目录。");
        string uiRoot = string.Equals(Path.GetFileName(documentDirectory), "screens", StringComparison.OrdinalIgnoreCase) &&
            Directory.GetParent(documentDirectory) is DirectoryInfo parent
                ? parent.FullName
                : documentDirectory;
        string imagesDirectory = EnsureTrailingSeparator(Path.GetFullPath(Path.Combine(uiRoot, "images")));
        string candidate = !string.IsNullOrWhiteSpace(source)
            ? ResolveImageSource(uiRoot, documentDirectory, source)
            : Path.Combine(imagesDirectory, imageId + ".png");
        string fullPath = Path.GetFullPath(candidate);
        return !IsUnderDirectory(imagesDirectory, fullPath)
            ? throw new InvalidDataException($"UI 图片必须位于 content/ui/images 目录：{fullPath}")
            : File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException("找不到 UI 图片资产。", fullPath);
    }

    /// <summary>
    /// 将 <c>src</c> 相对路径解析为绝对路径；支持以 <c>images/</c> 开头的 UI 根相对路径。
    /// </summary>
    private static string ResolveImageSource(string uiRoot, string documentDirectory, string source)
    {
        string normalized = source.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? normalized
            : normalized.StartsWith("images" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(uiRoot, normalized)
            : Path.Combine(documentDirectory, normalized);
    }

    /// <summary>
    /// 确保目录路径以目录分隔符结尾，便于前缀匹配校验。
    /// </summary>
    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// 判断文件路径是否位于指定目录树下（大小写不敏感）。
    /// </summary>
    private static bool IsUnderDirectory(string directory, string path)
    {
        string normalized = Path.GetFullPath(path);
        return normalized.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }
}
