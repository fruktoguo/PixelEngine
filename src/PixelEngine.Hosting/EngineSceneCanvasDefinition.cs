using PixelEngine.UI;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 经 .scene 校验、层级 enabled 与 primary 解析后的单个 runtime Canvas 定义。
/// </summary>
public readonly record struct EngineSceneCanvasDefinition
{
    /// <summary>
    /// 创建完整 runtime Canvas 定义。
    /// </summary>
    public EngineSceneCanvasDefinition(
        ScriptUi.UiCanvasId id,
        int stableId,
        int sortingOrder,
        bool isPrimary,
        bool isImplicit,
        string? manifestAssetId,
        string? manifestPath,
        string? initialScreenId,
        UiCanvasScalerSettings scalerSettings)
    {
        Id = id;
        StableId = stableId;
        SortingOrder = sortingOrder;
        IsPrimary = isPrimary;
        IsImplicit = isImplicit;
        ManifestAssetId = manifestAssetId;
        ManifestPath = manifestPath;
        InitialScreenId = initialScreenId;
        ScalerSettings = scalerSettings;
    }

    /// <summary>稳定 opaque Canvas id。</summary>
    public ScriptUi.UiCanvasId Id { get; init; }

    /// <summary>owning GameObject StableId；implicit Canvas 为 0。</summary>
    public int StableId { get; init; }

    /// <summary>低值先合成，高值后合成并优先输入命中。</summary>
    public int SortingOrder { get; init; }

    /// <summary>是否为解析后的 primary Canvas。</summary>
    public bool IsPrimary { get; init; }

    /// <summary>是否是不落盘的旧场景兼容 Canvas。</summary>
    public bool IsImplicit { get; init; }

    /// <summary>可选 manifest stable asset id。</summary>
    public string? ManifestAssetId { get; init; }

    /// <summary>相对 content 根目录的可选 manifest 路径。</summary>
    public string? ManifestPath { get; init; }

    /// <summary>物化后自动显示的可选 screen id。</summary>
    public string? InitialScreenId { get; init; }

    /// <summary>该 Canvas 独立使用的完整 scaler 设置。</summary>
    public UiCanvasScalerSettings ScalerSettings { get; init; }
}

/// <summary>
/// 场景 Canvas authoring 诊断类型。
/// </summary>
public enum EngineSceneCanvasDiagnosticKind
{
    /// <summary>旧场景正在使用不落盘的 implicit Canvas。</summary>
    LegacyImplicit = 0,

    /// <summary>WebCanvas 没有同对象 Scaler，运行时使用明确默认值。</summary>
    MissingScaler = 1,

    /// <summary>Scaler 没有同对象 WebCanvas，因此保持 inactive。</summary>
    OrphanScaler = 2,

    /// <summary>被标记 primary 的 Canvas 因自身或层级 disabled 被跳过。</summary>
    DisabledPrimary = 3,

    /// <summary>场景有显式 Canvas，但没有任何已启用实例。</summary>
    AllCanvasesDisabled = 4,
}

/// <summary>
/// 可定位到 GameObject StableId 的 Canvas authoring 诊断。
/// </summary>
/// <param name="Kind">诊断类型。</param>
/// <param name="StableId">相关 GameObject StableId；scene-level 诊断为 0。</param>
/// <param name="Message">面向 Editor/日志的中文说明。</param>
public readonly record struct EngineSceneCanvasDiagnostic(
    EngineSceneCanvasDiagnosticKind Kind,
    int StableId,
    string Message);

/// <summary>
/// 单次场景解析产生的确定性 Canvas 列表、primary 与 authoring 诊断。
/// </summary>
public sealed class EngineSceneCanvasSet
{
    private readonly EngineSceneCanvasDefinition[] _canvases;
    private readonly EngineSceneCanvasDiagnostic[] _diagnostics;

    internal EngineSceneCanvasSet(
        bool hasExplicitCanvases,
        EngineSceneCanvasDefinition[] canvases,
        EngineSceneCanvasDiagnostic[] diagnostics)
    {
        HasExplicitCanvases = hasExplicitCanvases;
        _canvases = canvases;
        _diagnostics = diagnostics;
    }

    /// <summary>文档是否至少声明一个 WebCanvas 组件。</summary>
    public bool HasExplicitCanvases { get; }

    /// <summary>已启用并按 sorting order/StableId 排序的 Canvas 数量。</summary>
    public int Count => _canvases.Length;

    /// <summary>解析后的 primary id；全部显式 Canvas disabled 时为默认值。</summary>
    public ScriptUi.UiCanvasId PrimaryId
    {
        get
        {
            for (int i = 0; i < _canvases.Length; i++)
            {
                if (_canvases[i].IsPrimary)
                {
                    return _canvases[i].Id;
                }
            }

            return default;
        }
    }

    /// <summary>确定性 Canvas 序列。</summary>
    public ReadOnlySpan<EngineSceneCanvasDefinition> Canvases => _canvases;

    /// <summary>非阻塞 authoring 诊断序列。</summary>
    public ReadOnlySpan<EngineSceneCanvasDiagnostic> Diagnostics => _diagnostics;
}
