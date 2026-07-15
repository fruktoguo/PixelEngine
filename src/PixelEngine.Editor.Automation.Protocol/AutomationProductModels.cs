using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>Project Window 当前稳定选择。</summary>
public sealed record AutomationProjectSelectionSnapshot
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>选中资产的 stable asset ID；选中文件夹或无选择时为空。</summary>
    public string? AssetId { get; init; }

    /// <summary>选中资产的当前 logical path。</summary>
    public string? AssetPath { get; init; }

    /// <summary>选中文件夹的稳定 ID；选中资产或无选择时为空。</summary>
    public string? FolderId { get; init; }

    /// <summary>选中文件夹的 logical path；空字符串表示 Project 根，null 表示未选文件夹。</summary>
    public string? FolderPath { get; init; }
}

/// <summary>设置 Project Window 稳定选择；assetId、folderPath、clear 必须且只能指定一个。</summary>
public sealed record AutomationProjectSelectionSetRequest
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable asset ID。</summary>
    public string? AssetId { get; init; }

    /// <summary>目标 folder logical path；空字符串表示 Project 根。</summary>
    public string? FolderPath { get; init; }

    /// <summary>是否清除 Project Window 资产/文件夹选择。</summary>
    public bool Clear { get; init; }
}

/// <summary>Project Window 资产排序模式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationProjectSortMode>))]
public enum AutomationProjectSortMode
{
    /// <summary>按 logical path 升序。</summary>
    PathAscending,
    /// <summary>先按类型、再按 logical path。</summary>
    KindThenPath,
    /// <summary>按最后修改时间倒序。</summary>
    LastModifiedDescending,
    /// <summary>按文件大小倒序。</summary>
    SizeDescending,
}

/// <summary>Project Window 右侧内容展示模式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationProjectViewMode>))]
public enum AutomationProjectViewMode
{
    /// <summary>可缩放缩略图网格。</summary>
    Grid,
    /// <summary>紧凑列表。</summary>
    List,
}

/// <summary>Project Window 搜索、过滤、排序与展示控件的完整快照。</summary>
public sealed record AutomationProjectWindowSnapshot
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>当前搜索文本；空字符串表示不过滤文本。</summary>
    public required string Search { get; init; }

    /// <summary>当前资产类型过滤；null 表示全部类型。</summary>
    public AutomationAssetKind? KindFilter { get; init; }

    /// <summary>当前排序模式。</summary>
    public required AutomationProjectSortMode SortMode { get; init; }

    /// <summary>当前网格或列表展示模式。</summary>
    public required AutomationProjectViewMode ViewMode { get; init; }

    /// <summary>当前网格缩略图边长。</summary>
    public required float ThumbnailSize { get; init; }

    /// <summary>允许的最小缩略图边长。</summary>
    public required float MinimumThumbnailSize { get; init; }

    /// <summary>允许的最大缩略图边长。</summary>
    public required float MaximumThumbnailSize { get; init; }

    /// <summary>当前浏览文件夹 rooted logical path；空字符串表示 Project 总根。</summary>
    public required string ActiveFolderPath { get; init; }
}

/// <summary>部分更新 Project Window 视图状态；至少指定一个目标字段。</summary>
public sealed record AutomationProjectWindowSetRequest
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标搜索文本；null 表示保持不变。</summary>
    public string? Search { get; init; }

    /// <summary>目标资产类型过滤；null 表示未指定或由 <see cref="ClearKindFilter"/> 清除。</summary>
    public AutomationAssetKind? KindFilter { get; init; }

    /// <summary>是否清除资产类型过滤；不得与 <see cref="KindFilter"/> 同时指定。</summary>
    public bool ClearKindFilter { get; init; }

    /// <summary>目标排序模式；null 表示保持不变。</summary>
    public AutomationProjectSortMode? SortMode { get; init; }

    /// <summary>目标展示模式；null 表示保持不变。</summary>
    public AutomationProjectViewMode? ViewMode { get; init; }

    /// <summary>目标缩略图边长；null 表示保持不变。</summary>
    public float? ThumbnailSize { get; init; }
}

/// <summary>使用外部编辑器打开脚本资产的请求。</summary>
public sealed record AutomationScriptAssetOpenRequest
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable asset ID。</summary>
    public required string AssetId { get; init; }

    /// <summary>一基行号。</summary>
    public int Line { get; init; } = 1;

    /// <summary>一基列号。</summary>
    public int Column { get; init; } = 1;
}

/// <summary>Project 资产动作结果。</summary>
public sealed record AutomationAssetActionResult
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>动作是否被底层服务接受。</summary>
    public bool Succeeded { get; init; }

    /// <summary>面向脚本与 CI 的确定性诊断。</summary>
    public required string Diagnostic { get; init; }

    /// <summary>动作绑定的当前资产快照。</summary>
    public required AutomationAssetInfo Asset { get; init; }
}

/// <summary>Editor 支持的外部 C# IDE 类型。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationCodeEditorKind>))]
public enum AutomationCodeEditorKind
{
    /// <summary>Visual Studio Code。</summary>
    VsCode,
    /// <summary>Visual Studio。</summary>
    VisualStudio,
    /// <summary>JetBrains Rider。</summary>
    Rider,
    /// <summary>操作系统默认程序。</summary>
    SystemDefault,
    /// <summary>用户提供的自定义命令。</summary>
    Custom,
}

