namespace PixelEngine.UI;

/// <summary>
/// content/ui 下标准资产目录的规范化路径。
/// </summary>
/// <param name="RootDirectory">content/ui 根目录，带目录分隔符结尾。</param>
/// <param name="FontsDirectory">content/ui/fonts 目录，带目录分隔符结尾。</param>
/// <param name="ImagesDirectory">content/ui/images 目录，带目录分隔符结尾。</param>
public readonly record struct UiAssetDirectories(
    string RootDirectory,
    string FontsDirectory,
    string ImagesDirectory)
{
    /// <summary>
    /// 从 content/ui 根目录创建标准资产目录契约。
    /// </summary>
    /// <param name="rootDirectory">content/ui 根目录。</param>
    /// <returns>规范化后的资产目录。</returns>
    public static UiAssetDirectories FromRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        string root = NormalizeDirectory(rootDirectory);
        return new UiAssetDirectories(
            root,
            NormalizeDirectory(Path.Combine(root, "fonts")),
            NormalizeDirectory(Path.Combine(root, "images")));
    }

    /// <summary>
    /// 判断 fonts 目录当前是否存在。
    /// </summary>
    public bool HasFontsDirectory => Directory.Exists(FontsDirectory);

    /// <summary>
    /// 判断 images 目录当前是否存在。
    /// </summary>
    public bool HasImagesDirectory => Directory.Exists(ImagesDirectory);

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath + Path.DirectorySeparatorChar;
    }
}
