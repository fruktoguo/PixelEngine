using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// Capability 是否会产生或只会引用大型制品。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationArtifactBehavior>))]
public enum AutomationArtifactBehavior
{
    /// <summary>该 capability 不产生大型制品。</summary>
    None,

    /// <summary>结果始终通过 <see cref="AutomationArtifactReference" /> 返回。</summary>
    Required,

    /// <summary>仅当结果超过控制面限制或客户端显式请求时返回制品。</summary>
    Optional,
}

/// <summary>
/// 列表查询的过滤组合方式。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationFilterMatch>))]
public enum AutomationFilterMatch
{
    /// <summary>所有过滤条件都必须匹配。</summary>
    All,

    /// <summary>至少一个过滤条件匹配。</summary>
    Any,
}

/// <summary>
/// 结构化过滤运算符。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationFilterOperator>))]
public enum AutomationFilterOperator
{
    /// <summary>字段值等于目标值。</summary>
    Equals,

    /// <summary>字段值不等于目标值。</summary>
    NotEquals,

    /// <summary>字符串字段包含目标文本。</summary>
    Contains,

    /// <summary>字符串字段以目标文本开头。</summary>
    StartsWith,

    /// <summary>数值字段小于目标值。</summary>
    LessThan,

    /// <summary>数值字段小于或等于目标值。</summary>
    LessThanOrEqual,

    /// <summary>数值字段大于目标值。</summary>
    GreaterThan,

    /// <summary>数值字段大于或等于目标值。</summary>
    GreaterThanOrEqual,
}

/// <summary>
/// 确定性排序方向。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationSortDirection>))]
public enum AutomationSortDirection
{
    /// <summary>升序。</summary>
    Ascending,

    /// <summary>降序。</summary>
    Descending,
}

/// <summary>
/// 一条结构化过滤条件。
/// </summary>
public sealed record AutomationFilterClause
{
    /// <summary>可查询字段的稳定名称。</summary>
    public required string Field { get; init; }

    /// <summary>过滤运算符。</summary>
    public required AutomationFilterOperator Operator { get; init; }

    /// <summary>目标 JSON 标量。</summary>
    public required JsonElement Value { get; init; }
}

/// <summary>
/// 一组结构化过滤条件。
/// </summary>
public sealed record AutomationQueryFilter
{
    /// <summary>条件组合方式。</summary>
    public AutomationFilterMatch Match { get; init; } = AutomationFilterMatch.All;

    /// <summary>过滤条件；空数组表示不过滤。</summary>
    public AutomationFilterClause[] Clauses { get; init; } = [];
}

/// <summary>
/// 一条确定性排序条件。
/// </summary>
public sealed record AutomationSortClause
{
    /// <summary>可排序字段的稳定名称。</summary>
    public required string Field { get; init; }

    /// <summary>排序方向。</summary>
    public AutomationSortDirection Direction { get; init; } = AutomationSortDirection.Ascending;
}

/// <summary>
/// 通用结构化过滤、排序与 cursor 分页请求。
/// </summary>
public sealed record AutomationPageRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>结构化过滤；省略表示不过滤。</summary>
    public AutomationQueryFilter? Filter { get; init; }

    /// <summary>按优先级排列的排序条件；服务端始终追加稳定 ID tie-breaker。</summary>
    public AutomationSortClause[] Sort { get; init; } = [];

    /// <summary>上页返回的不透明 cursor；首屏为空。</summary>
    public string? Cursor { get; init; }

    /// <summary>单页条目数，范围 1..500。</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>
/// 通用分页元数据。
/// </summary>
public sealed record AutomationPageInfo
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>本页条目数。</summary>
    public required int Returned { get; init; }

    /// <summary>过滤后的总条目数。</summary>
    public required int Total { get; init; }

    /// <summary>下一页 cursor；null 表示已经到末页。</summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// 从真实 semantic delegate 生成的机器可读 capability descriptor。
/// </summary>
public sealed record AutomationCapabilityDescriptor
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>稳定 capability/method ID。</summary>
    public required string Id { get; init; }

    /// <summary>稳定 domain ID。</summary>
    public required string Domain { get; init; }

    /// <summary>只读、可逆写入或非事务 command。</summary>
    public required AutomationOperationKind OperationKind { get; init; }

    /// <summary>请求 DTO 的 JSON Schema reference。</summary>
    public required string RequestSchema { get; init; }

    /// <summary>响应 DTO 的 JSON Schema reference。</summary>
    public required string ResponseSchema { get; init; }

    /// <summary>执行所需的全部 permission scopes。</summary>
    public required string[] RequiredScopes { get; init; }

    /// <summary>支持的 Editor mode；例如 edit、play、paused。</summary>
    public required string[] SupportedModes { get; init; }

    /// <summary>访问权威对象的唯一 safe phase。</summary>
    public required AutomationExecutionPhase ExecutionPhase { get; init; }

    /// <summary>transaction 参与策略。</summary>
    public required AutomationTransactionMode TransactionMode { get; init; }

    /// <summary>是否强制写请求携带 expected revision。</summary>
    public bool RequiresExpectedRevision { get; init; }

    /// <summary>是否强制请求携带跨连接 idempotency key。</summary>
    public bool RequiresIdempotencyKey { get; init; }

    /// <summary>该 capability 可能发布的 event type。</summary>
    public string[] EventTypes { get; init; } = [];

    /// <summary>大型数据的制品行为。</summary>
    public AutomationArtifactBehavior ArtifactBehavior { get; init; }

    /// <summary>调用同一语义实现的菜单、快捷键、面板或工具 command ID。</summary>
    public string[] UiCommandIds { get; init; } = [];
}