/// <summary>打开当前工程完整 C# workspace/solution 的结果。</summary>
public sealed record AutomationCodeProjectOpenResult
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>IDE 进程是否成功启动。</summary>
    public required bool Succeeded { get; init; }

    /// <summary>解析出的 IDE 类型；准备失败且尚未分类时为空。</summary>
    public AutomationCodeEditorKind? EditorKind { get; init; }

    /// <summary>被解析或生成的 C# project canonical path。</summary>
    public string? ProjectPath { get; init; }

    /// <summary>被解析或生成的 solution canonical path。</summary>
    public string? SolutionPath { get; init; }

    /// <summary>实际传给 IDE 的 workspace、project root 或 solution canonical path。</summary>
    public string? OpenedTarget { get; init; }

    /// <summary>是否使用 Editor-owned project 文件。</summary>
    public required bool ProjectGenerated { get; init; }

    /// <summary>是否使用 Editor-owned solution 文件。</summary>
    public required bool SolutionGenerated { get; init; }

    /// <summary>本次是否真实创建或更新了 Editor-owned project/solution 文件。</summary>
    public required bool FilesChanged { get; init; }

    /// <summary>稳定、可直接显示的打开或失败诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>Project Window 公开资产类型。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationAssetKind>))]
public enum AutomationAssetKind
{
    /// <summary>逻辑文件夹。</summary>
    Folder,
    /// <summary>材质或反应定义。</summary>
    Material,
    /// <summary>纹理。</summary>
    Texture,
    /// <summary>音频。</summary>
    Audio,
    /// <summary>Scene。</summary>
    Scene,
    /// <summary>Prefab。</summary>
    Prefab,
    /// <summary>C# 脚本。</summary>
    Script,
    /// <summary>Web UI screen。</summary>
    UiScreen,
    /// <summary>普通 JSON。</summary>
    Json,
    /// <summary>其它文件。</summary>
    Other,
}

/// <summary>资产详细预览的主要内容形态。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationAssetPreviewKind>))]
public enum AutomationAssetPreviewKind
{
    /// <summary>只有元数据。</summary>
    Metadata,
    /// <summary>图片。</summary>
    Image,
    /// <summary>音频。</summary>
    Audio,
    /// <summary>有界文本。</summary>
    Text,
}

/// <summary>当前工程与 Asset Database 摘要。</summary>
public sealed record AutomationProjectSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程稳定 ID。</summary>
    public required string ProjectId { get; init; }
    /// <summary>工程显示名。</summary>
    public required string Name { get; init; }
    /// <summary>工程 canonical root。</summary>
    public required string RootPath { get; init; }
    /// <summary>当前 Asset Database 实际使用的 Content canonical root。</summary>
    public required string ContentRootPath { get; init; }
    /// <summary>当前 script runtime 实际使用的 ScriptSource canonical root。</summary>
    public required string ScriptSourcePath { get; init; }
    /// <summary>Project Settings 已配置的 Content canonical root。</summary>
    public required string ConfiguredContentRootPath { get; init; }
    /// <summary>Project Settings 已配置的 ScriptSource canonical root。</summary>
    public required string ConfiguredScriptSourcePath { get; init; }
    /// <summary>配置根与当前 session 根是否不同。</summary>
    public required bool RequiresReload { get; init; }
    /// <summary>当前 Scene logical path。</summary>
    public required string CurrentScenePath { get; init; }
    /// <summary>当前缓存内资产数量。</summary>
    public required int AssetCount { get; init; }
    /// <summary>当前缓存内文件夹数量。</summary>
    public required int FolderCount { get; init; }
    /// <summary>Asset Database 非致命诊断。</summary>
    public required string AssetDatabaseDiagnostic { get; init; }
}

/// <summary>一个稳定 Project 资产。</summary>
public sealed record AutomationAssetInfo
{
    /// <summary>工程级 stable asset id。</summary>
    public required string AssetId { get; init; }
    /// <summary>Content/ 或 ScriptSource/ rooted logical path。</summary>
    public required string Path { get; init; }
    /// <summary>资产类型。</summary>
    public required AutomationAssetKind Kind { get; init; }
    /// <summary>文件字节数。</summary>
    public required long SizeBytes { get; init; }
    /// <summary>最后修改 UTC。</summary>
    public required DateTimeOffset LastModifiedUtc { get; init; }
    /// <summary>显示名。</summary>
    public required string DisplayName { get; init; }
    /// <summary>只读预览摘要。</summary>
    public required string PreviewSummary { get; init; }
    /// <summary>面向用户的类型标签。</summary>
    public required string TypeLabel { get; init; }
    /// <summary>资产用途。</summary>
    public required string Purpose { get; init; }
    /// <summary>稳定 badge 名称。</summary>
    public string[] Badges { get; init; } = [];
}

/// <summary>资产分页响应。</summary>
public sealed record AutomationAssetListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>本页资产。</summary>
    public required AutomationAssetInfo[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>一个稳定 Project 文件夹。</summary>
public sealed record AutomationFolderInfo
{
    /// <summary>当前工程内稳定、path-scoped 文件夹 ID。</summary>
    public required string FolderId { get; init; }
    /// <summary>Rooted logical path；空字符串表示 Project 根。</summary>
    public required string Path { get; init; }
    /// <summary>显示名。</summary>
    public required string DisplayName { get; init; }
    /// <summary>该文件夹及其后代资产数量。</summary>
    public required int AssetCount { get; init; }
}

/// <summary>文件夹分页响应。</summary>
public sealed record AutomationFolderListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>本页文件夹。</summary>
    public required AutomationFolderInfo[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>按稳定 ID 寻址资产。</summary>
public sealed record AutomationAssetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程级 stable asset id。</summary>
    public required string AssetId { get; init; }
}

/// <summary>创建资产或文件夹。</summary>
public sealed record AutomationAssetCreateRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 rooted logical path。</summary>
    public required string Path { get; init; }
    /// <summary>目标类型。</summary>
    public required AutomationAssetKind Kind { get; init; }
}

