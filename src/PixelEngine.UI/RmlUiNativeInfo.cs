using System.Runtime.InteropServices;

namespace PixelEngine.UI;

/// <summary>
/// RmlUi native 核探活信息。
/// </summary>
/// <param name="IsAvailable">native 库是否可加载。</param>
/// <param name="ApiVersion">PixelEngine UI native C ABI 版本。</param>
/// <param name="RmlUiVersion">链接的 RmlUi 版本。</param>
/// <param name="Error">不可用时的错误摘要。</param>
public readonly record struct RmlUiNativeProbe(bool IsAvailable, int ApiVersion, string? RmlUiVersion, string? Error);

/// <summary>
/// 查询 RmlUi native 核状态的冷路径工具。
/// </summary>
public static class RmlUiNativeInfo
{
    /// <summary>
    /// 尝试查询 native 核；缺库、入口缺失或架构不匹配时返回 false，不抛出。
    /// </summary>
    /// <param name="probe">探活结果。</param>
    /// <returns>native 核可用则返回 true。</returns>
    public static bool TryQuery(out RmlUiNativeProbe probe)
    {
        try
        {
            int apiVersion = RmlUiNative.GetApiVersion();
            string? rmlUiVersion = Marshal.PtrToStringUTF8(RmlUiNative.GetRmlUiVersionUtf8());
            probe = new RmlUiNativeProbe(
                IsAvailable: !string.IsNullOrWhiteSpace(rmlUiVersion),
                apiVersion,
                rmlUiVersion,
                Error: null);
            return probe.IsAvailable;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            probe = new RmlUiNativeProbe(false, 0, null, ex.GetType().Name);
            return false;
        }
    }
}