/// <summary>
/// Capability 列表响应。
/// </summary>
public sealed record AutomationCapabilityListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>当前 capability digest。</summary>
    public required string CapabilityDigest { get; init; }

    /// <summary>本页 descriptors。</summary>
    public required AutomationCapabilityDescriptor[] Items { get; init; }

    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>
/// Editor 的 edit/play 状态。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationEditorMode>))]
public enum AutomationEditorMode
{
    /// <summary>authoring 编辑态。</summary>
    Edit,

    /// <summary>运行态。</summary>
    Play,

    /// <summary>暂停的运行态。</summary>
    Paused,
}

/// <summary>
/// 当前 workspace 权威快照。
/// </summary>
public sealed record AutomationWorkspaceSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>是否已经打开工程。</summary>
    public required bool ProjectOpen { get; init; }

    /// <summary>工程稳定 ID。</summary>
    public string? ProjectId { get; init; }

    /// <summary>工程显示名。</summary>
    public string? ProjectName { get; init; }

    /// <summary>工程 canonical root。</summary>
    public string? ProjectRoot { get; init; }

    /// <summary>当前 Scene 稳定 ID。</summary>
    public string? SceneId { get; init; }

    /// <summary>当前 Scene logical path。</summary>
    public string? ScenePath { get; init; }

    /// <summary>当前 Scene 是否未保存。</summary>
    public required bool SceneDirty { get; init; }

    /// <summary>当前 Editor mode。</summary>
    public required AutomationEditorMode Mode { get; init; }

    /// <summary>是否存在等待 Save/Discard/Cancel 的转场。</summary>
    public required bool TransitionPending { get; init; }

    /// <summary>等待转场的稳定 kind。</summary>
    public string? TransitionKind { get; init; }

    /// <summary>等待转场的目标。</summary>
    public string? TransitionTarget { get; init; }
}

/// <summary>
/// 通用语义 command 结果。
/// </summary>
public sealed record AutomationCommandResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>命令是否成功完成或被统一 dirty guard 接纳。</summary>
    public required bool Succeeded { get; init; }

    /// <summary>稳定、可读诊断。</summary>
    public required string Diagnostic { get; init; }

    /// <summary>受影响的稳定 resource IDs。</summary>
    public string[] ResourceIds { get; init; } = [];
}

/// <summary>
/// Editor 顶层窗口快照。
/// </summary>
public enum AutomationWindowState
{
    /// <summary>普通窗口。</summary>
    Normal,

    /// <summary>最小化窗口。</summary>
    Minimized,

    /// <summary>最大化窗口。</summary>
    Maximized,

    /// <summary>全屏窗口。</summary>
    Fullscreen,
}

/// <summary>
/// Editor 顶层窗口快照。
/// </summary>
public sealed record AutomationWindowSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>逻辑客户区宽度。</summary>
    public required int LogicalWidth { get; init; }

    /// <summary>逻辑客户区高度。</summary>
    public required int LogicalHeight { get; init; }

    /// <summary>平台窗口左上角 X 坐标。</summary>
    public required int LogicalX { get; init; }

    /// <summary>平台窗口左上角 Y 坐标。</summary>
    public required int LogicalY { get; init; }

    /// <summary>framebuffer 宽度。</summary>
    public required int FramebufferWidth { get; init; }

    /// <summary>framebuffer 高度。</summary>
    public required int FramebufferHeight { get; init; }

    /// <summary>X 方向 framebuffer scale。</summary>
    public required float FramebufferScaleX { get; init; }

    /// <summary>Y 方向 framebuffer scale。</summary>
    public required float FramebufferScaleY { get; init; }

    /// <summary>实时平台窗口状态。</summary>
    public required AutomationWindowState State { get; init; }

    /// <summary>最近平台 focus event 报告窗口是否持有输入焦点。</summary>
    public required bool Focused { get; init; }

    /// <summary>当前窗口标题。</summary>
    public required string Title { get; init; }
}

/// <summary>
/// 顶层窗口尺寸请求。
/// </summary>
public sealed record AutomationWindowResizeRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标逻辑客户区宽度。</summary>
    public required int Width { get; init; }

    /// <summary>目标逻辑客户区高度。</summary>
    public required int Height { get; init; }
}

/// <summary>
/// 原子修改顶层窗口 placement、状态与焦点的请求。
/// </summary>
public sealed record AutomationWindowSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>可选窗口左上角 X；必须与 <see cref="Y"/> 同时提供。</summary>
    public int? X { get; init; }

    /// <summary>可选窗口左上角 Y；必须与 <see cref="X"/> 同时提供。</summary>
    public int? Y { get; init; }

    /// <summary>可选逻辑客户区宽度；必须与 <see cref="Height"/> 同时提供。</summary>
    public int? Width { get; init; }

    /// <summary>可选逻辑客户区高度；必须与 <see cref="Width"/> 同时提供。</summary>
    public int? Height { get; init; }

    /// <summary>可选平台窗口状态。</summary>
    public AutomationWindowState? State { get; init; }

    /// <summary>是否请求操作系统把输入焦点交给 Editor。</summary>
    public bool Activate { get; init; }
}

