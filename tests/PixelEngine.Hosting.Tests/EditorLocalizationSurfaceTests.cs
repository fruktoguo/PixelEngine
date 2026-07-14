using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Shell 内建语言包与核心设置/Inspector surface 的本地化契约。
/// </summary>
public sealed partial class EditorLocalizationSurfaceTests
{
    /// <summary>
    /// en-US 与 zh-CN 必须提供完全相同的键；核心 surface 源码引用的 Get/Format 键不能依赖 fallback 假装完整。
    /// </summary>
    [Fact]
    public void BuiltInLanguagePacksStayInParityAndCoverCoreEditorSurfaces()
    {
        string root = FindRepositoryRoot();
        string shellRoot = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        JsonObject english = LoadStrings(Path.Combine(shellRoot, "Localization", "en-US.json"));
        JsonObject chinese = LoadStrings(Path.Combine(shellRoot, "Localization", "zh-CN.json"));

        string[] englishKeys = [.. english.Select(pair => pair.Key).Order(StringComparer.Ordinal)];
        string[] chineseKeys = [.. chinese.Select(pair => pair.Key).Order(StringComparer.Ordinal)];
        Assert.Equal(englishKeys, chineseKeys);
        Assert.All(
            english,
            pair => Assert.False(
                pair.Value?.GetValue<string>().Any(IsHan) ?? false,
                $"en-US key '{pair.Key}' contains a CJK character."));

        string[] localizedSurfaces =
        [
            Path.Combine(shellRoot, "Settings", "ProjectSettingsPanel.cs"),
            Path.Combine(shellRoot, "Settings", "PlayerSettingsPanel.cs"),
            Path.Combine(shellRoot, "GameObjectInspectorPanel.cs"),
        ];
        foreach (string sourcePath in localizedSurfaces)
        {
            string source = File.ReadAllText(sourcePath);
            foreach (Match match in LocalizationKeyCall().Matches(source))
            {
                string key = match.Groups["key"].Value;
                Assert.True(english.ContainsKey(key), $"en-US is missing '{key}' referenced by {Path.GetFileName(sourcePath)}.");
                Assert.True(chinese.ContainsKey(key), $"zh-CN is missing '{key}' referenced by {Path.GetFileName(sourcePath)}.");
            }
        }
    }

    /// <summary>
    /// 防止本轮真实窗口中出现过的中英文混杂文案重新绕过语言包。
    /// </summary>
    [Fact]
    public void CoreSettingsAndInspectorDoNotRestoreKnownHardcodedChineseChrome()
    {
        string root = FindRepositoryRoot();
        string shellRoot = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string project = File.ReadAllText(Path.Combine(shellRoot, "Settings", "ProjectSettingsPanel.cs"));
        string player = File.ReadAllText(Path.Combine(shellRoot, "Settings", "PlayerSettingsPanel.cs"));
        string inspector = File.ReadAllText(Path.Combine(shellRoot, "GameObjectInspectorPanel.cs"));

        Assert.DoesNotContain("工程级 authoring 设置", project, StringComparison.Ordinal);
        Assert.DoesNotContain("玩家包运行时与发布设置", player, StringComparison.Ordinal);
        Assert.DoesNotContain("未选中 GameObject 或 Asset", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("Play 运行中：Authoring 数据只读", inspector, StringComparison.Ordinal);
        Assert.Contains("projectSettings.help", project, StringComparison.Ordinal);
        Assert.Contains("playerSettings.help", player, StringComparison.Ordinal);
        Assert.Contains("inspector.empty", inspector, StringComparison.Ordinal);
    }

    private static JsonObject LoadStrings(string path)
    {
        JsonObject document = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException($"无法解析语言包：{path}");
        return document["strings"]?.AsObject()
            ?? throw new InvalidOperationException($"语言包缺少 strings：{path}");
    }

    private static bool IsHan(char value)
    {
        return value is
            (>= '\u3400' and <= '\u4dbf') or
            (>= '\u4e00' and <= '\u9fff') or
            (>= '\uf900' and <= '\ufaff');
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

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }

    [GeneratedRegex("(?:EditorLocalization|L)\\.(?:Get|Format)\\(\\s*\"(?<key>[^\"]+)\"")]
    private static partial Regex LocalizationKeyCall();
}
