namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 启动参数与脚本化验收探针开关。
/// </summary>
internal sealed record EditorShellOptions(
    string? ProjectPath,
    string? ScenePath,
    int WindowTicks,
    bool ScriptedProbe,
    bool ScriptedBuildProbe,
    bool ScriptedBuildRunProbe,
    bool ScriptedBuildCancelProbe,
    bool ScriptedBuildSettingsProbe,
    bool ScriptedMenuLayoutProbe,
    bool ScriptedHierarchyProbe,
    bool ScriptedDefaultWorkbenchProbe,
    bool ScriptedPreferencesProbe,
    string? BuildOutputPath,
    string? CaptureFramePath,
    string? LogDirectory)
{
    /// <summary>
    /// 显式指定的 Editor 用户数据根目录；为空时由环境变量或平台默认目录解析。
    /// </summary>
    public string? UserDataDirectory { get; init; }

    /// <summary>
    /// 是否使用隔离的临时用户状态。所有 scripted probe 默认启用，避免污染真实 Editor 状态。
    /// </summary>
    public bool EphemeralUserState { get; init; }

    /// <summary>
    /// 无显式 <c>--project</c> 时是否允许恢复上一个成功打开的工程。
    /// </summary>
    public bool ReopenLastProject { get; init; } = true;

    /// <summary>
    /// 是否运行保持在 Play 状态的 Game View 玩家可见性与移动验收探针。
    /// </summary>
    public bool ScriptedGameViewProbe { get; init; }

    /// <summary>
    /// 是否运行真实 Play Mode runtime entity 选择与 Inspector 绘制探针。
    /// </summary>
    public bool ScriptedRuntimeInspectorProbe { get; init; }

    public static EditorShellOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? scenePath = null;
        string? logDirectory = null;
        string? captureFramePath = null;
        string? buildOutputPath = null;
        int windowTicks = 0;
        bool scriptedProbe = false;
        bool scriptedBuildProbe = false;
        bool scriptedBuildRunProbe = false;
        bool scriptedBuildCancelProbe = false;
        bool scriptedBuildSettingsProbe = false;
        bool scriptedMenuLayoutProbe = false;
        bool scriptedHierarchyProbe = false;
        bool scriptedDefaultWorkbenchProbe = false;
        bool scriptedPreferencesProbe = false;
        bool scriptedGameViewProbe = false;
        bool scriptedRuntimeInspectorProbe = false;
        bool ephemeralUserState = false;
        bool reopenLastProject = true;
        string? userDataDirectory = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--project":
                    projectPath = RequireValue(args, ref i, arg);
                    break;
                case "--scene":
                    scenePath = RequireValue(args, ref i, arg);
                    break;
                case "--window-ticks":
                    windowTicks = int.Parse(RequireValue(args, ref i, arg), System.Globalization.CultureInfo.InvariantCulture);
                    if (windowTicks < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args), "window-ticks 不能为负数。");
                    }

                    break;
                case "--scripted-probe":
                    scriptedProbe = true;
                    break;
                case "--scripted-build-probe":
                    scriptedBuildProbe = true;
                    break;
                case "--scripted-build-run-probe":
                    scriptedBuildProbe = true;
                    scriptedBuildRunProbe = true;
                    break;
                case "--scripted-build-cancel-probe":
                    scriptedBuildProbe = true;
                    scriptedBuildCancelProbe = true;
                    break;
                case "--scripted-build-settings-probe":
                    scriptedBuildSettingsProbe = true;
                    break;
                case "--scripted-menu-layout-probe":
                    scriptedMenuLayoutProbe = true;
                    break;
                case "--scripted-hierarchy-probe":
                    scriptedHierarchyProbe = true;
                    break;
                case "--scripted-default-workbench-probe":
                    scriptedDefaultWorkbenchProbe = true;
                    break;
                case "--scripted-preferences-probe":
                    scriptedPreferencesProbe = true;
                    break;
                case "--scripted-gameview-probe":
                    scriptedGameViewProbe = true;
                    break;
                case "--scripted-runtime-inspector-probe":
                    scriptedRuntimeInspectorProbe = true;
                    break;
                case "--build-output":
                    buildOutputPath = RequireValue(args, ref i, arg);
                    break;
                case "--capture-frame":
                    captureFramePath = RequireValue(args, ref i, arg);
                    break;
                case "--log-directory":
                    logDirectory = RequireValue(args, ref i, arg);
                    break;
                case "--user-data-dir":
                    userDataDirectory = RequireValue(args, ref i, arg);
                    break;
                case "--ephemeral-user-state":
                    ephemeralUserState = true;
                    break;
                case "--no-reopen-last-project":
                    reopenLastProject = false;
                    break;
                default:
                    throw new ArgumentException($"未知参数：{arg}");
            }
        }

        bool hasScriptedProbe = scriptedProbe ||
            scriptedBuildProbe ||
            scriptedBuildRunProbe ||
            scriptedBuildCancelProbe ||
            scriptedBuildSettingsProbe ||
            scriptedMenuLayoutProbe ||
            scriptedHierarchyProbe ||
            scriptedDefaultWorkbenchProbe ||
            scriptedPreferencesProbe ||
            scriptedGameViewProbe ||
            scriptedRuntimeInspectorProbe;
        return new EditorShellOptions(projectPath, scenePath, windowTicks, scriptedProbe, scriptedBuildProbe, scriptedBuildRunProbe, scriptedBuildCancelProbe, scriptedBuildSettingsProbe, scriptedMenuLayoutProbe, scriptedHierarchyProbe, scriptedDefaultWorkbenchProbe, scriptedPreferencesProbe, buildOutputPath, captureFramePath, logDirectory)
        {
            UserDataDirectory = string.IsNullOrWhiteSpace(userDataDirectory) ? null : userDataDirectory.Trim(),
            EphemeralUserState = ephemeralUserState || hasScriptedProbe,
            ReopenLastProject = reopenLastProject,
            ScriptedGameViewProbe = scriptedGameViewProbe,
            ScriptedRuntimeInspectorProbe = scriptedRuntimeInspectorProbe,
        };
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} 缺少参数值。");
        }

        index++;
        return args[index];
    }
}
