using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>分页读取一个稳定资产的引用位置。</summary>
public sealed record AutomationAssetReferenceListRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>工程级 stable asset id。</summary>
    public required string AssetId { get; init; }
    /// <summary>结构化过滤条件。</summary>
    public AutomationQueryFilter? Filter { get; init; }
    /// <summary>结构化排序条件。</summary>
    public AutomationSortClause[] Sort { get; init; } = [];
    /// <summary>页大小。</summary>
    public int PageSize { get; init; } = 100;
    /// <summary>上一页返回的 opaque cursor。</summary>
    public string? Cursor { get; init; }
}

/// <summary>资产在 Scene/Prefab 文档或当前内存 Scene 中的一处引用。</summary>
public sealed record AutomationAssetReferenceInfo
{
    /// <summary>由资产 ID 与 location 派生的稳定引用 ID。</summary>
    public required string ReferenceId { get; init; }
    /// <summary>可定位的 document:object.component.field 位置。</summary>
    public required string Location { get; init; }
    /// <summary>该引用是否来自当前内存中的 active Scene。</summary>
    public required bool ActiveScene { get; init; }
}

/// <summary>一个稳定资产的引用分页结果。</summary>
public sealed record AutomationAssetReferenceListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 stable asset id。</summary>
    public required string AssetId { get; init; }
    /// <summary>全部引用数量。</summary>
    public required int ReferenceCount { get; init; }
    /// <summary>包含引用的磁盘文档数量。</summary>
    public required int ReferenceDocuments { get; init; }
    /// <summary>当前内存 Scene 是否包含引用。</summary>
    public required bool ActiveSceneHasReferences { get; init; }
    /// <summary>本页引用。</summary>
    public required AutomationAssetReferenceInfo[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>Game View preset 的尺寸语义。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationGameViewPresetKind>))]
public enum AutomationGameViewPresetKind
{
    /// <summary>Player Settings 默认窗口尺寸。</summary>
    PlayerDefault,
    /// <summary>跟随当前 Game View 可用区域。</summary>
    FreeAspect,
    /// <summary>固定宽高比。</summary>
    AspectRatio,
    /// <summary>固定像素分辨率。</summary>
    FixedResolution,
}

/// <summary>Game presentation 的来源。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationGamePresentationSource>))]
public enum AutomationGamePresentationSource
{
    /// <summary>Player Settings。</summary>
    PlayerDefault,
    /// <summary>Editor Free Aspect。</summary>
    EditorFreeAspect,
    /// <summary>Editor ratio preset。</summary>
    EditorAspectRatio,
    /// <summary>Editor fixed-resolution preset。</summary>
    EditorFixedResolution,
}

/// <summary>Game View 的一个稳定 preset。</summary>
public sealed record AutomationGameViewPreset
{
    /// <summary>稳定 preset ID。</summary>
    public required string PresetId { get; init; }
    /// <summary>显示名。</summary>
    public required string Name { get; init; }
    /// <summary>尺寸语义。</summary>
    public required AutomationGameViewPresetKind Kind { get; init; }
    /// <summary>内建 preset 为 true；自定义 preset 为 false。</summary>
    public required bool BuiltIn { get; init; }
    /// <summary>ratio 分子或固定宽度；不适用时为 0。</summary>
    public required int ValueA { get; init; }
    /// <summary>ratio 分母或固定高度；不适用时为 0。</summary>
    public required int ValueB { get; init; }
}

/// <summary>左下原点的整数 pixel rectangle。</summary>
public sealed record AutomationPixelRect
{
    /// <summary>X。</summary>
    public required int X { get; init; }
    /// <summary>Y。</summary>
    public required int Y { get; init; }
    /// <summary>宽度。</summary>
    public required int Width { get; init; }
    /// <summary>高度。</summary>
    public required int Height { get; init; }
}

