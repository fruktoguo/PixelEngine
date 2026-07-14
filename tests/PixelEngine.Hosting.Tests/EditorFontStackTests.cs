using System.Security.Cryptography;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell;
using PixelEngine.Gui;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor 确定性字体栈与 ImGui atlas 合并测试。
/// </summary>
public sealed class EditorFontStackTests
{
    /// <summary>
    /// 验证构建输出携带固定版本的 Inter、Noto Sans SC 与两份许可证。
    /// </summary>
    [Fact]
    public void EditorOutputCarriesDeterministicFontAssetsAndLicenses()
    {
        EditorFontStackPaths paths = EditorFontAssets.Resolve(AppContext.BaseDirectory);
        string fontsDirectory = Path.GetDirectoryName(paths.PrimaryFontPath)!;

        Assert.Equal("Inter-Regular.ttf", Path.GetFileName(paths.PrimaryFontPath));
        Assert.Equal("NotoSansSC-VF.ttf", Path.GetFileName(paths.CjkFallbackFontPath));
        Assert.Equal(
            "FC87DAEF80EBD62CA64506A7BCB999172FCB57F2AB3B022899DA2F23FE3CB46C",
            ComputeSha256(paths.PrimaryFontPath));
        Assert.Equal(
            "D68BAFCB48A2707749396AA12BBBD833CB70401F3A9A689FD2902C7E0D295964",
            ComputeSha256(paths.CjkFallbackFontPath));
        Assert.Contains(
            "SIL OPEN FONT LICENSE Version 1.1",
            File.ReadAllText(Path.Combine(fontsDirectory, "Inter-LICENSE.txt")),
            StringComparison.Ordinal);
        Assert.Contains(
            "SIL OPEN FONT LICENSE Version 1.1",
            File.ReadAllText(Path.Combine(fontsDirectory, "NotoSansSC-OFL.txt")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Unity 6.5 的 EditorStyles label/text field 在 100% scale 使用 12px 字号和 18px 单行高度；
    /// Shell 与工程工作台两个 ImGui context 必须共用同一基础字号，避免默认 18px 再被 UI Scale 放大。
    /// </summary>
    [Fact]
    public void EditorFontBaselineMatchesUnityEditorDensity()
    {
        Assert.Equal(12f, PixelEngine.Editor.EditorAppOptions.DefaultFontSizePixels);
        Assert.Equal(
            PixelEngine.Editor.EditorAppOptions.DefaultFontSizePixels,
            EditorFontAssets.BaseFontSizePixels);
    }

    /// <summary>
    /// Windows 与 Unity 6.5 一样优先使用系统 Microsoft YaHei；非 Windows 或字体缺失时保留
    /// packaged Noto Sans SC，不能因为追求平台观感破坏可移植启动。
    /// </summary>
    [Fact]
    public void RuntimeCjkFontMatchesUnityOnWindowsAndKeepsPackagedFallback()
    {
        using TempDir temp = new();
        string packaged = Path.Combine(temp.Path, "NotoSansSC-VF.ttf");
        string windowsFonts = Path.Combine(temp.Path, "WindowsFonts");
        _ = Directory.CreateDirectory(windowsFonts);
        File.WriteAllText(packaged, "packaged");
        string microsoftYaHei = Path.Combine(windowsFonts, EditorFontAssets.WindowsUnityCjkFontFileName);
        File.WriteAllText(microsoftYaHei, "system");

        Assert.Equal(
            Path.GetFullPath(microsoftYaHei),
            EditorFontAssets.ResolveRuntimeCjkFallback(packaged, isWindows: true, windowsFonts));
        Assert.Equal(
            Path.GetFullPath(packaged),
            EditorFontAssets.ResolveRuntimeCjkFallback(packaged, isWindows: false, windowsFonts));

        File.Delete(microsoftYaHei);
        Assert.Equal(
            Path.GetFullPath(packaged),
            EditorFontAssets.ResolveRuntimeCjkFallback(packaged, isWindows: true, windowsFonts));
    }

    /// <summary>
    /// Project Picker/Preferences 与工程工作台使用两个独立 ImGui context；两者都必须显式采用
    /// 同一 Editor 字号和运行时字体栈，避免打开工程后字体密度突然改变。
    /// </summary>
    [Fact]
    public void BothEditorContextsUseTheSameRuntimeFontStackAndBaseline()
    {
        string root = FindRepositoryRoot();
        string shellWindow = File.ReadAllText(Path.Combine(
            root,
            "apps",
            "PixelEngine.Editor.Shell",
            "EditorShellWindow.cs"));
        string hostExtension = File.ReadAllText(Path.Combine(
            root,
            "apps",
            "PixelEngine.Editor.Shell",
            "EditorShellHostExtension.cs"));

        foreach (string source in new[] { shellWindow, hostExtension })
        {
            Assert.Contains("EditorFontAssets.ResolveRuntime()", source, StringComparison.Ordinal);
            Assert.Contains(
                "FontSizePixels = EditorFontAssets.BaseFontSizePixels",
                source,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 验证首次构建与 DPI scale 重建都只产生一个字体，并保留 CJK MergeMode source。
    /// </summary>
    [Fact]
    public unsafe void FontAtlasRebuildKeepsInterPrimaryAndMergedCjkFallback()
    {
        EditorFontStackPaths paths = EditorFontAssets.Resolve(AppContext.BaseDirectory);
        using GuiFontManager manager = new();
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;

            ImFontPtr initial = manager.RebuildFontAtlas(
                atlas,
                paths.PrimaryFontPath,
                paths.CjkFallbackFontPath,
                EditorFontAssets.BaseFontSizePixels);

            AssertMergedStack(atlas, initial, EditorFontAssets.BaseFontSizePixels);

            ImFontPtr scaled = manager.RebuildFontAtlas(
                atlas,
                paths.PrimaryFontPath,
                paths.CjkFallbackFontPath,
                EditorFontAssets.BaseFontSizePixels * 1.5f);

            AssertMergedStack(atlas, scaled, EditorFontAssets.BaseFontSizePixels * 1.5f);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static string ComputeSha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
    }

    private static unsafe void AssertMergedStack(ImFontAtlasPtr atlas, ImFontPtr primaryFont, float expectedSize)
    {
        Assert.False(primaryFont.IsNull);
        Assert.Equal(1, atlas.Fonts.Size);
        Assert.Equal(2, atlas.Sources.Size);

        ImFontConfig primarySource = atlas.Sources[0];
        ImFontConfig fallbackSource = atlas.Sources[1];
        Assert.Equal(0, primarySource.MergeMode);
        Assert.Equal(1, fallbackSource.MergeMode);
        Assert.Equal(expectedSize, primarySource.SizePixels);
        Assert.Equal(expectedSize, fallbackSource.SizePixels);
        Assert.True(fallbackSource.DstFont == primaryFont.Handle);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pixelengine-editor-fonts-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
