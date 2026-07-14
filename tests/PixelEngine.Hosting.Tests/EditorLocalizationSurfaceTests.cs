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
        string editorRoot = Path.Combine(root, "src", "PixelEngine.Editor");
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
            Path.Combine(shellRoot, "Build", "BuildSettingsPanel.cs"),
            Path.Combine(shellRoot, "GameObjectInspectorPanel.cs"),
            Path.Combine(shellRoot, "EditorMainMenuBar.cs"),
            Path.Combine(shellRoot, "SceneViewPanel.cs"),
            Path.Combine(shellRoot, "GameViewPanel.cs"),
            Path.Combine(shellRoot, "GameObjectHierarchyPanel.cs"),
            Path.Combine(shellRoot, "EditorConsolePanel.cs"),
            Path.Combine(editorRoot, "AssetBrowserPanel.cs"),
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
        string build = File.ReadAllText(Path.Combine(shellRoot, "Build", "BuildSettingsPanel.cs"));
        string inspector = File.ReadAllText(Path.Combine(shellRoot, "GameObjectInspectorPanel.cs"));

        Assert.DoesNotContain("工程级 authoring 设置", project, StringComparison.Ordinal);
        Assert.DoesNotContain("玩家包运行时与发布设置", player, StringComparison.Ordinal);
        Assert.DoesNotContain("重新预检", build, StringComparison.Ordinal);
        Assert.DoesNotContain("尚无构建结果", build, StringComparison.Ordinal);
        Assert.DoesNotContain("未选中 GameObject 或 Asset", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("Play 运行中：Authoring 数据只读", inspector, StringComparison.Ordinal);
        Assert.Contains("projectSettings.help", project, StringComparison.Ordinal);
        Assert.Contains("playerSettings.help", player, StringComparison.Ordinal);
        Assert.Contains("build.action.preflight", build, StringComparison.Ordinal);
        Assert.Contains("inspector.empty", inspector, StringComparison.Ordinal);
    }

    /// <summary>
    /// 本地化页签必须用 ### 保留 canonical window ID，避免切换语言破坏 dock 布局与 Window 菜单查找。
    /// </summary>
    [Fact]
    public void LocalizedCorePanelTitlesKeepStableDockIds()
    {
        string root = FindRepositoryRoot();
        string[] panels =
        [
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "SceneViewPanel.cs"),
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "GameViewPanel.cs"),
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "GameObjectHierarchyPanel.cs"),
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "GameObjectInspectorPanel.cs"),
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorConsolePanel.cs"),
            Path.Combine(root, "src", "PixelEngine.Editor", "AssetBrowserPanel.cs"),
        ];

        Assert.All(
            panels,
            path => Assert.Contains("###", File.ReadAllText(path), StringComparison.Ordinal));
    }

    /// <summary>
    /// Inspector 会显示脚本值、路径和诊断等任意文本；这些内容不得进入 ImGui printf 风格 API。
    /// Canvas Scaler 的枚举标签也必须按当前 locale 缓存刷新，而不是静态锁死英文。
    /// </summary>
    [Fact]
    public void InspectorUsesUnformattedTextAndLocalizedCanvasOptions()
    {
        string root = FindRepositoryRoot();
        string inspector = File.ReadAllText(Path.Combine(
            root,
            "apps",
            "PixelEngine.Editor.Shell",
            "GameObjectInspectorPanel.cs"));

        Assert.DoesNotContain("ImGui.Text(", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("ImGui.TextWrapped(", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("ImGui.TextColored(", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("ImGui.TextDisabled(", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("ImGui.SetTooltip(", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] ScaleModeLabels", inspector, StringComparison.Ordinal);
        Assert.Contains("RefreshCanvasLocalizedOptions", inspector, StringComparison.Ordinal);
        Assert.Contains("inspector.canvasScaler.mode.screen", inspector, StringComparison.Ordinal);
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