/// <summary>从显式获准的 import root 导入外部文件。</summary>
public sealed record AutomationAssetImportRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>外部源 canonical path。</summary>
    public required string SourcePath { get; init; }
    /// <summary>目标 rooted logical path。</summary>
    public required string TargetPath { get; init; }
    /// <summary>目标资产类型。</summary>
    public required AutomationAssetKind Kind { get; init; }
}

/// <summary>从显式获准 import root 替换已有资产的字节内容。</summary>
public sealed record AutomationAssetReplaceRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>要保留的工程级 stable asset id。</summary>
    public required string AssetId { get; init; }
    /// <summary>外部源 canonical path。</summary>
    public required string SourcePath { get; init; }
}

/// <summary>移动或重命名稳定资产。</summary>
public sealed record AutomationAssetMoveRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程级 stable asset id。</summary>
    public required string AssetId { get; init; }
    /// <summary>目标同根 rooted logical path。</summary>
    public required string NewPath { get; init; }
}

/// <summary>删除稳定资产。</summary>
public sealed record AutomationAssetDeleteRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程级 stable asset id。</summary>
    public required string AssetId { get; init; }
    /// <summary>显式确认安全预检后的删除。</summary>
    public bool Confirmed { get; init; }
}

/// <summary>移动或重命名文件夹。</summary>
public sealed record AutomationFolderMoveRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>当前 rooted logical path。</summary>
    public required string Path { get; init; }
    /// <summary>目标同根 rooted logical path。</summary>
    public required string NewPath { get; init; }
}

/// <summary>递归删除文件夹。</summary>
public sealed record AutomationFolderDeleteRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 rooted logical path。</summary>
    public required string Path { get; init; }
    /// <summary>显式确认安全预检后的删除。</summary>
    public bool Confirmed { get; init; }
}

/// <summary>资产或文件夹变更结果。</summary>
public sealed record AutomationAssetMutationResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>操作是否已执行。</summary>
    public required bool Succeeded { get; init; }
    /// <summary>是否需要带 confirmed=true 重试。</summary>
    public required bool RequiresConfirmation { get; init; }
    /// <summary>稳定诊断。</summary>
    public required string Diagnostic { get; init; }
    /// <summary>创建、导入或移动后的资产。</summary>
    public AutomationAssetInfo? Asset { get; init; }
}

/// <summary>Project Window 完整刷新结果。</summary>
public sealed record AutomationAssetRefreshResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>刷新后的资产数。</summary>
    public required int AssetCount { get; init; }
    /// <summary>刷新后的逻辑文件夹数。</summary>
    public required int FolderCount { get; init; }
    /// <summary>catalog、manifest、引用、project 或 active scene 是否实际变化。</summary>
    public required bool StateChanged { get; init; }
    /// <summary>刷新或安全恢复诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>资产预览的一项元数据。</summary>
public sealed record AutomationAssetPreviewProperty
{
    /// <summary>短标签。</summary>
    public required string Label { get; init; }
    /// <summary>值。</summary>
    public required string Value { get; init; }
}

/// <summary>资产详细预览；大内容只通过 artifact 返回。</summary>
public sealed record AutomationAssetPreviewResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标资产。</summary>
    public required AutomationAssetInfo Asset { get; init; }
    /// <summary>预览标题。</summary>
    public required string Title { get; init; }
    /// <summary>内容类型。</summary>
    public required AutomationAssetPreviewKind ContentKind { get; init; }
    /// <summary>类型化摘要。</summary>
    public required string Summary { get; init; }
    /// <summary>只读元数据。</summary>
    public required AutomationAssetPreviewProperty[] Properties { get; init; }
    /// <summary>非致命诊断。</summary>
    public string? Diagnostic { get; init; }
    /// <summary>文本、图片或音频内容制品；Metadata 预览可为空。</summary>
    public AutomationArtifactReference? Artifact { get; init; }
}

/// <summary>Console 分类。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationConsoleCategory>))]
public enum AutomationConsoleCategory
{
    /// <summary>通用。</summary>
    General,
    /// <summary>构建。</summary>
    Build,
    /// <summary>脚本。</summary>
    Script,
    /// <summary>UI。</summary>
    Ui,
    /// <summary>资产。</summary>
    Asset,
    /// <summary>工程。</summary>
    Project,
    /// <summary>运行时。</summary>
    Runtime,
}

/// <summary>Console 严重度。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationConsoleSeverity>))]
public enum AutomationConsoleSeverity
{
    /// <summary>跟踪。</summary>
    Trace,
    /// <summary>信息。</summary>
    Info,
    /// <summary>警告。</summary>
    Warning,
    /// <summary>错误。</summary>
    Error,
}

/// <summary>一条有稳定 sequence 的 Console 日志。</summary>
public sealed record AutomationConsoleEntry
{
    /// <summary>稳定 entry ID。</summary>
    public required string EntryId { get; init; }
    /// <summary>原始单调 sequence。</summary>
    public required long Sequence { get; init; }
    /// <summary>UTC 时间。</summary>
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>分类。</summary>
    public required AutomationConsoleCategory Category { get; init; }
    /// <summary>严重度。</summary>
    public required AutomationConsoleSeverity Severity { get; init; }
    /// <summary>来源。</summary>
    public required string Source { get; init; }
    /// <summary>主文本。</summary>
    public required string Text { get; init; }
    /// <summary>详细信息。</summary>
    public required string Details { get; init; }
    /// <summary>可选源码路径。</summary>
    public string? FilePath { get; init; }
    /// <summary>一基行号；未知为 0。</summary>
    public required int Line { get; init; }
    /// <summary>一基列号；未知为 0。</summary>
    public required int Column { get; init; }
    /// <summary>运行时帧号；未知为 -1。</summary>
    public required long FrameIndex { get; init; }
}