/// <summary>
/// 一个已注册 Editor panel 的稳定快照。
/// </summary>
public sealed record AutomationPanelInfo
{
    /// <summary>稳定 panel ID。</summary>
    public required string Id { get; init; }

    /// <summary>当前可见标题。</summary>
    public required string Title { get; init; }

    /// <summary>是否可见。</summary>
    public required bool Visible { get; init; }

    /// <summary>是否属于非停靠 chrome。</summary>
    public required bool Chrome { get; init; }

    /// <summary>是否处于独占内容区模式。</summary>
    public required bool Maximized { get; init; }

    /// <summary>是否已经请求在下一次绘制时聚焦。</summary>
    public required bool FocusPending { get; init; }

    /// <summary>panel 是否已至少完成一次 ImGui Begin，可读取 dock 运行态。</summary>
    public required bool DockStateKnown { get; init; }

    /// <summary>panel 当前是否位于 dock node。</summary>
    public required bool Docked { get; init; }

    /// <summary>由同 tab group 的稳定 panel IDs 派生的稳定 group ID。</summary>
    public string? DockGroupId { get; init; }

    /// <summary>浮动窗口或 dock node X。</summary>
    public float? X { get; init; }

    /// <summary>浮动窗口或 dock node Y。</summary>
    public float? Y { get; init; }

    /// <summary>浮动窗口或 dock node 宽度。</summary>
    public float? Width { get; init; }

    /// <summary>浮动窗口或 dock node 高度。</summary>
    public float? Height { get; init; }
}

/// <summary>
/// Panel 列表响应。
/// </summary>
public sealed record AutomationPanelListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>本页 panels。</summary>
    public required AutomationPanelInfo[] Items { get; init; }

    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>
/// Panel 显示、隐藏和聚焦请求。
/// </summary>
public sealed record AutomationPanelSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>稳定 panel ID。</summary>
    public required string PanelId { get; init; }

    /// <summary>目标可见性。</summary>
    public required bool Visible { get; init; }

    /// <summary>可见时是否在下一次绘制聚焦该 panel。</summary>
    public bool Focus { get; init; }
}

/// <summary>相对稳定目标 panel 的语义停靠位置。</summary>
public enum AutomationPanelDockPlacement
{
    /// <summary>进入目标 tab group。</summary>
    Tab,

    /// <summary>拆分到目标左侧。</summary>
    Left,

    /// <summary>拆分到目标右侧。</summary>
    Right,

    /// <summary>拆分到目标上方。</summary>
    Top,

    /// <summary>拆分到目标下方。</summary>
    Bottom,

    /// <summary>脱离 dock tree。</summary>
    Floating,
}

/// <summary>Dock、undock 或 tab 合并请求。</summary>
public sealed record AutomationPanelDockRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>源稳定 panel ID。</summary>
    public required string PanelId { get; init; }

    /// <summary>非 Floating 时的目标稳定 panel ID。</summary>
    public string? TargetPanelId { get; init; }

    /// <summary>目标位置。</summary>
    public required AutomationPanelDockPlacement Placement { get; init; }

    /// <summary>四向拆分时新区域占原区域比例。</summary>
    public float SplitRatio { get; init; } = 0.25f;

    /// <summary>Floating 时可选 X；必须与 <see cref="Y"/> 同时提供。</summary>
    public float? X { get; init; }

    /// <summary>Floating 时可选 Y；必须与 <see cref="X"/> 同时提供。</summary>
    public float? Y { get; init; }

    /// <summary>Floating 时可选宽度；必须与 <see cref="Height"/> 同时提供。</summary>
    public float? Width { get; init; }

    /// <summary>Floating 时可选高度；必须与 <see cref="Width"/> 同时提供。</summary>
    public float? Height { get; init; }
}

/// <summary>布局中的稳定 panel 可见性。</summary>
public sealed record AutomationPanelLayoutEntry
{
    /// <summary>稳定 panel ID。</summary>
    public required string PanelId { get; init; }

    /// <summary>目标可见性。</summary>
    public required bool Visible { get; init; }
}

/// <summary>可无损导入的完整 ImGui dock layout。</summary>
public sealed record AutomationDockLayoutSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>PixelEngine layout 版本。</summary>
    public required int LayoutVersion { get; init; }

    /// <summary>UTF-8 字节数。</summary>
    public required int Utf8ByteLength { get; init; }

    /// <summary>小写十六进制 SHA256。</summary>
    public required string Sha256 { get; init; }

    /// <summary>经过安全校验、可直接交给 Dear ImGui 的 ini 文本。</summary>
    public required string LayoutText { get; init; }

    /// <summary>不由 ImGui ini 保存的 panel 可见性。</summary>
    public required AutomationPanelLayoutEntry[] Panels { get; init; }
}

/// <summary>完整布局导入请求。</summary>
public sealed record AutomationDockLayoutSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>由 <c>window.layout.get</c> 捕获或按同一 schema 生成的布局。</summary>
    public required AutomationDockLayoutSnapshot Layout { get; init; }
}

