namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor 发行包内的确定性字体资产。
/// </summary>
internal static class EditorFontAssets
{
    internal const string FontsDirectoryName = "Fonts";
    internal const string PrimaryFontFileName = "Inter-Regular.ttf";
    internal const string CjkFallbackFontFileName = "NotoSansSC-VF.ttf";
    internal const string WindowsUnityCjkFontFileName = "msyh.ttc";
    internal const float BaseFontSizePixels = PixelEngine.Editor.EditorAppOptions.DefaultFontSizePixels;

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

    /// <summary>
    /// 解析运行时 Editor 字体栈。Windows 上与 Unity 6.5 的 fontsettings 一致，优先使用系统
    /// Microsoft YaHei；缺失时仍回退发行包内的 Noto Sans SC，保证离线安装可启动。
    /// </summary>
    public static EditorFontStackPaths ResolveRuntime(string? applicationBaseDirectory = null)
    {
        EditorFontStackPaths packaged = Resolve(applicationBaseDirectory);
        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return packaged with
        {
            CjkFallbackFontPath = ResolveRuntimeCjkFallback(
                packaged.CjkFallbackFontPath,
                OperatingSystem.IsWindows(),
                fontsDirectory),
        };
    }

    internal static string ResolveRuntimeCjkFallback(
        string packagedFallbackPath,
        bool isWindows,
        string? windowsFontsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagedFallbackPath);
        if (isWindows && !string.IsNullOrWhiteSpace(windowsFontsDirectory))
        {
            string candidate = Path.GetFullPath(Path.Combine(windowsFontsDirectory, WindowsUnityCjkFontFileName));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(packagedFallbackPath);
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
/// <param name="CjkFallbackFontPath">运行时 CJK fallback；Windows 优先 Microsoft YaHei，其余回退 Noto Sans SC。</param>
internal readonly record struct EditorFontStackPaths(
    string PrimaryFontPath,
    string CjkFallbackFontPath);