/// <summary>Console 日志分页响应。</summary>
public sealed record AutomationConsoleListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>本页日志。</summary>
    public required AutomationConsoleEntry[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>Console 当前严重度计数。</summary>
public sealed record AutomationConsoleCounts
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>Trace/Info 数。</summary>
    public required int Logs { get; init; }
    /// <summary>Warning 数。</summary>
    public required int Warnings { get; init; }
    /// <summary>Error 数。</summary>
    public required int Errors { get; init; }
    /// <summary>最新 sequence；空缓冲为 -1。</summary>
    public required long LastSequence { get; init; }
}

/// <summary>Console 工具栏过滤、滚动与 Play 联动的共享语义状态。</summary>
public sealed record AutomationConsoleOptions
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>当前文本过滤；空字符串表示不过滤。</summary>
    public required string Search { get; init; }
    /// <summary>是否按内容键折叠重复日志。</summary>
    public required bool Collapse { get; init; }
    /// <summary>进入 Play 时是否清空日志。</summary>
    public required bool ClearOnPlay { get; init; }
    /// <summary>Play 中出现 runtime error 时是否暂停。</summary>
    public required bool ErrorPause { get; init; }
    /// <summary>是否显示 Trace/Info。</summary>
    public required bool ShowLogs { get; init; }
    /// <summary>是否显示 Warning。</summary>
    public required bool ShowWarnings { get; init; }
    /// <summary>是否显示 Error。</summary>
    public required bool ShowErrors { get; init; }
    /// <summary>新日志到达且当前位于底部时是否自动滚动。</summary>
    public required bool AutoScroll { get; init; }
}

/// <summary>按稳定 ID 定位一条 Console entry。</summary>
public sealed record AutomationConsoleEntryRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标稳定 entry ID。</summary>
    public required string EntryId { get; init; }
}

/// <summary>Console 当前选择与详情投影。</summary>
public sealed record AutomationConsoleSelectionSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>当前可见代表 entry ID；无选择或已淘汰时为 null。</summary>
    public string? EntryId { get; init; }
    /// <summary>当前可见代表 entry 的完整详情。</summary>
    public AutomationConsoleEntry? Entry { get; init; }
}

/// <summary>选择或清除 Console entry。</summary>
public sealed record AutomationConsoleSelectionSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标稳定 entry ID。</summary>
    public string? EntryId { get; init; }
    /// <summary>是否清除选择；与 entryId 必须且只能指定一个。</summary>
    public bool Clear { get; init; }
}

/// <summary>Console Copy 语义结果。</summary>
public sealed record AutomationConsoleCopyResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>源 entry。</summary>
    public required AutomationConsoleEntry Entry { get; init; }
    /// <summary>与 UI 写入剪贴板完全相同的文本。</summary>
    public required string Text { get; init; }
}

/// <summary>Console Open Source 语义结果。</summary>
public sealed record AutomationConsoleOpenSourceResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>源 entry。</summary>
    public required AutomationConsoleEntry Entry { get; init; }
    /// <summary>底层脚本编辑器是否接受启动请求。</summary>
    public required bool Succeeded { get; init; }
    /// <summary>确定性诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>Play 使用的 authoring 来源。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationPlaySource>))]
public enum AutomationPlaySource
{
    /// <summary>直接使用当前已提交 authoring state。</summary>
    CurrentState,
    /// <summary>进入 Play 前保存临时完整快照，Stop 时恢复。</summary>
    TemporarySnapshot,
}

/// <summary>当前 Play session 快照。</summary>
public sealed record AutomationPlaySnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>Play-session scoped 稳定 ID；Edit 时为空。</summary>
    public string? PlaySessionId { get; init; }
    /// <summary>当前 Editor mode。</summary>
    public required AutomationEditorMode Mode { get; init; }
    /// <summary>Play 来源；Edit 时为空。</summary>
    public AutomationPlaySource? Source { get; init; }
    /// <summary>是否持有 Stop 时要恢复的临时快照。</summary>
    public required bool TemporarySnapshotActive { get; init; }
    /// <summary>当前 Engine frame index。</summary>
    public required long FrameIndex { get; init; }
    /// <summary>最近状态诊断。</summary>
    public required string Status { get; init; }
}

/// <summary>进入 Play 请求。</summary>
public sealed record AutomationPlayEnterRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>Play 来源。</summary>
    public AutomationPlaySource Source { get; init; } = AutomationPlaySource.TemporarySnapshot;
}

/// <summary>Play/Pause/Step/Stop 结果。</summary>
public sealed record AutomationPlayCommandResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>命令是否被真实执行或已处于目标状态。</summary>
    public required bool Succeeded { get; init; }
    /// <summary>稳定诊断。</summary>
    public required string Diagnostic { get; init; }
    /// <summary>命令后的 session 快照。</summary>
    public required AutomationPlaySnapshot Snapshot { get; init; }
}

/// <summary>Runtime entity Transform。</summary>
public sealed record AutomationRuntimeTransform
{
    /// <summary>X。</summary>
    public required float X { get; init; }
    /// <summary>Y。</summary>
    public required float Y { get; init; }
    /// <summary>弧度旋转。</summary>
    public required float RotationRadians { get; init; }
    /// <summary>X 缩放。</summary>
    public required float ScaleX { get; init; }
    /// <summary>Y 缩放。</summary>
    public required float ScaleY { get; init; }
}

/// <summary>Play-session scoped runtime component。</summary>
public sealed record AutomationRuntimeComponent
{
    /// <summary>组件稳定 ID。</summary>
    public required string ComponentId { get; init; }
    /// <summary>Behaviour type 全名。</summary>
    public required string TypeName { get; init; }
    /// <summary>是否 Enabled。</summary>
    public required bool Enabled { get; init; }
    /// <summary>是否因异常 faulted。</summary>
    public required bool Faulted { get; init; }
    /// <summary>运行时 Inspector 字段。</summary>
    public AutomationInspectorField[] Fields { get; init; } = [];
}