/// <summary>
/// 当前 Scene 元数据快照。
/// </summary>
public sealed record AutomationSceneSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>Scene 稳定 ID。</summary>
    public required string SceneId { get; init; }

    /// <summary>Scene revision resource ID。</summary>
    public required string ResourceId { get; init; }

    /// <summary>Scene logical path。</summary>
    public required string Path { get; init; }

    /// <summary>Scene 显示名。</summary>
    public required string Name { get; init; }

    /// <summary>未保存状态。</summary>
    public required bool Dirty { get; init; }

    /// <summary>内容版本。</summary>
    public required int ContentVersion { get; init; }

    /// <summary>Scene View 非落盘状态版本。</summary>
    public required int SceneViewVersion { get; init; }

    /// <summary>场景整体替换世代。</summary>
    public required long Generation { get; init; }

    /// <summary>GameObject 数量。</summary>
    public required int GameObjectCount { get; init; }

    /// <summary>根 GameObject stable IDs。</summary>
    public required int[] RootStableIds { get; init; }

    /// <summary>选中的 GameObject stable ID。</summary>
    public int? SelectedStableId { get; init; }
}

/// <summary>
/// Scene logical path 请求。
/// </summary>
public sealed record AutomationScenePathRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>工程内 Scene logical path。</summary>
    public required string Path { get; init; }
}

/// <summary>
/// Scene 另存请求。
/// </summary>
public sealed record AutomationSceneSaveAsRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>工程内目标 Scene logical path。</summary>
    public required string Path { get; init; }

    /// <summary>是否同时设为 Project StartScene。</summary>
    public bool MakeStartScene { get; init; }
}

/// <summary>
/// Scene/Project 转场决策。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationTransitionDecision>))]
public enum AutomationTransitionDecision
{
    /// <summary>先保存当前 Scene 再继续。</summary>
    Save,

    /// <summary>丢弃当前未保存修改并继续。</summary>
    Discard,

    /// <summary>取消转场。</summary>
    Cancel,
}

/// <summary>
/// 解决等待转场的请求。
/// </summary>
public sealed record AutomationTransitionResolveRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>Save/Discard/Cancel 决策。</summary>
    public required AutomationTransitionDecision Decision { get; init; }
}

/// <summary>
/// 转场请求或解决后的状态。
/// </summary>
public sealed record AutomationTransitionResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>状态稳定 ID。</summary>
    public required string Status { get; init; }

    /// <summary>转场 kind。</summary>
    public string? Kind { get; init; }

    /// <summary>目标。</summary>
    public string? Target { get; init; }

    /// <summary>可诊断消息。</summary>
    public string? Diagnostic { get; init; }
}

/// <summary>
/// GameObject 的分页层级条目。
/// </summary>
public sealed record AutomationHierarchyItem
{
    /// <summary>Scene 内稳定 ID。</summary>
    public required int StableId { get; init; }

    /// <summary>跨资源统一格式的稳定 resource ID。</summary>
    public required string ResourceId { get; init; }

    /// <summary>父 GameObject stable ID；根对象为 null。</summary>
    public int? ParentStableId { get; init; }

    /// <summary>同级顺序。</summary>
    public required int SiblingIndex { get; init; }

    /// <summary>层级深度，根为 0。</summary>
    public required int Depth { get; init; }

    /// <summary>由 stable IDs 组成的确定性层级路径。</summary>
    public required string HierarchyPath { get; init; }

    /// <summary>显示名。</summary>
    public required string Name { get; init; }

    /// <summary>运行时 active 状态。</summary>
    public required bool Enabled { get; init; }

    /// <summary>Scene View 是否可见。</summary>
    public required bool SceneVisible { get; init; }

    /// <summary>Scene View 是否可拾取。</summary>
    public required bool ScenePickable { get; init; }

    /// <summary>是否选中。</summary>
    public required bool Selected { get; init; }

    /// <summary>直接子节点数量。</summary>
    public required int ChildCount { get; init; }

    /// <summary>脚本组件数量。</summary>
    public required int ComponentCount { get; init; }

    /// <summary>是否为 Prefab instance/root。</summary>
    public required bool HasPrefabLink { get; init; }

    /// <summary>是否有内建 Canvas (Web)。</summary>
    public required bool HasWebCanvas { get; init; }

    /// <summary>是否有内建 Canvas Scaler。</summary>
    public required bool HasCanvasScaler { get; init; }
}

/// <summary>
/// Hierarchy 列表响应。
/// </summary>
public sealed record AutomationHierarchyListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>本页层级条目。</summary>
    public required AutomationHierarchyItem[] Items { get; init; }

    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>
/// 按 Scene stable ID 寻址 GameObject 的请求。
/// </summary>
public sealed record AutomationGameObjectRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>Scene 内 GameObject stable ID。</summary>
    public required int StableId { get; init; }
}

/// <summary>
/// 读取 Inspector；未指定 stable ID 时使用当前 selection。
/// </summary>
public sealed record AutomationInspectorGetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable ID；null 表示当前选中 GameObject。</summary>
    public int? StableId { get; init; }
}

/// <summary>
/// Scene Transform DTO。
/// </summary>
public sealed record AutomationTransformValue
{
    /// <summary>局部 X。</summary>
    public required float X { get; init; }

    /// <summary>局部 Y。</summary>
    public required float Y { get; init; }

    /// <summary>局部 Z 旋转，弧度。</summary>
    public required float RotationRadians { get; init; }

    /// <summary>局部 X 缩放。</summary>
    public required float ScaleX { get; init; }

    /// <summary>局部 Y 缩放。</summary>
    public required float ScaleY { get; init; }
}

/// <summary>
/// Inspector 字段 schema 与当前序列化值。
/// </summary>
public sealed record AutomationInspectorField
{
    /// <summary>稳定字段名。</summary>
    public required string Name { get; init; }

    /// <summary>CLR value type 的稳定全名。</summary>
    public required string ValueType { get; init; }

