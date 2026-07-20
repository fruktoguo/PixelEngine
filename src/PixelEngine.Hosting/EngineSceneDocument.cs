using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// .scene 文件根文档，描述脚本实体与 Behaviour 组件。
/// </summary>
public sealed class EngineSceneDocument
{
    /// <summary>
    /// 场景文件格式版本。
    /// </summary>
    public int FormatVersion { get; init; } = 1;

    /// <summary>
    /// 场景稳定名称。
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 可选初始世界存档目录；相对路径按 .scene 文件所在目录解析。
    /// </summary>
    public string? InitialSaveDirectory { get; init; }

    /// <summary>
    /// 可选流式程序化世界生成器键；与 <see cref="InitialSaveDirectory" /> 互斥。
    /// </summary>
    public string? ProceduralWorldGenerator { get; init; }

    /// <summary>
    /// 脚本实体数组。
    /// </summary>
    public EngineSceneEntityDocument[]? Entities { get; init; }
}

/// <summary>
/// .scene 文件中的脚本实体文档。
/// </summary>
public sealed class EngineSceneEntityDocument
{
    /// <summary>
    /// 编辑器侧稳定实体 id。
    /// </summary>
    public int StableId { get; init; }

    /// <summary>
    /// 实体显示名称。
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 父实体稳定 id；为空表示根实体。
    /// </summary>
    public int? ParentId { get; init; }

    /// <summary>
    /// GameObject authoring 激活位；旧场景缺失时按启用处理。
    /// 父级禁用会在运行时物化阶段递归覆盖子级。
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// 编辑态局部 Transform；v1 场景缺失时按单位 Transform 处理。
    /// </summary>
    public EngineSceneTransformDocument? Transform { get; init; }

    /// <summary>
    /// 可选 Prefab authoring 元数据；运行时物化会忽略该字段，编辑器用于保持 prefab 实例关系。
    /// </summary>
    public EngineScenePrefabDocument? Prefab { get; init; }

    /// <summary>
    /// 可选的内建 Canvas (Web) 组件；不进入 Behaviour 生命周期或脚本组件桶。
    /// </summary>
    public EngineSceneWebCanvasDocument? WebCanvas { get; init; }

    /// <summary>
    /// 可选的内建 Canvas Scaler 组件；没有同对象 WebCanvas 时仅保留 authoring 诊断。
    /// </summary>
    public EngineSceneCanvasScalerDocument? CanvasScaler { get; init; }

    /// <summary>
    /// 挂载在实体上的 Behaviour 文档数组。
    /// </summary>
    public EngineSceneBehaviourDocument[]? Behaviours { get; init; }
}

/// <summary>
/// .scene 文件中的编辑态局部 2D Transform。
/// </summary>
public sealed class EngineSceneTransformDocument
{
    /// <summary>
    /// 局部 X 坐标，单位 cell。
    /// </summary>
    public float X { get; init; }

    /// <summary>
    /// 局部 Y 坐标，单位 cell。
    /// </summary>
    public float Y { get; init; }

    /// <summary>
    /// 局部旋转角，单位弧度。
    /// </summary>
    public float RotationRadians { get; init; }

    /// <summary>
    /// 局部 X 方向缩放。
    /// </summary>
    public float ScaleX { get; init; } = 1f;

    /// <summary>
    /// 局部 Y 方向缩放。
    /// </summary>
    public float ScaleY { get; init; } = 1f;
}

/// <summary>
/// .scene v3 中的内建屏幕空间 Web Canvas 组件。
/// </summary>
public sealed class EngineSceneWebCanvasDocument
{
    /// <summary>
    /// UI manifest 的工程级 stable asset id；运行时可在无 Editor manifest 时回退到 <see cref="ManifestPath" />。
    /// </summary>
    public string? ManifestAssetId { get; init; }

    /// <summary>
    /// 相对 content 根目录的 UI manifest 路径；为空时使用 ui/ui-manifest.json 兼容入口。
    /// </summary>
    public string? ManifestPath { get; init; }

    /// <summary>
    /// Canvas 物化后自动显示的可选 manifest screen id；为空时只预载 manifest 声明项。
    /// </summary>
    public string? InitialScreenId { get; init; }

    /// <summary>
    /// 组件启用位；还会与 owning GameObject 及父级 enabled 共同解析。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 多 Canvas 合成排序；小值先绘制，大值后绘制并优先命中。
    /// </summary>
    public int SortingOrder { get; init; }

    /// <summary>
    /// 是否是 scene-level primary Canvas；Prefab asset 不允许持久化该值。
    /// </summary>
    public bool Primary { get; init; }
}

/// <summary>
/// .scene v3 中的内建 Canvas Scaler 组件 DTO。
/// </summary>
public sealed class EngineSceneCanvasScalerDocument
{
    /// <summary>UI 缩放模式。</summary>
    public UiScaleMode ScaleMode { get; init; } = UiScaleMode.ConstantPixelSize;

