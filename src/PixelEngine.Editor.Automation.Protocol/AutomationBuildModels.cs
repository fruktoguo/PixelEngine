using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>公开 build job 状态。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationBuildState>))]
public enum AutomationBuildState
{
    /// <summary>build-player 仍在执行。</summary>
    Running,

    /// <summary>构建成功并通过产物审计。</summary>
    Succeeded,

    /// <summary>构建失败。</summary>
    Failed,

    /// <summary>构建已在真实取消边界结束。</summary>
    Cancelled,
}

/// <summary>公开 build pipeline 阶段。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationBuildPhase>))]
public enum AutomationBuildPhase
{
    /// <summary>尚未收到阶段信息。</summary>
    Unknown,

    /// <summary>native 依赖构建。</summary>
    Native,

    /// <summary>.NET publish。</summary>
    Publish,

    /// <summary>发布结果验证。</summary>
    Verify,

    /// <summary>确定性打包。</summary>
    Package,

    /// <summary>player-only 安全审计。</summary>
    Audit,

    /// <summary>流水线完成。</summary>
    Done,
}

/// <summary>公开 build 日志严重度。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationBuildLogLevel>))]
public enum AutomationBuildLogLevel
{
    /// <summary>信息。</summary>
    Info,

    /// <summary>警告。</summary>
    Warning,

    /// <summary>错误。</summary>
    Error,
}

/// <summary>Build Settings 面板的可观察状态。</summary>
public sealed record AutomationBuildPanelSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>新增日志时是否自动滚到底部。</summary>
    public required bool LogAutoScroll { get; init; }

    /// <summary>当前是否有 build job 运行。</summary>
    public required bool BuildRunning { get; init; }

    /// <summary>持久 settings 是否需要人工修复。</summary>
    public required bool RequiresRepair { get; init; }

    /// <summary>当前稳定校验诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>更新 Build Settings 面板的可观察状态。</summary>
public sealed record AutomationBuildPanelSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>新增日志时是否自动滚到底部。</summary>
    public required bool LogAutoScroll { get; init; }
}

/// <summary>build tool preflight 结果。</summary>
public sealed record AutomationBuildPreflightResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>全部必需工具是否可用。</summary>
    public required bool Ok { get; init; }

    /// <summary>是否找到 build-player 脚本。</summary>
    public required bool BuildPlayerAvailable { get; init; }

    /// <summary>dotnet SDK 版本。</summary>
    public required string DotnetVersion { get; init; }

    /// <summary>PowerShell/Bash 版本。</summary>
    public required string ShellVersion { get; init; }

    /// <summary>稳定诊断文本。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>启动 build job。</summary>
public sealed record AutomationBuildStartRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>成功后是否通过同一 player process manager 自动启动产物。</summary>
    public required bool LaunchOnSuccess { get; init; }
}

/// <summary>按稳定 build ID 请求。</summary>
public sealed record AutomationBuildRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>实例作用域 build ID。</summary>
    public required string BuildId { get; init; }
}

/// <summary>一个 build phase 的耗时。</summary>
public sealed record AutomationBuildPhaseTiming
{
    /// <summary>阶段。</summary>
    public required AutomationBuildPhase Phase { get; init; }

    /// <summary>耗时毫秒。</summary>
    public required double Milliseconds { get; init; }
}

/// <summary>build-player 的终态结果。</summary>
public sealed record AutomationBuildResult
{
    /// <summary>是否成功。</summary>
    public required bool Ok { get; init; }

    /// <summary>目标 RID。</summary>
    public required string Rid { get; init; }

    /// <summary>发行 channel。</summary>
    public required string Channel { get; init; }

    /// <summary>Development/Beta/Stable 发行通道。</summary>
    public required string ReleaseChannel { get; init; }

    /// <summary>player 窗口模式。</summary>
    public required string WindowMode { get; init; }

    /// <summary>配置。</summary>
    public required string Configuration { get; init; }

    /// <summary>版本。</summary>
    public required string Version { get; init; }

    /// <summary>informational version。</summary>
    public required string InformationalVersion { get; init; }

    /// <summary>package archive canonical path。</summary>
    public string? PackageArchivePath { get; init; }

    /// <summary>展开 package canonical directory。</summary>
    public string? PackageDirectory { get; init; }

    /// <summary>player canonical directory。</summary>
    public string? PlayerDirectory { get; init; }

    /// <summary>受信 launcher canonical path。</summary>
    public string? LauncherPath { get; init; }