/// <summary>Game View toolbar/workspace 与已提交 presentation 的同一快照。</summary>
public sealed record AutomationGamePresentationSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>当前选中的 preset ID。</summary>
    public required string SelectedPresetId { get; init; }
    /// <summary>0 表示 Fit，其余为 presentation-to-display 百分比。</summary>
    public required float ScalePercent { get; init; }
    /// <summary>裁剪中心 X 平移，单位为 presentation pixel。</summary>
    public required float PanX { get; init; }
    /// <summary>裁剪中心 Y 平移，单位为 presentation pixel。</summary>
    public required float PanY { get; init; }
    /// <summary>进入 Play 时是否最大化。</summary>
    public required bool MaximizeOnPlay { get; init; }
    /// <summary>Game View 当前是否最大化。</summary>
    public required bool Maximized { get; init; }
    /// <summary>所有内建及自定义 presets。</summary>
    public required AutomationGameViewPreset[] Presets { get; init; }
    /// <summary>是否已有可捕获的完整 presentation。</summary>
    public required bool HasCommittedPresentation { get; init; }
    /// <summary>完整 presentation 宽度；尚无提交时为 0。</summary>
    public required int PresentationWidth { get; init; }
    /// <summary>完整 presentation 高度；尚无提交时为 0。</summary>
    public required int PresentationHeight { get; init; }
    /// <summary>presentation 内容单调 revision；尚无提交时为 0。</summary>
    public required long PresentationRevision { get; init; }
    /// <summary>产生该 presentation 的 toolbar request revision。</summary>
    public required long RequestRevision { get; init; }
    /// <summary>presentation 来源；尚无提交时为空。</summary>
    public AutomationGamePresentationSource? Source { get; init; }
    /// <summary>letterbox 后的 world content rect；尚无提交时为空。</summary>
    public AutomationPixelRect? WorldContentRect { get; init; }
    /// <summary>最后一条非致命诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>原子替换 Game View toolbar/workspace 状态。</summary>
public sealed record AutomationGamePresentationSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>目标 preset ID。</summary>
    public required string SelectedPresetId { get; init; }
    /// <summary>0 表示 Fit，其余为 presentation-to-display 百分比。</summary>
    public required float ScalePercent { get; init; }
    /// <summary>裁剪中心 X 平移。</summary>
    public required float PanX { get; init; }
    /// <summary>裁剪中心 Y 平移。</summary>
    public required float PanY { get; init; }
    /// <summary>进入 Play 时是否最大化。</summary>
    public required bool MaximizeOnPlay { get; init; }
    /// <summary>Game View 当前是否最大化。</summary>
    public required bool Maximized { get; init; }
    /// <summary>完整替换的自定义 fixed-resolution presets。</summary>
    public AutomationGameViewPreset[] CustomPresets { get; init; } = [];
}

/// <summary>向当前 Play session 的可见 Game UI 屏幕投递稳定 action。</summary>
public sealed record AutomationGameUiActionInvokeRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>由 runtime Inspector 读取的正整数屏幕实例句柄。</summary>
    public required int ScreenHandle { get; init; }
    /// <summary>XHTML data-event-* 声明的稳定 action 字符串。</summary>
    public required string Action { get; init; }
    /// <summary>可选标量载荷；支持 null、Boolean、Int64 与有限 Double。</summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>分页读取当前 session 的 artifacts。</summary>
public sealed record AutomationArtifactListResponse
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>本页 artifact 引用。</summary>
    public required AutomationArtifactReference[] Items { get; init; }
    /// <summary>分页元数据。</summary>
    public required AutomationPageInfo Page { get; init; }
}

/// <summary>按当前 session 内稳定 ID 寻址 artifact。</summary>
public sealed record AutomationArtifactRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>artifact ID。</summary>
    public required string ArtifactId { get; init; }
}

/// <summary>artifact 长度与 SHA256 校验结果。</summary>
public sealed record AutomationArtifactVerifyResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>artifact ID。</summary>
    public required string ArtifactId { get; init; }
    /// <summary>磁盘内容是否仍与发布引用一致。</summary>
    public required bool Verified { get; init; }
}

/// <summary>artifact 删除结果。</summary>
public sealed record AutomationArtifactDeleteResult
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>artifact ID。</summary>
    public required string ArtifactId { get; init; }
    /// <summary>artifact 是否存在并已删除。</summary>
    public required bool Deleted { get; init; }
}
