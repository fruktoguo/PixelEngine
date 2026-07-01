using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace PixelEngine.Scripting;

internal sealed class ScriptLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
{
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

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        Assembly? shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
            assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
        return shared;
    }
}
