namespace PixelEngine.Gui;

/// <summary>
/// 中性即时模式 GUI 绘制上下文，供非脚本程序集复用同一 Gui host。
/// </summary>
public interface IGuiDrawContext
{
    /// <summary>
    /// 当前 framebuffer 宽度，单位像素。
    /// </summary>
    int Width { get; }

    /// <summary>
    /// 当前 framebuffer 高度，单位像素。
    /// </summary>
    int Height { get; }

    /// <summary>
    /// GUI 帧间隔，单位秒。
    /// </summary>
    float DeltaTime { get; }

    /// <summary>
    /// GUI 是否希望捕获鼠标输入。
    /// </summary>
    bool WantsMouse { get; }

    /// <summary>
    /// GUI 是否希望捕获键盘输入。
    /// </summary>
    bool WantsKeyboard { get; }

    /// <summary>
    /// 设置下一次 BeginWindow 使用的位置与尺寸。
    /// </summary>
    /// <param name="x">窗口左上角 X 坐标。</param>
    /// <param name="y">窗口左上角 Y 坐标。</param>
    /// <param name="width">窗口宽度。</param>
    /// <param name="height">窗口高度。</param>
    /// <param name="condition">应用条件。</param>
    void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always);

    /// <summary>
    /// 开始绘制一个 GUI 窗口。
    /// </summary>
    /// <param name="id">稳定窗口 id。</param>
    /// <param name="title">显示标题。</param>
    /// <param name="flags">窗口行为标志。</param>
    /// <returns>窗口未折叠时返回 true。</returns>
    bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None);

    /// <summary>
    /// 压入当前窗口的完整 Canvas 相对缩放；字体、padding、spacing、圆角和控件度量必须使用同一比例。
    /// 不支持的后端可忽略，但必须与 <see cref="PopCanvasScale" /> 成对。
    /// </summary>
    /// <param name="scale">有限正缩放。</param>
    void PushCanvasScale(float scale)
    {
    }

    /// <summary>
    /// 结束最近一次 <see cref="PushCanvasScale" />；不支持的后端可忽略。
    /// </summary>
    void PopCanvasScale()
    {
    }

    /// <summary>
    /// 结束当前 GUI 窗口。
    /// </summary>
    void EndWindow();

    /// <summary>
    /// 绘制一行文本。
    /// </summary>
    /// <param name="text">文本内容。</param>
    void Text(string text);

    /// <summary>
    /// 绘制一行 span 文本；支持的后端应直接消费该视图以避免动态字符串分配。
    /// </summary>
    /// <param name="text">文本内容；仅需在本次调用期间有效。</param>
    void Text(ReadOnlySpan<char> text)
    {
        Text(text.ToString());
    }

    /// <summary>
    /// 使用 BGRA 颜色绘制一行文本。
    /// </summary>
    /// <param name="text">文本内容。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    void TextColored(string text, uint colorBgra);

    /// <summary>
    /// 使用 BGRA 颜色绘制一行 span 文本。
    /// </summary>
    /// <param name="text">文本内容；仅需在本次调用期间有效。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    void TextColored(ReadOnlySpan<char> text, uint colorBgra)
    {
        TextColored(text.ToString(), colorBgra);
    }

    /// <summary>
    /// 后续控件与前一个控件放在同一行。
    /// </summary>
    void SameLine();

    /// <summary>
    /// 绘制分隔线。
    /// </summary>
    void Separator();

    /// <summary>
    /// 设置当前窗口内下一个控件的局部绘制位置；不支持的后端可忽略。
    /// </summary>
    /// <param name="x">窗口局部 X 坐标。</param>
    /// <param name="y">窗口局部 Y 坐标。</param>
    void SetCursor(float x, float y)
    {
    }

    /// <summary>
    /// 在当前布局中增加垂直留白；不支持的后端可忽略。
    /// </summary>
    /// <param name="height">留白高度，单位像素。</param>
    void AddVerticalSpacing(float height)
    {
    }

    /// <summary>
    /// 绘制按钮。
    /// </summary>
    /// <param name="label">按钮标签。</param>
    /// <returns>按钮在本帧被点击时返回 true。</returns>
    bool Button(string label);

    /// <summary>
    /// 绘制指定尺寸的按钮；不支持尺寸的后端可退回普通按钮。
    /// </summary>
    /// <param name="label">按钮标签。</param>
    /// <param name="width">显示宽度。</param>
    /// <param name="height">显示高度。</param>
    /// <returns>按钮在本帧被点击时返回 true。</returns>
    bool Button(string label, float width, float height)
    {
        _ = width;
        _ = height;
        return Button(label);
    }

    /// <summary>
    /// 绘制复选框并在变化时回写值。
    /// </summary>
    /// <param name="label">复选框标签。</param>
    /// <param name="value">当前值。</param>
    /// <returns>值在本帧变化时返回 true。</returns>
    bool Checkbox(string label, ref bool value);

    /// <summary>
    /// 绘制 0..1 进度条。
    /// </summary>
    /// <param name="value01">进度值。</param>
    /// <param name="label">可选覆盖文本。</param>
    void ProgressBar(float value01, string? label = null);

    /// <summary>
    /// 绘制 0..1 进度条，并使用 span 作为覆盖文本。
    /// </summary>
    /// <param name="value01">进度值。</param>
    /// <param name="label">覆盖文本；仅需在本次调用期间有效。</param>
    void ProgressBar(float value01, ReadOnlySpan<char> label)
    {
        ProgressBar(value01, label.ToString());
    }

    /// <summary>
    /// 绘制指定尺寸的 0..1 进度条；不支持尺寸的后端可退回默认进度条。
    /// </summary>
    /// <param name="value01">进度值。</param>
    /// <param name="label">可选覆盖文本。</param>
    /// <param name="width">显示宽度。</param>
    /// <param name="height">显示高度。</param>
    void ProgressBar(float value01, string? label, float width, float height)
    {
        _ = width;
        _ = height;
        ProgressBar(value01, label);
    }

    /// <summary>
    /// 绘制颜色色块。
    /// </summary>
    /// <param name="id">稳定控件 id。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    /// <param name="size">边长，单位像素。</param>
    void ColorSwatch(string id, uint colorBgra, float size = 16f);

    /// <summary>
    /// 绘制颜色色块，并使用 span 提供稳定控件 id。
    /// </summary>
    /// <param name="id">稳定控件 id；仅需在本次调用期间有效。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    /// <param name="size">边长，单位像素。</param>
    void ColorSwatch(ReadOnlySpan<char> id, uint colorBgra, float size = 16f)
    {
        ColorSwatch(id.ToString(), colorBgra, size);
    }

    /// <summary>
    /// 绘制一张已上传到当前 GL 上下文的 2D 图片纹理。
    /// </summary>
    /// <param name="id">稳定控件 id。</param>
    /// <param name="textureHandle">OpenGL Texture2D 句柄。</param>
    /// <param name="textureWidth">纹理原始宽度。</param>
    /// <param name="textureHeight">纹理原始高度。</param>
    /// <param name="width">显示宽度，单位像素。</param>
    /// <param name="height">显示高度，单位像素。</param>
    /// <param name="tintBgra">BGRA 颜色乘色。</param>
    void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 0xFF_FF_FF_FF);
}

