using System.Text.Json;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// Editor 外置语言包测试。
/// </summary>
public sealed class EditorLocalizationTests
{
    /// <summary>
    /// 验证外置 locale 可发现、切换并按 English/fallback 顺序回退。
    /// </summary>
    [Fact]
    public void ExternalLanguagePacksLoadSwitchAndFallback()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-localization-{Guid.NewGuid():N}");
        try
        {
            _ = Directory.CreateDirectory(root);
            WritePack(root, "en-US", "English", new Dictionary<string, string>
            {
                ["menu.file"] = "File",
                ["english.only"] = "English fallback",
            });
            WritePack(root, "zh-CN", "简体中文", new Dictionary<string, string>
            {
                ["menu.file"] = "文件",
            });

            EditorLocalization.Configure([root], "zh-CN");

            Assert.Equal("zh-CN", EditorLocalization.CurrentLocale);
            Assert.Equal("文件", EditorLocalization.Get("menu.file", "fallback"));
            Assert.Equal("English fallback", EditorLocalization.Get("english.only", "fallback"));
            Assert.Equal("fallback", EditorLocalization.Get("missing", "fallback"));
            Assert.Equal(2, EditorLocalization.AvailableLanguages.Count);
            Assert.True(EditorLocalization.TrySetLocale("en-US"));
            Assert.Equal("File", EditorLocalization.Get("menu.file", "fallback"));
            Assert.False(EditorLocalization.TrySetLocale("missing"));
        }
        finally
        {
            EditorLocalization.Configure([], "en-US");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WritePack(string root, string locale, string displayName, Dictionary<string, string> strings)
    {
        string json = JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            locale,
            displayName,
            strings,
        });
        File.WriteAllText(Path.Combine(root, $"{locale}.json"), json);
    }
}
