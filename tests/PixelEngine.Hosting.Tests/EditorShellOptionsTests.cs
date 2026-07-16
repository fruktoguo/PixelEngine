using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor Shell 用户状态参数与路径解析契约。
/// </summary>
public sealed class EditorShellOptionsTests
{
    /// <summary>
    /// 验证用户数据目录、临时状态与禁止自动恢复参数可共同解析。
    /// </summary>
    [Fact]
    public void ParseRecognizesUserDataAndReopenControls()
    {
        EditorShellOptions options = EditorShellOptions.Parse(
        [
            "--user-data-dir",
            "  isolated-editor-state  ",
            "--ephemeral-user-state",
            "--no-reopen-last-project",
        ]);

        Assert.Equal("isolated-editor-state", options.UserDataDirectory);
        Assert.True(options.EphemeralUserState);
        Assert.False(options.ReopenLastProject);
    }

    /// <summary>
    /// 验证普通交互启动默认使用持久状态并允许恢复上一个工程。
    /// </summary>
    [Fact]
    public void ParseDefaultsToPersistentStateAndReopenEnabled()
    {
        EditorShellOptions options = EditorShellOptions.Parse([]);

        Assert.Null(options.UserDataDirectory);
        Assert.False(options.EphemeralUserState);
        Assert.True(options.ReopenLastProject);
        Assert.True(options.AutomationEnabled);
    }

    /// <summary>验证 automation Server 可显式关闭，并 canonicalize 三个安全路径覆盖。</summary>
    [Fact]
    public void ParseRecognizesAutomationSecurityAndStorageOptions()
    {
        EditorShellOptions options = EditorShellOptions.Parse(
        [
            "--disable-automation",
            "--automation-discovery-root",
            " automation-discovery ",
            "--automation-artifact-root",
            " automation-artifacts ",
            "--automation-credential",
            " automation.token ",
        ]);

        Assert.False(options.AutomationEnabled);
        Assert.Equal(Path.GetFullPath("automation-discovery"), options.AutomationDiscoveryRoot);
        Assert.Equal(Path.GetFullPath("automation-artifacts"), options.AutomationArtifactRoot);
        Assert.Equal(Path.GetFullPath("automation.token"), options.AutomationCredentialPath);
    }

    /// <summary>
    /// 验证所有 scripted probe 默认隔离用户状态。
    /// </summary>
    [Theory]
    [InlineData("--scripted-probe")]
    [InlineData("--scripted-build-probe")]
    [InlineData("--scripted-build-run-probe")]
    [InlineData("--scripted-build-cancel-probe")]
    [InlineData("--scripted-build-settings-probe")]
    [InlineData("--scripted-menu-layout-probe")]
    [InlineData("--scripted-hierarchy-probe")]
    [InlineData("--scripted-default-workbench-probe")]
    [InlineData("--scripted-preferences-probe")]
    [InlineData("--scripted-gameview-probe")]
    [InlineData("--scripted-runtime-inspector-probe")]
    [InlineData("--physical-ui-input-probe")]
    public void EveryScriptedProbeDefaultsToEphemeralUserState(string probeFlag)
    {
        EditorShellOptions options = EditorShellOptions.Parse([probeFlag]);

        Assert.True(options.EphemeralUserState);
    }

    /// <summary>
    /// 验证 runtime Inspector 真实窗口探针使用独立开关，不会误启用 Game View 探针。
    /// </summary>
    [Fact]
    public void ParseRecognizesRuntimeInspectorProbeIndependently()
    {
        EditorShellOptions options = EditorShellOptions.Parse(["--scripted-runtime-inspector-probe"]);

        Assert.True(options.ScriptedRuntimeInspectorProbe);
        Assert.False(options.ScriptedGameViewProbe);
    }

    /// <summary>
    /// 验证设置面板真实窗口探针要求显式合法目标，并自动隔离用户状态。
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("player")]
    public void ParseRecognizesSettingsPanelProbe(string target)
    {
        EditorShellOptions options = EditorShellOptions.Parse(["--scripted-settings-panel-probe", target]);

        Assert.Equal(target, options.ScriptedSettingsPanelProbe);
        Assert.True(options.EphemeralUserState);
        _ = Assert.Throws<ArgumentException>(() =>
            EditorShellOptions.Parse(["--scripted-settings-panel-probe", "unknown"]));
    }

    /// <summary>
    /// 验证 authoring Inspector 真实窗口探针只接受正 stable ID，并自动隔离用户状态。
    /// </summary>
    [Fact]
    public void ParseRecognizesAuthoringInspectorProbe()
    {
        EditorShellOptions options = EditorShellOptions.Parse(
            ["--scripted-authoring-inspector-probe", "4"]);

        Assert.Equal(4, options.ScriptedAuthoringInspectorProbeStableId);
        Assert.True(options.EphemeralUserState);
        _ = Assert.Throws<ArgumentException>(() =>
            EditorShellOptions.Parse(["--scripted-authoring-inspector-probe", "0"]));
        _ = Assert.Throws<ArgumentException>(() =>
            EditorShellOptions.Parse(["--scripted-authoring-inspector-probe", "not-an-id"]));
    }

    /// <summary>
    /// 验证 CLI 用户数据目录优先于环境变量。
    /// </summary>
    [Fact]
    public void UserDataPathsPreferCommandLineOverEnvironment()
    {
        string cliRoot = Path.Combine(Path.GetTempPath(), "pixelengine-cli-user-data");
        string environmentRoot = Path.Combine(Path.GetTempPath(), "pixelengine-env-user-data");
        EditorShellOptions options = EditorShellOptions.Parse(["--user-data-dir", cliRoot]);

        EditorUserDataPaths paths = EditorUserDataPaths.Resolve(options, environmentRoot, ephemeralDirectory: null);

        Assert.Equal(Path.GetFullPath(cliRoot), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "editor-workspace.json"), paths.WorkspacePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "recent-projects.json"), paths.RecentProjectsPath);
        Assert.False(paths.IsEphemeral);
    }

    /// <summary>
    /// 验证未提供 CLI 覆盖时使用统一环境变量目录。
    /// </summary>
    [Fact]
    public void UserDataPathsUseEnvironmentWhenCommandLineIsAbsent()
    {
        string environmentRoot = Path.Combine(Path.GetTempPath(), "pixelengine-env-user-data");
        EditorShellOptions options = EditorShellOptions.Parse([]);

        EditorUserDataPaths paths = EditorUserDataPaths.Resolve(options, environmentRoot, ephemeralDirectory: null);

        Assert.Equal(Path.GetFullPath(environmentRoot), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "editor-preferences.json"), paths.PreferencesPath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "editor-shell-imgui.ini"), paths.LayoutPath);
    }

    /// <summary>
    /// 验证无显式目录的临时状态解析到隔离目录。
    /// </summary>
    [Fact]
    public void EphemeralStateWithoutConfiguredRootUsesIsolatedDirectory()
    {
        string ephemeralRoot = Path.Combine(Path.GetTempPath(), "pixelengine-ephemeral-user-data");
        EditorShellOptions options = EditorShellOptions.Parse(["--ephemeral-user-state"]);

        EditorUserDataPaths paths = EditorUserDataPaths.Resolve(
            options,
            environmentUserDataDirectory: null,
            ephemeralDirectory: ephemeralRoot);

        Assert.True(paths.IsEphemeral);
        Assert.Equal(Path.GetFullPath(ephemeralRoot), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "recovery"), paths.RecoveryRoot);
    }
}