    /// <summary>package SHA256。</summary>
    public string? Sha256 { get; init; }

    /// <summary>package 字节数。</summary>
    public required long SizeBytes { get; init; }

    /// <summary>阶段耗时。</summary>
    public AutomationBuildPhaseTiming[] PhaseTimings { get; init; } = [];

    /// <summary>构建警告。</summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>失败诊断。</summary>
    public string? Error { get; init; }

    /// <summary>build-player exit code。</summary>
    public required int ExitCode { get; init; }
}

/// <summary>稳定 build job 快照。</summary>
public sealed record AutomationBuildSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>实例作用域 build ID。</summary>
    public required string BuildId { get; init; }

    /// <summary>当前状态。</summary>
    public required AutomationBuildState State { get; init; }

    /// <summary>当前阶段。</summary>
    public required AutomationBuildPhase Phase { get; init; }

    /// <summary>0..1 进度。</summary>
    public required float Percent { get; init; }

    /// <summary>开始时间。</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>终态时间。</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>是否要求成功后启动。</summary>
    public required bool LaunchOnSuccess { get; init; }

    /// <summary>是否已向 build-player 进程树发出取消请求。</summary>
    public required bool CancellationRequested { get; init; }

    /// <summary>成功自动启动后产生的 player process ID。</summary>
    public string? PlayerProcessId { get; init; }

    /// <summary>包构建成功但自动启动失败时的有界诊断；可随后显式重试 player.launch。</summary>
    public string? PlayerLaunchError { get; init; }

    /// <summary>终态结果；运行中为空。</summary>
    public AutomationBuildResult? Result { get; init; }
}

/// <summary>build job 分页响应。</summary>
public sealed record AutomationBuildListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>build jobs。</summary>
    public AutomationBuildSnapshot[] Items { get; init; } = [];

    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>build log artifact 中的一条结构化记录。</summary>
public sealed record AutomationBuildLogEntry
{
    /// <summary>流水线阶段。</summary>
    public required AutomationBuildPhase Phase { get; init; }

    /// <summary>日志级别。</summary>
    public required AutomationBuildLogLevel Level { get; init; }

    /// <summary>0..1 进度。</summary>
    public required float Percent { get; init; }

    /// <summary>有界消息。</summary>
    public required string Message { get; init; }

    /// <summary>产生时间。</summary>
    public required DateTimeOffset TimestampUtc { get; init; }
}

/// <summary>公开 player process 状态。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationPlayerProcessState>))]
public enum AutomationPlayerProcessState
{
    /// <summary>进程仍在运行。</summary>
    Running,

    /// <summary>进程已退出。</summary>
    Exited,
}

/// <summary>从成功 build 启动玩家。</summary>
public sealed record AutomationPlayerLaunchRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>提供受信 launcher 的成功 build ID。</summary>
    public required string BuildId { get; init; }
}

/// <summary>按稳定 player process ID 请求。</summary>
public sealed record AutomationPlayerProcessRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>实例作用域 player process ID。</summary>
    public required string PlayerProcessId { get; init; }
}

/// <summary>终止玩家进程。</summary>
public sealed record AutomationPlayerTerminateRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>实例作用域 player process ID。</summary>
    public required string PlayerProcessId { get; init; }

    /// <summary>是否终止完整进程树。</summary>
    public required bool EntireProcessTree { get; init; }
}

/// <summary>稳定 player process 快照。</summary>
public sealed record AutomationPlayerProcessSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>实例作用域 player process ID。</summary>
    public required string PlayerProcessId { get; init; }

    /// <summary>来源 build ID。</summary>
    public required string BuildId { get; init; }

    /// <summary>操作系统 PID。</summary>
    public required int ProcessId { get; init; }

    /// <summary>用于防 PID reuse 的进程启动时间。</summary>
    public required DateTimeOffset ProcessStartUtc { get; init; }

    /// <summary>Editor 发起启动的时间。</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>当前状态。</summary>
    public required AutomationPlayerProcessState State { get; init; }

    /// <summary>是否已向该进程发出终止请求。</summary>
    public required bool TerminationRequested { get; init; }

    /// <summary>退出时间。</summary>
    public DateTimeOffset? ExitedAtUtc { get; init; }

    /// <summary>退出码；运行中为空。</summary>
    public int? ExitCode { get; init; }
}

/// <summary>player processes 分页响应。</summary>
public sealed record AutomationPlayerProcessListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>processes。</summary>
    public AutomationPlayerProcessSnapshot[] Items { get; init; } = [];

    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}
