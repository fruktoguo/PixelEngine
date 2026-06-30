using System.Reflection;
using System.Runtime.Loader;

namespace PixelEngine.Scripting;

internal sealed class ScriptLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
{
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