/// <summary>Play-session scoped runtime entity。</summary>
public sealed record AutomationRuntimeEntity
{
    /// <summary>含 play session 的稳定 entity ID。</summary>
    public required string EntityId { get; init; }
    /// <summary>脚本 Scene 内 numeric ID。</summary>
    public required long NumericId { get; init; }
    /// <summary>运行时句柄。</summary>
    public required string Handle { get; init; }
    /// <summary>可选 Transform。</summary>
    public AutomationRuntimeTransform? Transform { get; init; }
    /// <summary>组件。</summary>
    public required AutomationRuntimeComponent[] Components { get; init; }
}

/// <summary>Runtime entity 分页响应。</summary>
public sealed record AutomationRuntimeEntityListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>所属 Play session。</summary>
    public required string PlaySessionId { get; init; }
    /// <summary>本页 entities。</summary>
    public required AutomationRuntimeEntity[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>读取 runtime entity。</summary>
public sealed record AutomationRuntimeEntityRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>含 play session 的稳定 entity ID。</summary>
    public required string EntityId { get; init; }
}

/// <summary>Play-session scoped runtime rigid body 的只读物理快照。</summary>
public sealed record AutomationRuntimeBody
{
    /// <summary>包含 Play session 与 body key 的稳定 ID。</summary>
    public required string BodyId { get; init; }
    /// <summary>当前 PhysicsSystem 内的 body key。</summary>
    public required int BodyKey { get; init; }
    /// <summary>世界像素 X。</summary>
    public required float PositionX { get; init; }
    /// <summary>世界像素 Y。</summary>
    public required float PositionY { get; init; }
    /// <summary>旋转正弦。</summary>
    public required float RotationSin { get; init; }
    /// <summary>旋转余弦。</summary>
    public required float RotationCos { get; init; }
    /// <summary>像素/秒线速度 X。</summary>
    public required float LinearVelocityX { get; init; }
    /// <summary>像素/秒线速度 Y。</summary>
    public required float LinearVelocityY { get; init; }
    /// <summary>弧度/秒角速度。</summary>
    public required float AngularVelocityRadiansPerSecond { get; init; }
    /// <summary>不可变 body-local mask 宽度。</summary>
    public required int MaskWidth { get; init; }
    /// <summary>不可变 body-local mask 高度。</summary>
    public required int MaskHeight { get; init; }
    /// <summary>不可变 body-local mask 实心像素数。</summary>
    public required int SolidPixelCount { get; init; }
}

/// <summary>Runtime rigid body 分页响应。</summary>
public sealed record AutomationRuntimeBodyListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>所属 Play session。</summary>
    public required string PlaySessionId { get; init; }
    /// <summary>本页 rigid bodies。</summary>
    public required AutomationRuntimeBody[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>按 Play-session scoped stable ID 读取 runtime rigid body。</summary>
public sealed record AutomationRuntimeBodyRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>包含 Play session 与 body key 的稳定 ID。</summary>
    public required string BodyId { get; init; }
}

/// <summary>临时修改 Play-session scoped runtime entity Transform。</summary>
public sealed record AutomationRuntimeTransformSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>含 play session 的稳定 entity ID。</summary>
    public required string EntityId { get; init; }
    /// <summary>X。</summary>
    public required float X { get; init; }
    /// <summary>Y。</summary>
    public required float Y { get; init; }
    /// <summary>弧度旋转。</summary>
    public required float RotationRadians { get; init; }
    /// <summary>X 缩放。</summary>
    public required float ScaleX { get; init; }
    /// <summary>Y 缩放。</summary>
    public required float ScaleY { get; init; }
}

/// <summary>临时修改 Play-session scoped runtime Behaviour Inspector 字段。</summary>
public sealed record AutomationRuntimeComponentFieldSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>含 play session 的稳定 entity ID。</summary>
    public required string EntityId { get; init; }
    /// <summary>由 entity ID 与 Behaviour type 派生的稳定 component ID。</summary>
    public required string ComponentId { get; init; }
    /// <summary>稳定字段或 public property 名。</summary>
    public required string FieldName { get; init; }
    /// <summary>与 Inspector snapshot 相同格式的序列化值。</summary>
    public required string Value { get; init; }
}

/// <summary>Engine simulation 控制快照。</summary>
public sealed record AutomationRuntimeSimulationSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>当前 Play session；Edit 时为空。</summary>
    public string? PlaySessionId { get; init; }
    /// <summary>Engine 当前是否推进 simulation。</summary>
    public required bool IsPlaying { get; init; }
    /// <summary>请求的 simulation 频率。</summary>
    public required double SimulationHz { get; init; }
    /// <summary>当前渲染帧。</summary>
    public required long FrameIndex { get; init; }
    /// <summary>当前 simulation tick。</summary>
    public required long SimulationTickIndex { get; init; }
    /// <summary>最近一帧是否执行 simulation。</summary>
    public required bool RanSimulationThisFrame { get; init; }
}

/// <summary>设置 Simulation 面板支持的请求频率。</summary>
public sealed record AutomationRuntimeSimulationSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 simulation 频率；v1 支持 30 或 60 Hz。</summary>
    public required double SimulationHz { get; init; }
}

