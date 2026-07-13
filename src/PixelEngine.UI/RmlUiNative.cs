using System.Runtime.InteropServices;

namespace PixelEngine.UI;

/// <summary>
/// PixelEngine.UI.Native 的 source-generated P/Invoke 绑定。
/// </summary>
internal static unsafe partial class RmlUiNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeUiValue
    {
        internal int Kind;
        internal int Reserved;
        internal long Integer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeUiEvent
    {
        internal int Document;
        internal int Element;
        internal int Action;
        internal int ValueKind;
        internal long Integer;
        internal double Number;
    }

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
    /// 设置 native renderer shader profile：0=desktop GL3（#version 330），1=GLES3/ANGLE（#version 300 es）。
    /// 必须在 <see cref="CreateRenderer" /> 之前调用。
    /// </summary>
    /// <param name="profile">profile 枚举整型。</param>
    /// <returns>1=成功，0=非法 profile。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_set_renderer_profile")]
    internal static partial int SetRendererProfile(int profile);

    /// <summary>
    /// 读取当前 native renderer shader profile。
    /// </summary>
    /// <returns>0=desktop，1=GLES3/ANGLE。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_get_renderer_profile")]
    internal static partial int GetRendererProfile();

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
    /// 更新 RmlUi GL3 renderer 在默认 framebuffer 中的输出区域。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="x">默认 framebuffer 左下角原点下的 X 偏移。</param>
    /// <param name="y">默认 framebuffer 左下角原点下的 Y 偏移。</param>
    /// <param name="width">输出区域宽度。</param>
    /// <param name="height">输出区域高度。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_renderer_set_viewport_region")]
    internal static partial void RendererSetViewportRegion(IntPtr renderer, int x, int y, int width, int height);

    /// <summary>
    /// 分别更新 RmlUi logical layout viewport 与默认 framebuffer 输出区域。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="x">默认 framebuffer 左下角原点下的 X 偏移。</param>
    /// <param name="y">默认 framebuffer 左下角原点下的 Y 偏移。</param>
    /// <param name="renderWidth">presentation 输出像素宽度。</param>
    /// <param name="renderHeight">presentation 输出像素高度。</param>
    /// <param name="layoutWidth">RmlUi CSS layout 宽度。</param>
    /// <param name="layoutHeight">RmlUi CSS layout 高度。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_renderer_set_canvas_metrics")]
    internal static partial void RendererSetCanvasMetrics(
        IntPtr renderer,
        int x,
        int y,
        int renderWidth,
        int renderHeight,
        int layoutWidth,
        int layoutHeight);

    /// <summary>
    /// 向 RmlUi FontEngine 注册字体文件。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="fontPath">UTF-8 字体路径，需以 0 结尾。</param>
    /// <returns>成功返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_register_font_face")]
    internal static partial int RegisterFontFace(IntPtr renderer, byte* fontPath);

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
    /// 扫描文档 DOM 并建立 PixelEngine data-model / data-event 绑定。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="document">RmlUi 文档句柄。</param>
    /// <param name="documentHandle">托管 UI 文档句柄值。</param>
    /// <returns>成功返回 1；负数表示绑定错误。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_document_bind")]
    internal static partial int DocumentBind(IntPtr renderer, IntPtr document, int documentHandle);

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
    /// 清理文档绑定并关闭 RmlUi 文档。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="document">RmlUi 文档句柄。</param>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_document_close_bound")]
    internal static partial void DocumentCloseBound(IntPtr renderer, IntPtr document);

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

    /// <summary>
    /// 注入鼠标移动。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="x">UI x 坐标。</param>
    /// <param name="y">UI y 坐标。</param>
    /// <param name="modifiers">RmlUi 修饰键位。</param>
    /// <returns>被 UI 消费返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_process_mouse_move")]
    internal static partial int ProcessMouseMove(IntPtr renderer, int x, int y, int modifiers);

    /// <summary>
    /// 注入鼠标按钮边沿。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="button">按钮索引。</param>
    /// <param name="isDown">按下为 1，释放为 0。</param>
    /// <param name="modifiers">RmlUi 修饰键位。</param>
    /// <returns>被 UI 消费返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_process_mouse_button")]
    internal static partial int ProcessMouseButton(IntPtr renderer, int button, int isDown, int modifiers);

    /// <summary>
    /// 注入鼠标滚轮。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="deltaX">水平滚动量。</param>
    /// <param name="deltaY">垂直滚动量。</param>
    /// <param name="modifiers">RmlUi 修饰键位。</param>
    /// <returns>被 UI 消费返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_process_mouse_wheel")]
    internal static partial int ProcessMouseWheel(IntPtr renderer, float deltaX, float deltaY, int modifiers);

    /// <summary>
    /// 注入键盘按键边沿。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="key">RmlUi KeyIdentifier。</param>
    /// <param name="isDown">按下为 1，释放为 0。</param>
    /// <param name="modifiers">RmlUi 修饰键位。</param>
    /// <returns>被 UI 消费返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_process_key")]
    internal static partial int ProcessKey(IntPtr renderer, int key, int isDown, int modifiers);

    /// <summary>
    /// 注入 UTF-8 文本输入。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="text">UTF-8 文本。</param>
    /// <param name="textLength">字节数。</param>
    /// <returns>被 UI 消费返回 1。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_process_text_utf8")]
    internal static partial int ProcessTextUtf8(IntPtr renderer, byte* text, int textLength);

    /// <summary>
    /// 查询当前焦点文本输入控件的 caret rect / 候选锚点（UI 坐标）；无焦点时返回 0。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="caretX">caret 左上角 x。</param>
    /// <param name="caretY">caret 左上角 y。</param>
    /// <param name="caretWidth">caret 宽度。</param>
    /// <param name="caretHeight">caret 高度。</param>
    /// <param name="anchorX">候选窗锚点 x。</param>
    /// <param name="anchorY">候选窗锚点 y。</param>
    /// <returns>1=成功，0=无焦点或不可用。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_try_get_active_text_input_geometry")]
    internal static partial int TryGetActiveTextInputGeometry(
        IntPtr renderer,
        out float caretX,
        out float caretY,
        out float caretWidth,
        out float caretHeight,
        out float anchorX,
        out float anchorY);

    /// <summary>
    /// 更新或取消 RmlUi 焦点文本框的 IME 预编辑字符串；isActive=0 为取消。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="text">UTF-8 预编辑文本；取消时可为空。</param>
    /// <param name="textLength">字节数。</param>
    /// <param name="isActive">1=更新预编辑，0=取消。</param>
    /// <param name="cursorIndex">预编辑内光标字符索引。</param>
    /// <param name="selectionStart">预编辑内选区起点。</param>
    /// <param name="selectionLength">预编辑内选区长度。</param>
    /// <returns>1=已应用到焦点上下文，0=无焦点或未应用。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_set_text_composition")]
    internal static partial int SetTextComposition(
        IntPtr renderer,
        byte* text,
        int textLength,
        int isActive,
        int cursorIndex,
        int selectionStart,
        int selectionLength);

    /// <summary>
    /// 确认当前预编辑为最终提交字符串；未在 composing 时返回 0，调用方应走 ProcessTextUtf8。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="text">UTF-8 提交文本。</param>
    /// <param name="textLength">字节数。</param>
    /// <returns>1=已确认 composition，0=未在 composing 或无焦点。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_confirm_text_composition")]
    internal static partial int ConfirmTextComposition(IntPtr renderer, byte* text, int textLength);

    /// <summary>
    /// 查询 native 是否正处于 composition 预编辑中。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <returns>1=正在 composing。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_is_text_composition_active")]
    internal static partial int IsTextCompositionActive(IntPtr renderer);

    /// <summary>
    /// 对当前 RmlUi context 执行 DOM 命中测试。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="x">UI x 坐标。</param>
    /// <param name="y">UI y 坐标。</param>
    /// <returns>bit0=命中元素，bit1=鼠标正在交互，bit2=存在键盘焦点。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_hit_test")]
    internal static partial int HitTest(IntPtr renderer, float x, float y);

    /// <summary>
    /// 设置已绑定 DOM 元素的模型值。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="documentHandle">托管 UI 文档句柄值。</param>
    /// <param name="pathHash">模型路径稳定 hash。</param>
    /// <param name="value">blittable UI 值。</param>
    /// <returns>1=成功，0=未找到，负数=错误。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_set_model_value")]
    internal static partial int SetModelValue(IntPtr renderer, int documentHandle, int pathHash, NativeUiValue* value);

    /// <summary>
    /// 设置已绑定 DOM 元素的字符串句柄模型值，并提供解析后的 UTF-8 文本。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="documentHandle">托管 UI 文档句柄值。</param>
    /// <param name="pathHash">模型路径稳定 hash。</param>
    /// <param name="value">blittable UI 值，Kind 必须为 StringHandle。</param>
    /// <param name="text">解析后的 UTF-8 文本。</param>
    /// <param name="textLength">文本字节数。</param>
    /// <returns>1=成功，0=未找到，负数=错误。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_set_model_string_value")]
    internal static partial int SetModelStringValue(
        IntPtr renderer,
        int documentHandle,
        int pathHash,
        NativeUiValue* value,
        byte* text,
        int textLength);

    /// <summary>
    /// 读取已绑定 DOM 元素的模型值。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="documentHandle">托管 UI 文档句柄值。</param>
    /// <param name="pathHash">模型路径稳定 hash。</param>
    /// <param name="value">输出 blittable UI 值。</param>
    /// <returns>1=成功，0=未找到，负数=错误。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_try_get_model_value")]
    internal static partial int TryGetModelValue(IntPtr renderer, int documentHandle, int pathHash, NativeUiValue* value);

    /// <summary>
    /// 复制指定文档已绑定的模型路径 hash。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="documentHandle">托管 UI 文档句柄值。</param>
    /// <param name="paths">输出路径缓冲。</param>
    /// <param name="capacity">缓冲容量。</param>
    /// <returns>写入路径数量。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_copy_model_paths")]
    internal static partial int CopyModelPaths(IntPtr renderer, int documentHandle, int* paths, int capacity);

    /// <summary>
    /// 调用已绑定 DOM action，并把载荷应用到对应 DOM 元素。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="documentHandle">托管 UI 文档句柄值。</param>
    /// <param name="actionHash">动作稳定 hash。</param>
    /// <param name="value">blittable UI 值。</param>
    /// <returns>1=成功，0=未找到，负数=错误。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_invoke_action")]
    internal static partial int InvokeAction(IntPtr renderer, int documentHandle, int actionHash, NativeUiValue* value);

    /// <summary>
    /// 拉取 RmlUi 事件队列。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <param name="events">输出事件缓冲。</param>
    /// <param name="capacity">缓冲容量。</param>
    /// <returns>写入事件数。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_drain_events")]
    internal static partial int DrainEvents(IntPtr renderer, NativeUiEvent* events, int capacity);

    /// <summary>
    /// 获取 native 最近错误文本 UTF-8 指针。
    /// </summary>
    /// <param name="renderer">renderer 句柄。</param>
    /// <returns>UTF-8 字符串指针，native 持有。</returns>
    [LibraryImport(RmlUiNativeLibrary.Name, EntryPoint = "peui_native_get_last_error")]
    internal static partial IntPtr GetLastErrorUtf8(IntPtr renderer);
}