    /// <summary>Constant Pixel Size 的固定缩放。</summary>
    public float ScaleFactor { get; init; } = 1f;

    /// <summary>参考分辨率宽度。</summary>
    public float ReferenceWidth { get; init; } = 800f;

    /// <summary>参考分辨率高度。</summary>
    public float ReferenceHeight { get; init; } = 600f;

    /// <summary>参考分辨率宽高合并方式。</summary>
    public UiScreenMatchMode ScreenMatchMode { get; init; } = UiScreenMatchMode.MatchWidthOrHeight;

    /// <summary>Match Width Or Height 插值；0 匹配宽，1 匹配高。</summary>
    public float MatchWidthOrHeight { get; init; }

    /// <summary>Constant Physical Size 的物理单位。</summary>
    public UiPhysicalUnit PhysicalUnit { get; init; } = UiPhysicalUnit.Points;

    /// <summary>raw physical DPI 不可用时的明确 fallback。</summary>
    public float FallbackScreenDpi { get; init; } = 96f;

    /// <summary>图片资产默认 DPI。</summary>
    public float DefaultSpriteDpi { get; init; } = 96f;

    /// <summary>参考 pixels-per-unit。</summary>
    public float ReferencePixelsPerUnit { get; init; } = 100f;

    /// <summary>
    /// 转换为后端与 Canvas registry 共用的不可变设置。
    /// </summary>
    /// <returns>完整 CanvasScaler 设置。</returns>
    public UiCanvasScalerSettings ToSettings()
    {
        return new UiCanvasScalerSettings(
            ScaleMode,
            ScaleFactor,
            ReferenceWidth,
            ReferenceHeight,
            ScreenMatchMode,
            MatchWidthOrHeight,
            PhysicalUnit,
            FallbackScreenDpi,
            DefaultSpriteDpi,
            ReferencePixelsPerUnit);
    }

    /// <summary>
    /// 从不可变设置创建可序列化 DTO。
    /// </summary>
    /// <param name="settings">完整 CanvasScaler 设置。</param>
    /// <returns>可写入 .scene 的 DTO。</returns>
    public static EngineSceneCanvasScalerDocument FromSettings(in UiCanvasScalerSettings settings)
    {
        return new EngineSceneCanvasScalerDocument
        {
            ScaleMode = settings.ScaleMode,
            ScaleFactor = settings.ScaleFactor,
            ReferenceWidth = settings.ReferenceWidth,
            ReferenceHeight = settings.ReferenceHeight,
            ScreenMatchMode = settings.ScreenMatchMode,
            MatchWidthOrHeight = settings.MatchWidthOrHeight,
            PhysicalUnit = settings.PhysicalUnit,
            FallbackScreenDpi = settings.FallbackScreenDpi,
            DefaultSpriteDpi = settings.DefaultSpriteDpi,
            ReferencePixelsPerUnit = settings.ReferencePixelsPerUnit,
        };
    }
}

/// <summary>
/// .scene 文件中的 Prefab 实例 authoring 元数据。
/// </summary>
public sealed class EngineScenePrefabDocument
{
    /// <summary>
    /// 工程级 stable asset id；编辑器使用该字段在移动 / 重命名后解析 prefab。
    /// </summary>
    public string? AssetId { get; init; }

    /// <summary>
    /// 相对 content 根目录的 prefab 资产路径；作为人可读 logical path 与旧文档 fallback。
    /// </summary>
    public string? AssetPath { get; init; }

    /// <summary>
    /// prefab 资产内源对象稳定路径或局部 id。
    /// </summary>
    public string? SourceStableId { get; init; }

    /// <summary>
    /// 该实例记录的属性覆盖。
    /// </summary>
    public EngineScenePrefabOverrideDocument[]? Overrides { get; init; }
}

/// <summary>
/// .scene 文件中的 Prefab override 条目。
/// </summary>
public sealed class EngineScenePrefabOverrideDocument
{
    /// <summary>
    /// prefab 资产内对象稳定路径或局部 id。
    /// </summary>
    public string? SourceStableId { get; init; }

    /// <summary>
    /// 被覆盖的属性路径，例如 Transform.X 或 Component:Type.Field。
    /// </summary>
    public string? PropertyPath { get; init; }

    /// <summary>
    /// 覆盖后的字符串值。
    /// </summary>
    public string? Value { get; init; }
}

/// <summary>
/// .scene 文件中的 Behaviour 组件文档。
/// </summary>
public sealed class EngineSceneBehaviourDocument
{
    /// <summary>
    /// Behaviour 类型全名或程序集限定名。
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// 需要写入的公开字段/属性字符串值。
    /// </summary>
    public Dictionary<string, string>? SerializedFields { get; init; }
}

/// <summary>
/// .scene 文件 JSON source generation 上下文。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(EngineSceneDocument))]
internal sealed partial class EngineSceneJsonContext : JsonSerializerContext
{
}