/// <summary>Runtime world 与运行诊断摘要。</summary>
public sealed record AutomationRuntimeWorldSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>所属 Play session。</summary>
    public required string PlaySessionId { get; init; }
    /// <summary>当前 frame index。</summary>
    public required long FrameIndex { get; init; }
    /// <summary>脚本 entity 数。</summary>
    public required int EntityCount { get; init; }
    /// <summary>帧率。</summary>
    public required double FramesPerSecond { get; init; }
    /// <summary>上一帧墙钟毫秒。</summary>
    public required double FrameMilliseconds { get; init; }
    /// <summary>p99 帧毫秒。</summary>
    public required double P99FrameMilliseconds { get; init; }
    /// <summary>1% low FPS。</summary>
    public required double OnePercentLowFps { get; init; }
    /// <summary>当前 sim Hz。</summary>
    public required int SimulationHz { get; init; }
    /// <summary>活跃 chunk 数。</summary>
    public required int ActiveChunks { get; init; }
    /// <summary>活跃粒子数。</summary>
    public required int ActiveParticles { get; init; }
    /// <summary>活跃刚体数。</summary>
    public required int ActiveBodies { get; init; }
    /// <summary>活跃光源数。</summary>
    public required int ActiveLights { get; init; }
}

/// <summary>Profiler 的一个稳定 phase 样本。</summary>
public sealed record AutomationProfilerSample
{
    /// <summary>稳定 phase 名。</summary>
    public required string Phase { get; init; }
    /// <summary>上一帧毫秒。</summary>
    public required double Milliseconds { get; init; }
    /// <summary>近 60 帧平均毫秒；sub-phase 为 null。</summary>
    public double? Average60Milliseconds { get; init; }
}

/// <summary>Profiler 面板滚动窗口中的一帧曲线数据。</summary>
public sealed record AutomationProfilerHistorySample
{
    /// <summary>Engine frame index。</summary>
    public required long FrameIndex { get; init; }
    /// <summary>整帧墙钟毫秒。</summary>
    public required double FrameMilliseconds { get; init; }
    /// <summary>CA A-D 合计毫秒。</summary>
    public required double CaMilliseconds { get; init; }
    /// <summary>Physics 与 shape rebuild 合计毫秒。</summary>
    public required double PhysicsMilliseconds { get; init; }
    /// <summary>Render 与 upload 合计毫秒。</summary>
    public required double RenderMilliseconds { get; init; }
    /// <summary>Audio 毫秒。</summary>
    public required double AudioMilliseconds { get; init; }
    /// <summary>CPU busy 毫秒。</summary>
    public required double CpuMilliseconds { get; init; }
    /// <summary>GPU timer 毫秒；不可用时为 0。</summary>
    public required double GpuMilliseconds { get; init; }
    /// <summary>present/vsync 等等待毫秒。</summary>
    public required double WaitMilliseconds { get; init; }
    /// <summary>有效帧毫秒。</summary>
    public required double EffectiveMilliseconds { get; init; }
    /// <summary>随负载变化的工作毫秒。</summary>
    public required double VariableWorkMilliseconds { get; init; }
    /// <summary>固定开销毫秒。</summary>
    public required double FixedOverheadMilliseconds { get; init; }
    /// <summary>活跃 chunk 数。</summary>
    public required long ActiveChunks { get; init; }
    /// <summary>活跃 cell 数。</summary>
    public required long ActiveCells { get; init; }
    /// <summary>自由粒子数。</summary>
    public required long FreeParticles { get; init; }
    /// <summary>刚体数。</summary>
    public required long RigidBodies { get; init; }
    /// <summary>cell/刚体销毁与创建事件合计。</summary>
    public required long DestructionEvents { get; init; }
    /// <summary>自定义计数器原始值。</summary>
    public required long CustomMetricValue { get; init; }
    /// <summary>当前 simulation Hz。</summary>
    public required double SimulationHz { get; init; }
}

/// <summary>Profiler 面板一个滚动序列的统计。</summary>
public sealed record AutomationProfilerStatistics
{
    /// <summary>有效样本数。</summary>
    public required int SampleCount { get; init; }
    /// <summary>平均毫秒。</summary>
    public required double AverageMilliseconds { get; init; }
    /// <summary>P50 毫秒。</summary>
    public required double P50Milliseconds { get; init; }
    /// <summary>P95 毫秒。</summary>
    public required double P95Milliseconds { get; init; }
    /// <summary>P99 毫秒。</summary>
    public required double P99Milliseconds { get; init; }
    /// <summary>最大毫秒。</summary>
    public required double MaxMilliseconds { get; init; }
    /// <summary>是否达到稳态样本数。</summary>
    public required bool IsSteady { get; init; }
    /// <summary>最新样本是否是 spike。</summary>
    public required bool IsSpike { get; init; }
}

