using System.Runtime.InteropServices;

namespace PixelEngine.UI;

/// <summary>
/// PixelEngine.UI.Native 的 source-generated P/Invoke 绑定。
/// </summary>
internal static unsafe partial class RmlUiNative
{
    /// <summary>
    /// 查询 native C ABI 版本。
    /// </summary>
    /// <returns>API 版本。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_get_api_version")]
    internal static partial int GetApiVersion();

    /// <summary>
    /// 查询链接的 RmlUi 版本 UTF-8 指针；指针由 native 持有。
    /// </summary>
    /// <returns>UTF-8 字符串指针。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_get_rmlui_version")]
    internal static partial IntPtr GetRmlUiVersionUtf8();
}
