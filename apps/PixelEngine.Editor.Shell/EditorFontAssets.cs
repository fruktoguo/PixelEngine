namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor 发行包内的确定性字体资产。
/// </summary>
internal static class EditorFontAssets
{
    internal const string FontsDirectoryName = "Fonts";
    internal const string PrimaryFontFileName = "Inter-Regular.ttf";
    internal const string CjkFallbackFontFileName = "NotoSansSC-VF.ttf";

    /// <summary>
    /// 从应用目录解析并校验 Editor 字体栈，缺失资产时立即失败，绝不静默依赖系统字体。
    /// </summary>
    /// <param name="applicationBaseDirectory">应用基目录；为空时使用 <see cref="AppContext.BaseDirectory"/>。</param>
    /// <returns>Inter 主字体与 Noto Sans SC fallback 的绝对路径。</returns>
    public static EditorFontStackPaths Resolve(string? applicationBaseDirectory = null)
    {
        string baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(applicationBaseDirectory);
        string fontsDirectory = Path.Combine(baseDirectory, FontsDirectoryName);
        return new EditorFontStackPaths(
            RequireFont(fontsDirectory, PrimaryFontFileName),
            RequireFont(fontsDirectory, CjkFallbackFontFileName));
    }

    private static string RequireFont(string fontsDirectory, string fileName)
    {
        string path = Path.GetFullPath(Path.Combine(fontsDirectory, fileName));
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"PixelEngine Editor 字体资产缺失：{fileName}。请重新构建或安装完整 Editor 包。",
                path);
    }
}

/// <summary>
/// Editor 确定性字体栈路径。
/// </summary>
/// <param name="PrimaryFontPath">Inter 拉丁与数字主字体。</param>
/// <param name="CjkFallbackFontPath">Noto Sans SC CJK fallback。</param>
internal readonly record struct EditorFontStackPaths(
    string PrimaryFontPath,
    string CjkFallbackFontPath);
