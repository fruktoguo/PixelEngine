#if !PIXELENGINE_RUNTIME_SCRIPT_COMPILATION
using System.Text.Json.Serialization;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 权威内容目录的 AOT/source-generated JSON 元数据。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BiomeCatalog))]
[JsonSerializable(typeof(NoitaWangTerrainCatalog))]
internal sealed partial class DemoContentJsonContext : JsonSerializerContext;
#endif
