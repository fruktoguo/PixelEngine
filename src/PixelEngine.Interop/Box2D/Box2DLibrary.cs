using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PixelEngine.Interop.Box2D;

/// <summary>
/// Box2D 原生库解析入口。
/// </summary>
public static class Box2DLibrary
{
    /// <summary>
    /// Box2D 动态库逻辑名称。
    /// </summary>
    public const string Name = "box2d";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(Box2DLibrary).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!StringComparer.Ordinal.Equals(libraryName, Name))
        {
            return IntPtr.Zero;
        }

        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "box2d.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libbox2d.dylib"
                : "libbox2d.so";
        string baseDirectory = AppContext.BaseDirectory;
        foreach (string probeDirectory in GetProbeDirectories(baseDirectory))
        {
            foreach (string rid in GetProbeRuntimeIdentifiers())
            {
                string ridPath = Path.Combine(probeDirectory, "runtimes", rid, "native", fileName);
                if (NativeLibrary.TryLoad(ridPath, assembly, searchPath, out IntPtr ridHandle))
                {
                    return ridHandle;
                }
            }

            string localPath = Path.Combine(probeDirectory, fileName);
            if (NativeLibrary.TryLoad(localPath, assembly, searchPath, out IntPtr localHandle))
            {
                return localHandle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetProbeRuntimeIdentifiers()
    {
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        yield return runtimeIdentifier;

        string? portableRuntimeIdentifier = GetPortableRuntimeIdentifier();
        if (portableRuntimeIdentifier is not null &&
            !StringComparer.Ordinal.Equals(runtimeIdentifier, portableRuntimeIdentifier))
        {
            // Framework-dependent Linux/macOS hosts report distro/version-specific RIDs
            // (for example ubuntu.24.04-x64), while engine assets intentionally use the
            // portable RID layout from docs §14.4 / plan §15.
            yield return portableRuntimeIdentifier;
        }
    }

    private static string? GetPortableRuntimeIdentifier()
    {
        Architecture processArchitecture = RuntimeInformation.ProcessArchitecture;
        string? architecture = processArchitecture == Architecture.X64
            ? "x64"
            : processArchitecture == Architecture.Arm64
                ? "arm64"
                : null;
        if (architecture is null)
        {
            return null;
        }

        string? platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "linux"
                    : null;
        return platform is null ? null : $"{platform}-{architecture}";
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