/// <summary>上一帧 Profiler 快照。</summary>
public sealed record AutomationProfilerSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>当前 Engine frame index。</summary>
    public required long FrameIndex { get; init; }
    /// <summary>上一帧墙钟毫秒。</summary>
    public required double WallMilliseconds { get; init; }
    /// <summary>CPU 有效工作毫秒。</summary>
    public required double CpuWorkMilliseconds { get; init; }
    /// <summary>GPU timer 可用时的 GPU 工作毫秒。</summary>
    public required double GpuWorkMilliseconds { get; init; }
    /// <summary>GPU timer 是否真实可用。</summary>
    public required bool GpuTimerAvailable { get; init; }
    /// <summary>present/vsync 等待在内的等待毫秒。</summary>
    public required double WaitMilliseconds { get; init; }
    /// <summary>扣除等待后的有效帧毫秒。</summary>
    public required double EffectiveFrameMilliseconds { get; init; }
    /// <summary>按有效帧耗时计算的 FPS。</summary>
    public required double EffectiveFramesPerSecond { get; init; }
    /// <summary>CPU/GPU/Wait/Idle bound 分类。</summary>
    public required string BoundType { get; init; }
    /// <summary>当前 VSync 状态。</summary>
    public required bool VSyncEnabled { get; init; }
    /// <summary>当前后端是否允许实时切换 VSync。</summary>
    public required bool CanToggleVSync { get; init; }
    /// <summary>当前模拟时间倍率。</summary>
    public required double TimeScale { get; init; }
    /// <summary>当前过载降级等级。</summary>
    public required int DegradationLevel { get; init; }
    /// <summary>当前过载降级稳定名称。</summary>
    public required string DegradationName { get; init; }
    /// <summary>连续超预算帧数。</summary>
    public required int ConsecutiveOverBudgetFrames { get; init; }
    /// <summary>主 phases。</summary>
    public required AutomationProfilerSample[] MainPhases { get; init; }
    /// <summary>细分 phases。</summary>
    public required AutomationProfilerSample[] SubPhases { get; init; }
    /// <summary>面板环形历史容量。</summary>
    public required int HistoryCapacity { get; init; }
    /// <summary>面板自创建以来捕获的去重 frame 数。</summary>
    public required int CapturedSampleCount { get; init; }
    /// <summary>按 frame index 递增排列的当前滚动样本。</summary>
    public required AutomationProfilerHistorySample[] History { get; init; }
    /// <summary>整帧滚动统计。</summary>
    public required AutomationProfilerStatistics FrameStatistics { get; init; }
    /// <summary>CPU busy 滚动统计。</summary>
    public required AutomationProfilerStatistics CpuStatistics { get; init; }
    /// <summary>GPU timer 滚动统计。</summary>
    public required AutomationProfilerStatistics GpuStatistics { get; init; }
    /// <summary>等待时间滚动统计。</summary>
    public required AutomationProfilerStatistics WaitStatistics { get; init; }
    /// <summary>有效帧滚动统计。</summary>
    public required AutomationProfilerStatistics EffectiveStatistics { get; init; }
    /// <summary>随负载工作滚动统计。</summary>
    public required AutomationProfilerStatistics VariableWorkStatistics { get; init; }
    /// <summary>固定开销滚动统计。</summary>
    public required AutomationProfilerStatistics FixedOverheadStatistics { get; init; }
}

/// <summary>通过 Profiler 的真实 present 控制器设置 VSync。</summary>
public sealed record AutomationProfilerVSyncSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 VSync 状态。</summary>
    public required bool Enabled { get; init; }
}

/// <summary>Debug overlay 当前 flags。</summary>
public sealed record AutomationDebugOverlaySnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>所有可用稳定 flag 名。</summary>
    public required string[] AvailableFlags { get; init; }
    /// <summary>已启用稳定 flag 名。</summary>
    public required string[] EnabledFlags { get; init; }
}

/// <summary>设置一个 Debug overlay flag。</summary>
public sealed record AutomationDebugOverlaySetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>稳定 flag 名。</summary>
    public required string Flag { get; init; }
    /// <summary>目标状态。</summary>
    public required bool Enabled { get; init; }
}

/// <summary>公开 UI backend。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationUiBackend>))]
public enum AutomationUiBackend
{
    /// <summary>纯托管回退。</summary>
    ManagedFallback,
    /// <summary>RmlUi HTML/CSS 子集。</summary>
    RmlUi,
    /// <summary>Ultralight 可选后端。</summary>
    Ultralight,
}

/// <summary>独立 Player 窗口模式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationPlayerWindowMode>))]
public enum AutomationPlayerWindowMode
{
    /// <summary>普通窗口。</summary>
    Windowed,
    /// <summary>保留系统 chrome 的最大化窗口。</summary>
    MaximizedWindow,
    /// <summary>无边框全屏窗口。</summary>
    BorderlessFullscreen,
}

/// <summary>Player 发行通道。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationPlayerReleaseChannel>))]
public enum AutomationPlayerReleaseChannel
{
    /// <summary>开发通道。</summary>
    Development,
    /// <summary>生产通道。</summary>
    Production,
}

/// <summary>build-player 编译通道。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationBuildChannel>))]
public enum AutomationBuildChannel
{
    /// <summary>ReadyToRun。</summary>
    R2R,
    /// <summary>NativeAOT。</summary>
    Aot,
}

/// <summary>Build profile Scene 来源。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationSceneSourceKind>))]
public enum AutomationSceneSourceKind
{
    /// <summary>空场景。</summary>
    Empty,
    /// <summary>存档目录。</summary>
    SaveDirectory,
    /// <summary>.scene 文件。</summary>
    SceneFile,
    /// <summary>宿主程序化场景。</summary>
    Procedural,
}

/// <summary>菜单、全局调度器与 Preferences 共用的一条快捷键定义。</summary>
public sealed record AutomationShortcutInfo
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>能力矩阵使用的稳定 UI command ID。</summary>
    public required string UiCommandId { get; init; }

    /// <summary>面向用户的动作名。</summary>
    public required string Action { get; init; }

    /// <summary>不含修饰键的稳定按键名。</summary>
    public required string Key { get; init; }

    /// <summary>是否包含 Control 修饰键。</summary>
    public required bool Control { get; init; }

    /// <summary>是否包含 Shift 修饰键。</summary>
    public required bool Shift { get; init; }

    /// <summary>是否包含 Alt 修饰键。</summary>
    public required bool Alt { get; init; }

    /// <summary>是否包含平台 Super 修饰键。</summary>
    public required bool Super { get; init; }

    /// <summary>与菜单和 Preferences 相同的显示文本。</summary>
    public required string DisplayText { get; init; }
}

