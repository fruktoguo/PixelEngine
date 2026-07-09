using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace PixelEngine.Scripting;

/// <summary>
/// 可收集的脚本程序集加载上下文；热重载时卸载旧上下文以释放脚本类型。
/// </summary>
internal sealed class ScriptLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
{
    /// <summary>
    /// 从内存中的 PE/PDB 镜像加载程序集，不写入磁盘。
    /// </summary>
    [RequiresUnreferencedCode("脚本热重载会从运行时编译的程序集镜像加载代码；这些脚本类型不属于 trimmed 引擎静态闭包。")]
    [RequiresDynamicCode("脚本热重载依赖运行时生成并加载程序集，NativeAOT 发布应关闭该路径。")]
    public Assembly LoadFromImages(byte[] peImage, byte[]? pdbImage = null)
    {
        ArgumentNullException.ThrowIfNull(peImage);
        using MemoryStream peStream = new(peImage, writable: false);
        if (pdbImage is { Length: > 0 })
        {
            using MemoryStream pdbStream = new(pdbImage, writable: false);
            return LoadFromStream(peStream, pdbStream);
        }

        return LoadFromStream(peStream);
    }

    /// <summary>
    /// 脚本程序集未自带依赖时，回退到默认上下文中的共享程序集（引擎 BCL 等）。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        Assembly? shared = Default.Assemblies.FirstOrDefault(
            assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
        return shared;
    }
}