    /// <summary>Inspector field kind。</summary>
    public required string Kind { get; init; }

    /// <summary>是否可写。</summary>
    public required bool CanWrite { get; init; }

    /// <summary>是否为 public 字段/属性。</summary>
    public required bool Public { get; init; }

    /// <summary>是否为 [SerializeField] private 字段。</summary>
    public required bool SerializedPrivate { get; init; }

    /// <summary>是否允许 null。</summary>
    public required bool Nullable { get; init; }

    /// <summary>声明范围最小值。</summary>
    public double? RangeMinimum { get; init; }

    /// <summary>声明范围最大值。</summary>
    public double? RangeMaximum { get; init; }

    /// <summary>枚举名称；非 enum 为空。</summary>
    public string[] EnumNames { get; init; } = [];

    /// <summary>typed asset kind；非资产字段为空。</summary>
    public string? AssetKind { get; init; }

    /// <summary>当前或默认序列化值。</summary>
    public required string Value { get; init; }

    /// <summary>Scene 文档是否显式覆盖该值。</summary>
    public required bool Overridden { get; init; }
}

/// <summary>
/// 一个 authoring Behaviour component 快照。
/// </summary>
public sealed record AutomationComponentSnapshot
{
    /// <summary>GameObject 内稳定 component index。</summary>
    public required int Index { get; init; }

    /// <summary>Behaviour type 全名。</summary>
    public required string TypeName { get; init; }

    /// <summary>类型当前是否可由 ScriptAssemblyRegistry 解析。</summary>
    public required bool TypeAvailable { get; init; }

    /// <summary>组件 Enabled 语义。</summary>
    public required bool Enabled { get; init; }

    /// <summary>Inspector schema 与当前值。</summary>
    public required AutomationInspectorField[] Fields { get; init; }
}

/// <summary>
/// CanvasScaler 的 UI Scale Mode。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationCanvasScaleMode>))]
public enum AutomationCanvasScaleMode
{
    /// <summary>使用固定缩放因子。</summary>
    ConstantPixelSize,

    /// <summary>相对参考分辨率缩放。</summary>
    ScaleWithScreenSize,

    /// <summary>按显示器物理 DPI 缩放。</summary>
    ConstantPhysicalSize,
}

/// <summary>
/// Scale With Screen Size 的宽高合并方式。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationCanvasScreenMatchMode>))]
public enum AutomationCanvasScreenMatchMode
{
    /// <summary>在对数空间按 Match 值插值。</summary>
    MatchWidthOrHeight,

    /// <summary>取较小缩放。</summary>
    Expand,

    /// <summary>取较大缩放。</summary>
    Shrink,
}

/// <summary>
/// Constant Physical Size 的物理单位。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationCanvasPhysicalUnit>))]
public enum AutomationCanvasPhysicalUnit
{
    /// <summary>厘米。</summary>
    Centimeters,

    /// <summary>毫米。</summary>
    Millimeters,

    /// <summary>英寸。</summary>
    Inches,

    /// <summary>点。</summary>
    Points,

    /// <summary>派卡。</summary>
    Picas,
}

/// <summary>
/// 内建 Canvas (Web) 可写内容；Primary 由独立互斥操作维护。
/// </summary>
public sealed record AutomationWebCanvasValue
{
    /// <summary>manifest asset stable ID。</summary>
    public string? ManifestAssetId { get; init; }

    /// <summary>manifest logical path。</summary>
    public string? ManifestPath { get; init; }

    /// <summary>初始 screen ID。</summary>
    public string? InitialScreenId { get; init; }

    /// <summary>authoring Enabled。</summary>
    public required bool Enabled { get; init; }

    /// <summary>Canvas 排序值。</summary>
    public required int SortingOrder { get; init; }
}

/// <summary>
/// 内建 Canvas (Web) 当前快照。
/// </summary>
public sealed record AutomationWebCanvasSnapshot
{
    /// <summary>manifest asset stable ID。</summary>
    public string? ManifestAssetId { get; init; }

    /// <summary>manifest logical path。</summary>
    public string? ManifestPath { get; init; }

    /// <summary>初始 screen ID。</summary>
    public string? InitialScreenId { get; init; }

    /// <summary>authoring Enabled。</summary>
    public required bool Enabled { get; init; }

    /// <summary>Canvas 排序值。</summary>
    public required int SortingOrder { get; init; }

    /// <summary>是否为显式 primary Canvas。</summary>
    public required bool Primary { get; init; }
}

/// <summary>
/// 内建 CanvasScaler 完整入盘值。
/// </summary>
public sealed record AutomationCanvasScalerValue
{
    /// <summary>UI Scale Mode。</summary>
    public required AutomationCanvasScaleMode ScaleMode { get; init; }

    /// <summary>Constant Pixel Size 的固定缩放。</summary>
    public required float ScaleFactor { get; init; }

    /// <summary>参考分辨率宽度。</summary>
    public required float ReferenceWidth { get; init; }

    /// <summary>参考分辨率高度。</summary>
    public required float ReferenceHeight { get; init; }

    /// <summary>参考分辨率宽高合并方式。</summary>
    public required AutomationCanvasScreenMatchMode ScreenMatchMode { get; init; }

    /// <summary>0 匹配宽、1 匹配高。</summary>
    public required float MatchWidthOrHeight { get; init; }

    /// <summary>Constant Physical Size 的物理单位。</summary>
    public required AutomationCanvasPhysicalUnit PhysicalUnit { get; init; }

