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
    /// 挂载在实体上的 Behaviour 文档数组。
    /// </summary>
    public EngineSceneBehaviourDocument[]? Behaviours { get; init; }
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
