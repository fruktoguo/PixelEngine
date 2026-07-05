using System.Text.Json.Serialization.Metadata;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting ContentRoot 配置加载门面暴露给脚本上下文。
/// </summary>
public sealed class EngineScriptConfigApi(string contentRoot) : IConfigApi
{
    private readonly string _contentRoot = Path.GetFullPath(contentRoot);

    /// <inheritdoc />
    public TConfig Load<TConfig>(string relativePath, JsonTypeInfo<TConfig> typeInfo)
        where TConfig : class
    {
        return EngineContentLoader.LoadConfig(_contentRoot, relativePath, typeInfo);
    }
}