    /// <summary>显示器 DPI 不可用时的回退 DPI。</summary>
    public required float FallbackScreenDpi { get; init; }

    /// <summary>图片资产默认 DPI。</summary>
    public required float DefaultSpriteDpi { get; init; }

    /// <summary>参考 pixels-per-unit。</summary>
    public required float ReferencePixelsPerUnit { get; init; }
}

/// <summary>
/// 原子替换 GameObject 上的内建 Canvas (Web) 与 CanvasScaler。
/// </summary>
public sealed record AutomationBuiltInCanvasSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>目标 Canvas (Web)；null 表示移除。</summary>
    public AutomationWebCanvasValue? WebCanvas { get; init; }

    /// <summary>目标 CanvasScaler；null 表示移除。</summary>
    public AutomationCanvasScalerValue? CanvasScaler { get; init; }
}

/// <summary>
/// 设置 Canvas (Web) 的互斥 Primary 状态。
/// </summary>
public sealed record AutomationCanvasPrimarySetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>true 会清除其他 Canvas 的 Primary；false 只清除目标。</summary>
    public required bool Primary { get; init; }
}

/// <summary>
/// Editor Undo/Redo history 快照。
/// </summary>
public sealed record AutomationHistorySnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>是否可以 Undo。</summary>
    public required bool CanUndo { get; init; }

    /// <summary>是否可以 Redo。</summary>
    public required bool CanRedo { get; init; }

    /// <summary>Undo 栈条目数。</summary>
    public required int UndoCount { get; init; }

    /// <summary>Redo 栈条目数。</summary>
    public required int RedoCount { get; init; }

    /// <summary>下一条 Undo command 名称。</summary>
    public string? UndoName { get; init; }

    /// <summary>下一条 Redo command 名称。</summary>
    public string? RedoName { get; init; }
}

/// <summary>
/// 从 GameObject subtree 创建 Prefab asset。
/// </summary>
public sealed record AutomationPrefabCreateRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>源 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>Content root 内 .prefab logical path；null 表示确定性分配。</summary>
    public string? AssetPath { get; init; }
}

/// <summary>
/// Prefab 创建结果。
/// </summary>
public sealed record AutomationPrefabCreateResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>创建或替换的 .prefab logical path。</summary>
    public required string AssetPath { get; init; }

    /// <summary>已链接 Prefab 的源 GameObject 快照。</summary>
    public required AutomationGameObjectSnapshot GameObject { get; init; }
}

/// <summary>
/// 实例化 Prefab 请求。
/// </summary>
public sealed record AutomationPrefabInstantiateRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>Content root 内 .prefab logical path。</summary>
    public required string AssetPath { get; init; }

    /// <summary>目标父 GameObject stable ID；null 表示根。</summary>
    public int? ParentStableId { get; init; }

    /// <summary>可选初始 local Transform。</summary>
    public AutomationTransformValue? Transform { get; init; }
}

/// <summary>
/// Inspector 的完整 GameObject 快照。
/// </summary>
public sealed record AutomationGameObjectSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>Hierarchy 摘要。</summary>
    public required AutomationHierarchyItem Hierarchy { get; init; }

    /// <summary>局部 Transform。</summary>
    public required AutomationTransformValue LocalTransform { get; init; }

    /// <summary>世界 Transform。</summary>
    public required AutomationTransformValue WorldTransform { get; init; }

    /// <summary>Prefab asset logical path。</summary>
    public string? PrefabAssetPath { get; init; }

    /// <summary>Prefab override property paths。</summary>
    public string[] PrefabOverridePaths { get; init; } = [];

    /// <summary>内建 Canvas (Web)；不存在时为 null。</summary>
    public AutomationWebCanvasSnapshot? WebCanvas { get; init; }

    /// <summary>内建 CanvasScaler；不存在时为 null。</summary>
    public AutomationCanvasScalerValue? CanvasScaler { get; init; }

    /// <summary>脚本组件。</summary>
    public required AutomationComponentSnapshot[] Components { get; init; }
}

/// <summary>
/// 设置当前 GameObject selection。
/// </summary>
public sealed record AutomationSelectionSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable ID；null 表示清空 GameObject selection。</summary>
    public int? StableId { get; init; }
}

/// <summary>
/// 当前 authoring GameObject selection 快照。
/// </summary>
public sealed record AutomationSelectionSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>选中 GameObject stable ID；无选择时为 null。</summary>
    public int? StableId { get; init; }

    /// <summary>选中 GameObject resource ID；无选择时为 null。</summary>
    public string? ResourceId { get; init; }
}

/// <summary>
/// 创建 GameObject 请求。
/// </summary>
public sealed record AutomationGameObjectCreateRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>新对象名称。</summary>
    public required string Name { get; init; }

    /// <summary>父 GameObject stable ID；null 表示根。</summary>
    public int? ParentStableId { get; init; }

    /// <summary>同级插入位置；null 表示末尾。</summary>
    public int? SiblingIndex { get; init; }
}

/// <summary>
/// 重命名 GameObject 请求。
/// </summary>
public sealed record AutomationGameObjectRenameRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>新名称。</summary>
    public required string Name { get; init; }
}

/// <summary>
/// 设置 GameObject bool 状态请求。
/// </summary>
public sealed record AutomationGameObjectBoolRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>目标 bool 值。</summary>
    public required bool Value { get; init; }
}

/// <summary>
/// 通用 bool 值请求。
/// </summary>
public sealed record AutomationBoolValueRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 bool 值。</summary>
    public required bool Value { get; init; }
}