/// <summary>Editor 快捷键目录分页结果。</summary>
public sealed record AutomationShortcutListResponse
{
    /// <summary>协议 DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>本页快捷键。</summary>
    public required AutomationShortcutInfo[] Items { get; init; }

    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>用户级 Editor Preferences 完整值。</summary>
public sealed record AutomationEditorPreferences
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>UI 缩放。</summary>
    public required float UiScale { get; init; }
    /// <summary>退出时保存布局。</summary>
    public required bool SaveLayoutOnExit { get; init; }
    /// <summary>启动时重开上次工程。</summary>
    public required bool ReopenLastProject { get; init; }
    /// <summary>重开工程时恢复上次 Scene。</summary>
    public required bool RestoreLastScene { get; init; }
    /// <summary>外部脚本编辑器 sentinel 或命令模板。</summary>
    public required string ExternalScriptEditor { get; init; }
    /// <summary>BCP-47 UI locale。</summary>
    public required string Language { get; init; }
}

/// <summary>Project Settings 完整值。</summary>
public sealed record AutomationProjectSettingsSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程显示名。</summary>
    public required string Name { get; init; }
    /// <summary>工程内 Content root。</summary>
    public required string ContentRoot { get; init; }
    /// <summary>工程内 ScriptSource root。</summary>
    public required string ScriptSourceDir { get; init; }
    /// <summary>默认启动 Scene。</summary>
    public required string StartScene { get; init; }
    /// <summary>材质是否必须使用稳定名。</summary>
    public required bool RequireStableMaterialNames { get; init; }
    /// <summary>默认 content 收集 globs。</summary>
    public required string[] ContentFileGlobs { get; init; }
    /// <summary>默认运行时 UI backend。</summary>
    public required AutomationUiBackend DefaultUiBackend { get; init; }
}

/// <summary>Project Settings 已配置值与当前 session 生效边界。</summary>
public sealed record AutomationProjectSettings
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程显示名。</summary>
    public required string Name { get; init; }
    /// <summary>已配置的工程内 Content root。</summary>
    public required string ContentRoot { get; init; }
    /// <summary>已配置的工程内 ScriptSource root。</summary>
    public required string ScriptSourceDir { get; init; }
    /// <summary>当前 Engine session 实际使用的工程内 Content root。</summary>
    public required string ActiveContentRoot { get; init; }
    /// <summary>当前 Engine session 实际使用的工程内 ScriptSource root。</summary>
    public required string ActiveScriptSourceDir { get; init; }
    /// <summary>默认启动 Scene。</summary>
    public required string StartScene { get; init; }
    /// <summary>材质是否必须使用稳定名。</summary>
    public required bool RequireStableMaterialNames { get; init; }
    /// <summary>默认 content 收集 globs。</summary>
    public required string[] ContentFileGlobs { get; init; }
    /// <summary>默认运行时 UI backend。</summary>
    public required AutomationUiBackend DefaultUiBackend { get; init; }
    /// <summary>当前 session 是否需重载才能应用全部已配置值。</summary>
    public required bool RequiresReload { get; init; }
    /// <summary>需重载字段的稳定名称。</summary>
    public required string[] ReloadReasons { get; init; }
}

/// <summary>Player Settings 完整值。</summary>
public sealed record AutomationPlayerSettings
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>窗口标题。</summary>
    public required string WindowTitle { get; init; }
    /// <summary>窗口宽度。</summary>
    public required int WindowWidth { get; init; }
    /// <summary>窗口高度。</summary>
    public required int WindowHeight { get; init; }
    /// <summary>窗口模式。</summary>
    public required AutomationPlayerWindowMode WindowMode { get; init; }
    /// <summary>VSync。</summary>
    public required bool VSync { get; init; }
    /// <summary>可选图标 logical path。</summary>
    public string? IconPath { get; init; }
    /// <summary>产品版本。</summary>
    public required string Version { get; init; }
    /// <summary>启动 Scene。</summary>
    public required string StartupScene { get; init; }
    /// <summary>键鼠输入。</summary>
    public required bool EnableKeyboardMouse { get; init; }
    /// <summary>手柄输入。</summary>
    public required bool EnableGamepad { get; init; }
    /// <summary>运行时 UI backend。</summary>
    public required AutomationUiBackend RuntimeUiBackend { get; init; }
    /// <summary>发行通道。</summary>
    public required AutomationPlayerReleaseChannel ReleaseChannel { get; init; }
}

/// <summary>Build Settings 中一个 Scene 条目。</summary>
public sealed record AutomationBuildScene
{
    /// <summary>显示名。</summary>
    public required string SceneName { get; init; }
    /// <summary>是否入包。</summary>
    public required bool Included { get; init; }
    /// <summary>是否为启动 Scene。</summary>
    public required bool IsStartup { get; init; }
    /// <summary>来源类型。</summary>
    public required AutomationSceneSourceKind SourceKind { get; init; }
    /// <summary>可选来源 logical path。</summary>
    public string? Source { get; init; }
}

/// <summary>Build Settings profile 完整值。</summary>
public sealed record AutomationBuildSettings
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 RID。</summary>
    public required string Rid { get; init; }
    /// <summary>编译通道。</summary>
    public required AutomationBuildChannel Channel { get; init; }
    /// <summary>配置名。</summary>
    public required string Configuration { get; init; }
    /// <summary>输出目录。</summary>
    public required string OutputDirectory { get; init; }
    /// <summary>产品名。</summary>
    public required string ProductName { get; init; }
    /// <summary>产品版本。</summary>
    public required string Version { get; init; }
    /// <summary>信息版本。</summary>
    public required string InformationalVersion { get; init; }
    /// <summary>可选图标路径。</summary>
    public string? IconPath { get; init; }
    /// <summary>包含符号。</summary>
    public required bool IncludeSymbols { get; init; }
    /// <summary>打包完整 Content。</summary>
    public required bool PackageWholeContent { get; init; }
    /// <summary>Build 后运行。</summary>
    public required bool RunAfterBuild { get; init; }
    /// <summary>Scene 入包配置。</summary>
    public required AutomationBuildScene[] Scenes { get; init; }
}
