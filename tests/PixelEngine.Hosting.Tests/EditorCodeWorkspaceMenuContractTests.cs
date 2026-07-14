using System.Text.Json;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Unity-like Assets 菜单与中英文语言包的源码契约测试。
/// </summary>
public sealed class EditorCodeWorkspaceMenuContractTests
{
    /// <summary>
    /// 验证 Assets > Open C# Project 直接接线到当前 EditorShellApp，而非只藏在 Preferences。
    /// </summary>
    [Fact]
    public void AssetsMenuExposesOpenCSharpProjectAction()
    {
        string root = FindRepositoryRoot();
        string menu = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorMainMenuBar.cs"));

        Assert.Contains("DrawAssetsMenu(app);", menu, StringComparison.Ordinal);
        Assert.Contains("L.Get(\"menu.assets\", \"Assets\")", menu, StringComparison.Ordinal);
        Assert.Contains("L.Get(\"action.openCSharpProject\", \"Open C# Project\")", menu, StringComparison.Ordinal);
        Assert.Contains("app.OpenCSharpProject(out _)", menu, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证中英文语言包同时提供菜单、编辑器 selector 与迁移后默认体验文案。
    /// </summary>
    [Theory]
    [InlineData("en-US.json", "Assets", "Open C# Project", "Visual Studio Code (Recommended)")]
    [InlineData("zh-CN.json", "资源", "打开 C# 工程", "Visual Studio Code（推荐）")]
    public void LanguagePacksContainCodeEditorWorkflowStrings(
        string fileName,
        string assetsMenu,
        string openProject,
        string recommendedEditor)
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "Localization", fileName);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement strings = document.RootElement.GetProperty("strings");

        Assert.Equal(assetsMenu, strings.GetProperty("menu.assets").GetString());
        Assert.Equal(openProject, strings.GetProperty("action.openCSharpProject").GetString());
        Assert.Equal(recommendedEditor, strings.GetProperty("prefs.editor.vscode").GetString());
        string customHelp = Assert.IsType<string>(strings.GetProperty("prefs.customEditorHelp").GetString());
        string scriptHelp = Assert.IsType<string>(strings.GetProperty("prefs.scriptEditorHelp").GetString());
        Assert.Contains("{solution}", customHelp, StringComparison.Ordinal);
        Assert.Contains("{workspace}", customHelp, StringComparison.Ordinal);
        Assert.Contains(".code-workspace", scriptHelp, StringComparison.Ordinal);
        Assert.Contains(".sln", scriptHelp, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Preferences 双栏中的解释文字全部来自语言包，切换语言后不会残留中文或英文硬编码。
    /// </summary>
    [Theory]
    [InlineData("en-US.json")]
    [InlineData("zh-CN.json")]
    public void LanguagePacksContainPreferenceFieldHelpStrings(string fileName)
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "Localization", fileName);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement strings = document.RootElement.GetProperty("strings");
        string[] requiredKeys =
        [
            "prefs.uiScaleHelp",
            "prefs.uiScaleRestartHelp",
            "prefs.saveLayoutHelp",
            "prefs.reopenProjectHelp",
            "prefs.restoreSceneHelp",
            "prefs.layout",
            "prefs.actions",
            "prefs.diagnostic",
            "prefs.shortcutsHelp",
        ];

        foreach (string key in requiredKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(strings.GetProperty(key).GetString()), key);
        }
    }

    /// <summary>
    /// ImGui 的 TextWrapped 会把本地化字符串中的百分号当作 printf 格式符；Preferences 必须使用
    /// TextUnformatted 配合显式 wrap，避免“150% is”读取随机 vararg 并污染画面。
    /// </summary>
    [Fact]
    public void PreferencesLocalizedCopyUsesUnformattedWrappedText()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "apps",
            "PixelEngine.Editor.Shell",
            "Settings",
            "EditorPreferencesWindow.cs"));

        Assert.DoesNotContain("ImGui.TextWrapped(", source, StringComparison.Ordinal);
        Assert.Contains("ImGui.PushTextWrapPos", source, StringComparison.Ordinal);
        Assert.Contains("ImGui.TextUnformatted(text)", source, StringComparison.Ordinal);
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
}