/// <summary>
/// GameObject 重父请求。
/// </summary>
public sealed record AutomationGameObjectReparentRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>新父 stable ID；null 表示根。</summary>
    public int? ParentStableId { get; init; }

    /// <summary>新同级位置；null 表示末尾。</summary>
    public int? SiblingIndex { get; init; }
}

/// <summary>
/// 设置 Transform 请求。
/// </summary>
public sealed record AutomationTransformSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>目标局部 Transform。</summary>
    public required AutomationTransformValue Transform { get; init; }
}

/// <summary>
/// 添加 Behaviour component 请求。
/// </summary>
public sealed record AutomationComponentAddRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>Behaviour type 全名。</summary>
    public required string TypeName { get; init; }

    /// <summary>插入 index；null 表示末尾。</summary>
    public int? Index { get; init; }
}

/// <summary>
/// 移除 Behaviour component 请求。
/// </summary>
public sealed record AutomationComponentRemoveRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>component index。</summary>
    public required int Index { get; init; }
}

/// <summary>
/// 设置 Behaviour component Enabled 请求。
/// </summary>
public sealed record AutomationComponentEnabledSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>component index。</summary>
    public required int Index { get; init; }

    /// <summary>目标 Enabled 值。</summary>
    public required bool Enabled { get; init; }
}

/// <summary>
/// 移动 Behaviour component 请求。
/// </summary>
public sealed record AutomationComponentMoveRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>源 component index。</summary>
    public required int FromIndex { get; init; }

    /// <summary>目标 component index。</summary>
    public required int ToIndex { get; init; }
}

/// <summary>
/// 设置 Behaviour 序列化字段请求。
/// </summary>
public sealed record AutomationComponentFieldSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标 GameObject stable ID。</summary>
    public required int StableId { get; init; }

    /// <summary>component index。</summary>
    public required int ComponentIndex { get; init; }

    /// <summary>字段名。</summary>
    public required string FieldName { get; init; }

    /// <summary>目标序列化值；RemoveOverride=true 时忽略。</summary>
    public string? Value { get; init; }

    /// <summary>移除 Scene 显式覆盖并恢复脚本默认值。</summary>
    public bool RemoveOverride { get; init; }
}

/// <summary>
/// Scene View gizmo 工具。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationSceneTool>))]
public enum AutomationSceneTool
{
    /// <summary>平移 gizmo。</summary>
    Move,

    /// <summary>旋转 gizmo。</summary>
    Rotate,

    /// <summary>缩放 gizmo。</summary>
    Scale,

    /// <summary>世界画刷。</summary>
    Brush,
}

/// <summary>
/// Scene View gizmo 坐标空间。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationGizmoSpace>))]
public enum AutomationGizmoSpace
{
    /// <summary>对象局部空间。</summary>
    Local,

    /// <summary>世界空间。</summary>
    World,
}

/// <summary>
/// Scene View 内嵌 Brush Tool overlay 停靠方式。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationSceneToolOverlayDock>))]
public enum AutomationSceneToolOverlayDock
{
    /// <summary>停靠在 Scene View 左侧。</summary>
    Left,

    /// <summary>在 Scene View 内浮动。</summary>
    Floating,

    /// <summary>停靠在 Scene View 右侧。</summary>
    Right,
}

/// <summary>
/// 世界画刷设置。
/// </summary>
public sealed record AutomationBrushSettings
{
    /// <summary>Paint/Dig/Erase/Temperature。</summary>
    public required string Tool { get; init; }

    /// <summary>Point/Circle/Square。</summary>
    public required string Shape { get; init; }

    /// <summary>运行时 material ID。</summary>
    public required int MaterialId { get; init; }

    /// <summary>半径，范围 0..128。</summary>
    public required int Radius { get; init; }

    /// <summary>应用概率，范围 0..1。</summary>
    public required float Probability { get; init; }

    /// <summary>Additive/Target。</summary>
    public required string TemperatureMode { get; init; }

    /// <summary>温度增量或目标温度，摄氏度。</summary>
    public required float TemperatureCelsius { get; init; }
}

/// <summary>
/// Scene View 工具快照。
/// </summary>
public sealed record AutomationSceneToolSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>当前工具。</summary>
    public required AutomationSceneTool Tool { get; init; }

    /// <summary>gizmo 坐标空间。</summary>
    public required AutomationGizmoSpace GizmoSpace { get; init; }

    /// <summary>是否显示 grid。</summary>
    public required bool GridVisible { get; init; }

    /// <summary>gizmo transform snapping 是否启用。</summary>
    public required bool SnapEnabled { get; init; }

    /// <summary>Move snapping 步长，单位为世界像素。</summary>
    public required float MoveSnap { get; init; }

    /// <summary>Rotate snapping 步长，单位为角度。</summary>
    public required float RotationSnapDegrees { get; init; }

    /// <summary>Scale snapping 步长。</summary>
    public required float ScaleSnap { get; init; }

    /// <summary>Scene View authoring camera 中心 X。</summary>
    public required float CameraCenterX { get; init; }

    /// <summary>Scene View authoring camera 中心 Y。</summary>
    public required float CameraCenterY { get; init; }

    /// <summary>每 framebuffer pixel 对应 cell 数。</summary>
    public required float CameraCellsPerPixel { get; init; }

    /// <summary>Brush 参数面板是否可见。</summary>
    public required bool BrushPanelVisible { get; init; }

    /// <summary>Brush Tool overlay 停靠方式。</summary>
    public required AutomationSceneToolOverlayDock OverlayDock { get; init; }

    /// <summary>浮动 overlay 相对 Scene View 的 X 偏移。</summary>
    public required float OverlayOffsetX { get; init; }

    /// <summary>浮动 overlay 相对 Scene View 的 Y 偏移。</summary>
    public required float OverlayOffsetY { get; init; }

    /// <summary>画刷设置；当前项目没有画刷服务时为 null。</summary>
    public AutomationBrushSettings? Brush { get; init; }
}

