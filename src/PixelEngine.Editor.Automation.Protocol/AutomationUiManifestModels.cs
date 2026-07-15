namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>UI Manifest 中一个稳定 screen。</summary>
public sealed record AutomationUiManifestScreen
{
    /// <summary>Manifest 内稳定 screen ID。</summary>
    public required string ScreenId { get; init; }
    /// <summary>相对 content/ui 的 screen path。</summary>
    public required string Path { get; init; }
    /// <summary>是否预加载。</summary>
    public required bool Preload { get; init; }
    /// <summary>目标文件是否存在。</summary>
    public required bool FileExists { get; init; }
    /// <summary>对应工程 stable asset ID；尚未登记时为空。</summary>
    public string? AssetId { get; init; }
    /// <summary>Content-rooted logical path。</summary>
    public required string LogicalPath { get; init; }
}

/// <summary>UI Manifest 完整 screen 快照。</summary>
public sealed record AutomationUiManifestSnapshot
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>按 screen ID 排序的完整列表。</summary>
    public required AutomationUiManifestScreen[] Screens { get; init; }
    /// <summary>缺失 screen 文件数。</summary>
    public required int MissingFileCount { get; init; }
    /// <summary>没有 stable asset ID 的 screen 数。</summary>
    public required int UnregisteredAssetCount { get; init; }
    /// <summary>最近语义操作诊断。</summary>
    public required string Diagnostic { get; init; }
}

/// <summary>设置一个 UI Manifest screen 的 preload。</summary>
public sealed record AutomationUiManifestPreloadSetRequest
{
    /// <summary>DTO schema 版本。</summary>
    public int SchemaVersion { get; init; } = AutomationProtocolConstants.WireSchemaVersion;
    /// <summary>Manifest 内稳定 screen ID。</summary>
    public required string ScreenId { get; init; }
    /// <summary>目标 preload 状态。</summary>
    public required bool Preload { get; init; }
}
