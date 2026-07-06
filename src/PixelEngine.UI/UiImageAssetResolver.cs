namespace PixelEngine.UI;

internal static class UiImageAssetResolver
{
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
            ? ResolveImageSource(uiRoot, documentDirectory, source!)
            : Path.Combine(imagesDirectory, imageId! + ".png");
        string fullPath = Path.GetFullPath(candidate);
        return !IsUnderDirectory(imagesDirectory, fullPath)
            ? throw new InvalidDataException($"UI 图片必须位于 content/ui/images 目录：{fullPath}")
            : File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException("找不到 UI 图片资产。", fullPath);
    }

    private static string ResolveImageSource(string uiRoot, string documentDirectory, string source)
    {
        string normalized = source.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? normalized
            : normalized.StartsWith("images" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(uiRoot, normalized)
            : Path.Combine(documentDirectory, normalized);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsUnderDirectory(string directory, string path)
    {
        string normalized = Path.GetFullPath(path);
        return normalized.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }
}