/// <summary>
/// 设置 Scene View 工具请求。
/// </summary>
public sealed record AutomationSceneToolSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>目标工具；省略表示保持。</summary>
    public AutomationSceneTool? Tool { get; init; }

    /// <summary>目标 gizmo 坐标空间；省略表示保持。</summary>
    public AutomationGizmoSpace? GizmoSpace { get; init; }

    /// <summary>目标 grid 可见性；省略表示保持。</summary>
    public bool? GridVisible { get; init; }

    /// <summary>目标 gizmo snapping 开关；省略表示保持。</summary>
    public bool? SnapEnabled { get; init; }

    /// <summary>目标 Move snapping 步长；省略表示保持。</summary>
    public float? MoveSnap { get; init; }

    /// <summary>目标 Rotate snapping 步长（角度）；省略表示保持。</summary>
    public float? RotationSnapDegrees { get; init; }

    /// <summary>目标 Scale snapping 步长；省略表示保持。</summary>
    public float? ScaleSnap { get; init; }

    /// <summary>目标 authoring camera 中心 X；必须与 Y 同时提供。</summary>
    public float? CameraCenterX { get; init; }

    /// <summary>目标 authoring camera 中心 Y；必须与 X 同时提供。</summary>
    public float? CameraCenterY { get; init; }

    /// <summary>目标每 framebuffer pixel 对应 cell 数；省略表示保持。</summary>
    public float? CameraCellsPerPixel { get; init; }

    /// <summary>目标画刷设置；省略表示保持。</summary>
    public AutomationBrushSettings? Brush { get; init; }

    /// <summary>目标 Brush 参数面板可见性；false 同时停用 Brush。</summary>
    public bool? BrushPanelVisible { get; init; }

    /// <summary>目标 Brush Tool overlay 停靠方式。</summary>
    public AutomationSceneToolOverlayDock? OverlayDock { get; init; }

    /// <summary>Floating 时的相对 X 偏移；必须与 Y 同时提供。</summary>
    public float? OverlayOffsetX { get; init; }

    /// <summary>Floating 时的相对 Y 偏移；必须与 X 同时提供。</summary>
    public float? OverlayOffsetY { get; init; }
}

/// <summary>
/// Scene View framing 目标。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationSceneFrameTarget>))]
public enum AutomationSceneFrameTarget
{
    /// <summary>全部 authoring 内容。</summary>
    All,

    /// <summary>当前选中 GameObject。</summary>
    Selected,
}

/// <summary>
/// Scene View framing 请求。
/// </summary>
public sealed record AutomationSceneFrameRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>Frame All 或 Frame Selected。</summary>
    public required AutomationSceneFrameTarget Target { get; init; }
}

/// <summary>
/// 在世界坐标应用当前画刷的请求。
/// </summary>
public sealed record AutomationBrushApplyRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>世界 X cell。</summary>
    public required int X { get; init; }

    /// <summary>世界 Y cell。</summary>
    public required int Y { get; init; }
}

/// <summary>
/// 画刷应用结果。
/// </summary>
public sealed record AutomationBrushApplyResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>实际写入 cell 数。</summary>
    public required int WrittenCells { get; init; }

    /// <summary>因 chunk 未驻留跳过的 cell 数。</summary>
    public required int SkippedNonResidentCells { get; init; }

    /// <summary>因越过 authoring 世界边界跳过的 cell 数。</summary>
    public required int SkippedOutOfBoundsCells { get; init; }

    /// <summary>权威诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>
/// 世界 cell 坐标点。
/// </summary>
public sealed record AutomationWorldPoint
{
    /// <summary>世界 X cell。</summary>
    public required int X { get; init; }

    /// <summary>世界 Y cell。</summary>
    public required int Y { get; init; }
}

/// <summary>
/// 使用当前画刷沿控制点折线执行连续 stroke。
/// </summary>
public sealed record AutomationBrushStrokeRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>至少一个控制点；服务端以 Bresenham 确定性栅格化相邻线段。</summary>
    public required AutomationWorldPoint[] Points { get; init; }
}

/// <summary>
/// 连续画刷 stroke 结果。
/// </summary>
public sealed record AutomationBrushStrokeResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;

    /// <summary>请求控制点数。</summary>
    public required int ControlPointCount { get; init; }

    /// <summary>实际栅格化采样点数。</summary>
    public required int SampleCount { get; init; }

    /// <summary>累计实际写入 cell 数。</summary>
    public required long WrittenCells { get; init; }

    /// <summary>累计因 chunk 未驻留跳过的 cell 数。</summary>
    public required long SkippedNonResidentCells { get; init; }

    /// <summary>累计因越过 authoring 世界边界跳过的 cell 数。</summary>
    public required long SkippedOutOfBoundsCells { get; init; }

    /// <summary>权威诊断。</summary>
    public required string Diagnostic { get; init; }
}
