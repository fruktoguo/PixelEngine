namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// Editor automation v1 permission scope 常量。
/// </summary>
public static class AutomationScopes
{
    /// <summary>读取 Editor 权威状态。</summary>
    public const string EditorRead = "editor.read";

    /// <summary>控制 Editor 工作流。</summary>
    public const string EditorControl = "editor.control";

    /// <summary>修改项目与资产。</summary>
    public const string ProjectWrite = "project.write";

    /// <summary>修改 Preferences/Project/Player/Build Settings。</summary>
    public const string SettingsWrite = "settings.write";

    /// <summary>发起与取消 build。</summary>
    public const string ProcessBuild = "process.build";

    /// <summary>启动或终止外部进程。</summary>
    public const string ProcessLaunch = "process.launch";

    /// <summary>管理会话、制品与审计设置。</summary>
    public const string AutomationAdmin = "automation.admin";

    /// <summary>默认受信本地客户端可请求的完整 scope 集。</summary>
    public static string[] All =>
    [
        EditorRead,
        EditorControl,
        ProjectWrite,
        SettingsWrite,
        ProcessBuild,
        ProcessLaunch,
        AutomationAdmin,
    ];
}
