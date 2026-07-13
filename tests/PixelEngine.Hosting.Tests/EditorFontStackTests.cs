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
                18f);

            AssertMergedStack(atlas, initial, 18f);

            ImFontPtr scaled = manager.RebuildFontAtlas(
                atlas,
                paths.PrimaryFontPath,
                paths.CjkFallbackFontPath,
                27f);

            AssertMergedStack(atlas, scaled, 27f);
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
}
