using System.Numerics;
using System.Text.Json;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell;
using PixelEngine.Editor.Shell.Settings;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 用户级 Editor Preferences、UI Scale 与快捷键同源契约。
/// </summary>
public sealed class EditorPreferencesTests
{
    /// <summary>
    /// 150% 与其他用户级设置通过临时文件原子替换后可完整重载。
    /// </summary>
    [Fact]
    public void PreferencesRoundTripPersists150PercentAtomically()
    {
        using TempDir temp = new();
        string path = System.IO.Path.Combine(temp.Path, "preferences.json");
        EditorPreferencesStore store = EditorPreferencesStore.Load(path);

        bool saved = store.TryUpdate(
            store.Current with
            {
                UiScale = 1.5f,
                SaveLayoutOnExit = false,
                ReopenLastProject = false,
                RestoreLastScene = false,
                ExternalScriptEditor = "  code --goto {file}  ",
            },
            out string diagnostic);
        EditorPreferencesStore reloaded = EditorPreferencesStore.Load(path);

        Assert.True(saved, diagnostic);
        Assert.True(File.Exists(path));
        Assert.True(reloaded.LoadedFromDisk);
        Assert.Equal(1.5f, reloaded.Current.UiScale);
        Assert.False(reloaded.Current.SaveLayoutOnExit);
        Assert.False(reloaded.Current.ReopenLastProject);
        Assert.False(reloaded.Current.RestoreLastScene);
        Assert.Equal("code --goto {file}", reloaded.Current.ExternalScriptEditor);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    /// <summary>
    /// UI Scale 被夹取并量化到受支持的 5% 档位。
    /// </summary>
    [Theory]
    [InlineData(0.1f, 0.75f)]
    [InlineData(0.77f, 0.75f)]
    [InlineData(1.48f, 1.5f)]
    [InlineData(2.8f, 2f)]
    public void UiScaleNormalizesToSupportedFivePercentSteps(float input, float expected)
    {
        Assert.Equal(expected, EditorUiScale.Normalize(input));
    }

    /// <summary>
    /// 非有限倍率回退默认值，窗口尺寸在高倍率下仍受 viewport 约束。
    /// </summary>
    [Fact]
    public void UiScaleRejectsNonFiniteValuesAndFitsWindowsInsideViewport()
    {
        Assert.Equal(1f, EditorUiScale.Normalize(float.NaN));
        Assert.Equal(1f, EditorUiScale.Normalize(float.PositiveInfinity));
        Assert.Equal(150, EditorUiScale.ToPercent(1.5f));
        Assert.Equal(54f, EditorUiScale.Scale(36f, 1.5f));
        Assert.Equal(138f, EditorUiScale.Scale(92f, 1.5f));
        Assert.Equal(1f, EditorUiScale.GetScaleRatio(1.5f, 1.5f));
        Assert.Equal(1.5f, EditorUiScale.GetScaleRatio(1.5f, 1f));
        Assert.Equal(2f / 3f, EditorUiScale.GetScaleRatio(1f, 1.5f), precision: 5);
        Assert.Equal(new Vector2(1216f, 656f), EditorUiScale.FitWindow(new Vector2(820f, 540f), 2f, new Vector2(1280f, 720f)));
    }

    /// <summary>
    /// 同一倍率连续应用不会重复放大 style，切回 100% 能恢复基线尺寸。
    /// </summary>
    [Fact]
    public void UiScaleContextStateDoesNotCompoundAcrossFrames()
    {
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(context);
            EditorUiScaleContextState state = new();
            float baseline = ImGui.GetStyle().WindowMinSize.X;
            state.Apply(1.5f, 1.5f);
            float scaled = ImGui.GetStyle().WindowMinSize.X;

            for (int i = 0; i < 12; i++)
            {
                state.Apply(1.5f, 1.5f);
            }

            Assert.Equal(baseline * 1.5f, scaled, precision: 4);
            Assert.Equal(scaled, ImGui.GetStyle().WindowMinSize.X, precision: 4);
            state.Apply(1f, 1.5f);
            Assert.Equal(baseline, ImGui.GetStyle().WindowMinSize.X, precision: 4);
            Assert.Equal(2f / 3f, ImGui.GetStyle().FontScaleMain, precision: 4);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>
    /// 损坏的偏好文件不会阻止启动，并保留可见诊断。
    /// </summary>
    [Fact]
    public void CorruptPreferencesFallBackWithVisibleDiagnostic()
    {
        using TempDir temp = new();
        string path = System.IO.Path.Combine(temp.Path, "preferences.json");
        File.WriteAllText(path, "{ invalid json");

        EditorPreferencesStore store = EditorPreferencesStore.Load(path);

        Assert.False(store.LoadedFromDisk);
        Assert.Equal(EditorUiScale.Default, store.Current.UiScale);
        Assert.Contains("读取 Editor Preferences 失败", store.LastDiagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 旧工程不得通过迁移让自定义 executable command 进入用户信任域。
    /// </summary>
    [Fact]
    public void LegacyProjectMigrationNeverTrustsCustomExecutableCommand()
    {
        using TempDir temp = new();
        string path = System.IO.Path.Combine(temp.Path, "preferences.json");
        EditorPreferencesStore store = EditorPreferencesStore.Load(path);
        EditorPreferencesDto legacy = new()
        {
            SaveLayoutOnExit = false,
            ExternalScriptEditor = "malicious-editor --open {file}",
        };

        bool migrated = store.TryMigrateLegacy(legacy, out string diagnostic);

        Assert.True(migrated);
        Assert.False(store.Current.SaveLayoutOnExit);
        Assert.Equal(ExternalCodeEditorPreference.VsCode, store.Current.ExternalScriptEditor);
        Assert.Contains("已忽略", diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证真实 v1 空 editor 偏好逐字段迁移到 v2 + VS Code，且不丢失其它用户设置。
    /// </summary>
    [Fact]
    public void VersionOneEmptyEditorMigratesToVersionTwoVsCodeAndPreservesPreferences()
    {
        using TempDir temp = new();
        string path = System.IO.Path.Combine(temp.Path, "preferences.json");
        File.WriteAllText(
            path,
            """
            {
              "formatVersion": 1,
              "uiScale": 1.4,
              "saveLayoutOnExit": false,
              "reopenLastProject": false,
              "restoreLastScene": false,
              "externalScriptEditor": "",
              "language": "zh-CN"
            }
            """);

        EditorPreferencesStore store = EditorPreferencesStore.Load(path);

        Assert.True(store.LoadedFromDisk, store.LastDiagnostic);
        Assert.Equal(EditorPreferencesDocument.CurrentFormatVersion, store.Current.FormatVersion);
        Assert.Equal(1.4f, store.Current.UiScale);
        Assert.False(store.Current.SaveLayoutOnExit);
        Assert.False(store.Current.ReopenLastProject);
        Assert.False(store.Current.RestoreLastScene);
        Assert.Equal("zh-CN", store.Current.Language);
        Assert.Equal(ExternalCodeEditorPreference.VsCode, store.Current.ExternalScriptEditor);
        using JsonDocument migrated = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            EditorPreferencesDocument.CurrentFormatVersion,
            migrated.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(
            ExternalCodeEditorPreference.VsCode,
            migrated.RootElement.GetProperty("externalScriptEditor").GetString());
    }

    /// <summary>
    /// 验证 v2 显式 System Default sentinel 不会在重载时迁回 VS Code。
    /// </summary>
    [Fact]
    public void VersionTwoSystemDefaultSentinelRoundTripsWithoutMigration()
    {
        using TempDir temp = new();
        string path = System.IO.Path.Combine(temp.Path, "preferences.json");
        EditorPreferencesStore store = EditorPreferencesStore.Load(path);
        Assert.True(store.TryUpdate(
            store.Current with { ExternalScriptEditor = ExternalCodeEditorPreference.SystemDefault },
            out string diagnostic), diagnostic);

        EditorPreferencesStore reloaded = EditorPreferencesStore.Load(path);

        Assert.Equal(EditorPreferencesDocument.CurrentFormatVersion, reloaded.Current.FormatVersion);
        Assert.Equal(ExternalCodeEditorPreference.SystemDefault, reloaded.Current.ExternalScriptEditor);
    }

    /// <summary>
    /// 验证旧工程明确填写 system-default 时迁移为 v2 sentinel；旧空值仍保留新版 VS Code 默认。
    /// </summary>
    [Theory]
    [InlineData("system-default")]
    [InlineData("default")]
    public void LegacyExplicitSystemDefaultMigratesToVersionTwoSentinel(string legacyValue)
    {
        EditorPreferencesStore store = EditorPreferencesStore.CreateInMemory();

        Assert.True(store.TryMigrateLegacy(
            new EditorPreferencesDto { ExternalScriptEditor = legacyValue },
            out string diagnostic), diagnostic);

        Assert.Equal(ExternalCodeEditorPreference.SystemDefault, store.Current.ExternalScriptEditor);
        Assert.DoesNotContain("已忽略", diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 已存在的全局偏好不会被任意工程内旧字段覆盖。
    /// </summary>
    [Fact]
    public void ExistingGlobalPreferencesAlwaysWinOverLegacyProjectValues()
    {
        using TempDir temp = new();
        string path = System.IO.Path.Combine(temp.Path, "preferences.json");
        EditorPreferencesStore first = EditorPreferencesStore.Load(path);
        Assert.True(first.TryUpdate(
            first.Current with
            {
                UiScale = 1.5f,
                SaveLayoutOnExit = true,
                ExternalScriptEditor = "code {file}",
            },
            out string saveDiagnostic), saveDiagnostic);
        EditorPreferencesStore reloaded = EditorPreferencesStore.Load(path);

        Assert.True(reloaded.TryMigrateLegacy(
            new EditorPreferencesDto
            {
                SaveLayoutOnExit = false,
                ExternalScriptEditor = "project-command {file}",
            },
            out string migrationDiagnostic), migrationDiagnostic);

        Assert.Equal(1.5f, reloaded.Current.UiScale);
        Assert.True(reloaded.Current.SaveLayoutOnExit);
        Assert.Equal("code {file}", reloaded.Current.ExternalScriptEditor);
    }

    /// <summary>
    /// Preferences 无需打开工程即可打开指定类别。
    /// </summary>
    [Fact]
    public void PreferencesWindowIsProjectIndependentAndOpensRequestedCategory()
    {
        EditorPreferencesStore store = EditorPreferencesStore.CreateInMemory(
            new EditorPreferencesDocument { UiScale = 1.5f });
        EditorShellApp app = EditorShellApp.CreateForTests(store);

        app.ShowPreferences(EditorPreferencesCategory.Shortcuts);

        Assert.False(app.HasOpenProject);
        Assert.True(app.PreferencesWindow.Visible);
        Assert.Equal(EditorPreferencesCategory.Shortcuts, app.PreferencesWindow.SelectedCategory);
        Assert.Equal(1.5f, app.UiScale);
    }

    /// <summary>
    /// 验证 Preferences 在窄窗口或 200% UI Scale 下折叠固定侧栏，保留最小可编辑设置宽度。
    /// </summary>
    [Fact]
    public void PreferencesNavigationCollapsesAtScaledReadableWidth()
    {
        Assert.False(EditorPreferencesWindow.UseCompactNavigation(470f, 1f));
        Assert.True(EditorPreferencesWindow.UseCompactNavigation(469f, 1f));
        Assert.False(EditorPreferencesWindow.UseCompactNavigation(940f, 2f));
        Assert.True(EditorPreferencesWindow.UseCompactNavigation(939f, 2f));
        Assert.True(EditorPreferencesWindow.UseCompactNavigation(float.NaN, 1f));
    }

    /// <summary>
    /// 验证 Preferences 的 label/value 分栏在宽窗口、高 UI Scale 与窄值区之间保留响应式预算。
    /// </summary>
    [Fact]
    public void PreferencesFieldLabelWidthPreservesReadableValueColumn()
    {
        Assert.Equal(220f, EditorPreferencesWindow.ResolvePreferenceLabelWidth(720f, 1f));
        Assert.Equal(217.6f, EditorPreferencesWindow.ResolvePreferenceLabelWidth(640f, 1.5f), precision: 3);
        Assert.Equal(160f, EditorPreferencesWindow.ResolvePreferenceLabelWidth(400f, 1.5f));
        Assert.Equal(1f, EditorPreferencesWindow.ResolvePreferenceLabelWidth(float.NaN, 1f));
    }

    /// <summary>
    /// 验证自定义编辑器命令先作为可校验草稿存在：空值、未闭合引号和 executable 占位符
    /// 不可应用，合法带空格路径与定位参数可应用。
    /// </summary>
    [Fact]
    public void CustomEditorCommandDraftRequiresValidCommandBeforeApply()
    {
        Assert.False(EditorPreferencesWindow.TryValidateCustomEditorCommand(string.Empty, out string emptyDiagnostic));
        Assert.Contains("不能为空", emptyDiagnostic, StringComparison.Ordinal);
        Assert.False(EditorPreferencesWindow.TryValidateCustomEditorCommand("\"\" --goto {file}", out string emptyExecutableDiagnostic));
        Assert.Contains("不能为空", emptyExecutableDiagnostic, StringComparison.Ordinal);
        Assert.False(EditorPreferencesWindow.TryValidateCustomEditorCommand("\"C:\\Tools\\Code.exe", out string quoteDiagnostic));
        Assert.Contains("未闭合", quoteDiagnostic, StringComparison.Ordinal);
        Assert.False(EditorPreferencesWindow.TryValidateCustomEditorCommand(
            "{file} --goto {line}:{column}",
            out string executableDiagnostic));
        Assert.Contains("executable", executableDiagnostic, StringComparison.Ordinal);
        Assert.True(EditorPreferencesWindow.TryValidateCustomEditorCommand(
            "\"C:\\Program Files\\Editor\\editor.exe\" --goto \"{file}:{line}:{column}\"",
            out string validDiagnostic));
        Assert.Empty(validDiagnostic);
    }

    /// <summary>
    /// 菜单、帮助与命令调度共用唯一且无冲突的快捷键表。
    /// </summary>
    [Fact]
    public void ShortcutCatalogUsesUniqueRealCommandBindings()
    {
        ReadOnlySpan<EditorShortcutDefinition> shortcuts = EditorShortcutCatalog.All;
        HashSet<int> chords = [];
        HashSet<EditorShortcutCommand> commands = [];
        for (int i = 0; i < shortcuts.Length; i++)
        {
            Assert.True(chords.Add(shortcuts[i].KeyChord));
            Assert.True(commands.Add(shortcuts[i].Command));
            Assert.False(string.IsNullOrWhiteSpace(shortcuts[i].DisplayText));
        }

        Assert.Equal("Ctrl+S", EditorShortcutCatalog.Get(EditorShortcutCommand.SaveScene).DisplayText);
        Assert.Equal("Ctrl+Shift+B", EditorShortcutCatalog.Get(EditorShortcutCommand.OpenBuildSettings).DisplayText);
        Assert.Equal("Ctrl+B", EditorShortcutCatalog.Get(EditorShortcutCommand.BuildAndRun).DisplayText);
        Assert.Equal("Ctrl+,", EditorShortcutCatalog.Get(EditorShortcutCommand.OpenPreferences).DisplayText);
    }

    /// <summary>
    /// 脚本化 Preferences probe 可在无工程窗口中打开真实 Appearance 页面。
    /// </summary>
    [Fact]
    public void PreferencesProbeOptionParsesWithoutProject()
    {
        EditorShellOptions options = EditorShellOptions.Parse(["--scripted-preferences-probe", "--window-ticks", "3"]);

        Assert.True(options.ScriptedPreferencesProbe);
        Assert.Equal(3, options.WindowTicks);
        Assert.Null(options.ProjectPath);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelengine-preferences-" + Guid.NewGuid().ToString("N"));
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
