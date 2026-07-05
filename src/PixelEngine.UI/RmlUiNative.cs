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

    /// <summary>
    /// 用宿主提供的 OpenGL resolver 初始化 native GL 函数表。
    /// </summary>
    /// <param name="resolver">函数入口 resolver。</param>
    /// <param name="user">resolver 用户数据。</param>
    /// <param name="major">加载到的 GL 主版本。</param>
    /// <param name="minor">加载到的 GL 次版本。</param>
    /// <returns>成功返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_load_gl")]
    internal static partial int LoadGl(
        delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr> resolver,
        IntPtr user,
        out int major,
        out int minor);

    /// <summary>
    /// 创建 RmlUi GL3 renderer 句柄。
    /// </summary>
    /// <param name="width">默认 framebuffer 宽度。</param>
    /// <param name="height">默认 framebuffer 高度。</param>
    /// <returns>renderer 句柄，失败返回 0。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_create_renderer")]
    internal static partial IntPtr CreateRenderer(int width, int height);

    /// <summary>
    /// 销毁 RmlUi GL3 renderer 句柄。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_destroy_renderer")]
    internal static partial void DestroyRenderer(IntPtr renderer);

    /// <summary>
    /// 更新 RmlUi GL3 renderer 视口。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="width">默认 framebuffer 宽度。</param>
    /// <param name="height">默认 framebuffer 高度。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_renderer_set_viewport")]
    internal static partial void RendererSetViewport(IntPtr renderer, int width, int height);

    /// <summary>
    /// 从 UTF-8 内存载入 RmlUi 文档。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="document">UTF-8 文档内容。</param>
    /// <param name="documentLength">文档字节数。</param>
    /// <param name="sourceUrl">UTF-8 source URL，需以 0 结尾。</param>
    /// <returns>RmlUi 文档句柄。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_load_document_memory")]
    internal static partial IntPtr LoadDocumentMemory(IntPtr renderer, byte* document, int documentLength, byte* sourceUrl);

    /// <summary>
    /// 显示 RmlUi 文档。
    /// </summary>
    /// <param name="document">RmlUi 文档句柄。</param>
    /// <param name="modal">是否模态。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_document_show")]
    internal static partial void DocumentShow(IntPtr document, int modal);

    /// <summary>
    /// 隐藏 RmlUi 文档。
    /// </summary>
    /// <param name="document">RmlUi 文档句柄。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_document_hide")]
    internal static partial void DocumentHide(IntPtr document);

    /// <summary>
    /// 关闭 RmlUi 文档。
    /// </summary>
    /// <param name="document">RmlUi 文档句柄。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_document_close")]
    internal static partial void DocumentClose(IntPtr document);

    /// <summary>
    /// 推进 RmlUi context。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_update")]
    internal static partial void Update(IntPtr renderer);

    /// <summary>
    /// 渲染 RmlUi context。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_render")]
    internal static partial void Render(IntPtr renderer);
}
