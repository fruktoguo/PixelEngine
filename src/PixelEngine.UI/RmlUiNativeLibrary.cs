using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PixelEngine.UI;

/// <summary>
/// PixelEngine.UI.Native 动态库解析入口。
/// </summary>
public static class RmlUiNativeLibrary
{
    /// <summary>
    /// UI native 动态库逻辑名称。
    /// </summary>
    public const string Name = "PixelEngine.UI.Native";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(RmlUiNativeLibrary).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!StringComparer.Ordinal.Equals(libraryName, Name))
        {
            return IntPtr.Zero;
        }

        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "PixelEngine.UI.Native.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libPixelEngine.UI.Native.dylib"
                : "libPixelEngine.UI.Native.so";
        string baseDirectory = AppContext.BaseDirectory;
        string rid = RuntimeInformation.RuntimeIdentifier;
        foreach (string probeDirectory in GetProbeDirectories(baseDirectory))
        {
            string ridPath = Path.Combine(probeDirectory, "runtimes", rid, "native", fileName);
            if (NativeLibrary.TryLoad(ridPath, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }

            string localPath = Path.Combine(probeDirectory, fileName);
            if (NativeLibrary.TryLoad(localPath, assembly, searchPath, out handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetProbeDirectories(string baseDirectory)
    {
        yield return baseDirectory;
        string packageDependencyDirectory = Path.Combine(baseDirectory, "app");
        if (Directory.Exists(packageDependencyDirectory))
        {
            yield return packageDependencyDirectory;
        }
    }
}
