using Xunit;
using System.Runtime.InteropServices;

namespace PixelEngine.UI.Tests;

/// <summary>
/// RmlUi 原生信息测试：版本与能力报告结构。
/// </summary>
public sealed class RmlUiNativeInfoTests
{
    /// <summary>
    /// 验证Native Probe Reports Unavailable Instead Of Throwing When Library Is Missing。
    /// </summary>
    [Fact]
    public void NativeProbeReportsUnavailableInsteadOfThrowingWhenLibraryIsMissing()
    {
        bool available = RmlUiNativeInfo.TryQuery(out RmlUiNativeProbe probe);
        bool nativeLibraryPresent = File.Exists(GetExpectedNativeLibraryPath());

        Assert.Equal(available, probe.IsAvailable);
        if (nativeLibraryPresent)
        {
            Assert.True(available);
        }

        if (available)
        {
            Assert.Equal(2, probe.ApiVersion);
            Assert.False(string.IsNullOrWhiteSpace(probe.RmlUiVersion));
            Assert.Null(probe.Error);
        }
        else
        {
            Assert.Equal(0, probe.ApiVersion);
            Assert.False(string.IsNullOrWhiteSpace(probe.Error));
        }
    }

    private static string GetExpectedNativeLibraryPath()
    {
        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "PixelEngine.UI.Native.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libPixelEngine.UI.Native.dylib"
                : "libPixelEngine.UI.Native.so";
        return Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            RuntimeInformation.RuntimeIdentifier,
            "native",
            fileName);
    }
}
