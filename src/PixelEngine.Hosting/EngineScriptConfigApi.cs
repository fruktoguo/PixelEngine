using System.Text.Json.Serialization.Metadata;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting ContentRoot 配置加载门面暴露给脚本上下文。
/// </summary>
public sealed class EngineScriptConfigApi(string contentRoot) : IConfigApi
{
    private readonly string _contentRoot = Path.GetFullPath(contentRoot);

    /// <summary>
    /// 从 ContentRoot 下按相对路径加载脚本配置文件。
    /// </summary>
    /// <typeparam name="TConfig">配置对象类型。</typeparam>
    /// <param name="relativePath">相对 ContentRoot 的配置路径。</param>
    /// <param name="typeInfo">source-generated JSON 类型信息。</param>
    /// <returns>反序列化后的配置对象。</returns>
    public TConfig Load<TConfig>(string relativePath, JsonTypeInfo<TConfig> typeInfo)
        where TConfig : class
    {
        return EngineContentLoader.LoadConfig(_contentRoot, relativePath, typeInfo);
    }
}
