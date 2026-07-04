using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// 编辑态局部 Transform；v1 场景缺失时按单位 Transform 处理。
    /// </summary>
    public EngineSceneTransformDocument? Transform { get; init; }

    /// <summary>
    /// 可选 Prefab authoring 元数据；运行时物化会忽略该字段，编辑器用于保持 prefab 实例关系。
    /// </summary>
    public EngineScenePrefabDocument? Prefab { get; init; }

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
/// .scene 文件中的 Prefab 实例 authoring 元数据。
/// </summary>
public sealed class EngineScenePrefabDocument
{
    /// <summary>
    /// 相对 content 根目录的 prefab 资产路径。
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
