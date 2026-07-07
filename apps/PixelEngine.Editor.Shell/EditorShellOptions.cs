namespace PixelEngine.Editor.Shell;

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
    string? BuildOutputPath,
    string? CaptureFramePath,
    string? LogDirectory)
{
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
                case "--build-output":
                    buildOutputPath = RequireValue(args, ref i, arg);
                    break;
                case "--capture-frame":
                    captureFramePath = RequireValue(args, ref i, arg);
                    break;
                case "--log-directory":
                    logDirectory = RequireValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"未知参数：{arg}");
            }
        }

        return new EditorShellOptions(projectPath, scenePath, windowTicks, scriptedProbe, scriptedBuildProbe, scriptedBuildRunProbe, scriptedBuildCancelProbe, scriptedBuildSettingsProbe, scriptedMenuLayoutProbe, scriptedHierarchyProbe, scriptedDefaultWorkbenchProbe, buildOutputPath, captureFramePath, logDirectory);
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