/// <summary>
/// GUI 窗口首次定位或调整尺寸的应用条件。
/// </summary>
public enum GuiDrawCondition
{
    /// <summary>
    /// 每帧都应用。
    /// </summary>
    Always,

    /// <summary>
    /// 仅窗口首次出现时应用。
    /// </summary>
    FirstUseEver,
}

/// <summary>
/// 中性 GUI 窗口行为标志。
/// </summary>
[Flags]
public enum GuiDrawWindowFlags
{
    /// <summary>
    /// 默认窗口行为。
    /// </summary>
    None = 0,

    /// <summary>
    /// 不显示标题栏。
    /// </summary>
    NoTitleBar = 1 << 0,

    /// <summary>
    /// 禁止调整尺寸。
    /// </summary>
    NoResize = 1 << 1,

    /// <summary>
    /// 禁止移动。
    /// </summary>
    NoMove = 1 << 2,

    /// <summary>
    /// 自动适配内容尺寸。
    /// </summary>
    AlwaysAutoResize = 1 << 3,

    /// <summary>
    /// 不保存窗口位置与尺寸。
    /// </summary>
    NoSavedSettings = 1 << 4,

    /// <summary>
    /// 不显示背景。
    /// </summary>
    NoBackground = 1 << 5,

    /// <summary>
    /// 禁止滚动条。
    /// </summary>
    NoScrollbar = 1 << 6,

    /// <summary>
    /// 窗口不参与鼠标命中与捕获。
    /// </summary>
    NoMouseInputs = 1 << 7,

    /// <summary>
    /// 窗口不消费键盘/手柄导航输入。
    /// </summary>
    NoNavInputs = 1 << 8,

    /// <summary>
    /// 窗口不能成为导航焦点。
    /// </summary>
    NoNavFocus = 1 << 9,

    /// <summary>
    /// 纯展示窗口：鼠标与导航输入全部穿透。
    /// </summary>
    NoInputs = NoMouseInputs | NoNavInputs | NoNavFocus,
}
