namespace PixelEngine.Scripting;

/// <summary>
/// 即时模式 GUI 绘制上下文；由 Hosting 或 Editor 层在 GUI 绘制相位提供具体实现。
/// </summary>
/// <remarks>
/// Scripting 层只定义脚本可见的 GUI 能力，不绑定 ImGui、Rendering 或 Editor 依赖。
/// </remarks>
public interface IGuiContext
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
    /// GUI 是否希望捕获鼠标输入；Hosting 可据此屏蔽世界输入。
    /// </summary>
    bool WantsMouse { get; }

    /// <summary>
    /// GUI 是否希望捕获键盘输入；Hosting 可据此屏蔽世界输入。
    /// </summary>
    bool WantsKeyboard { get; }

    /// <summary>
    /// 设置下一次 BeginWindow 使用的位置与尺寸。
    /// </summary>
    /// <param name="x">窗口左上角 X 坐标，单位像素。</param>
    /// <param name="y">窗口左上角 Y 坐标，单位像素。</param>
    /// <param name="width">窗口宽度，单位像素。</param>
    /// <param name="height">窗口高度，单位像素。</param>
    /// <param name="condition">应用条件。</param>
    void SetNextWindow(float x, float y, float width, float height, GuiCondition condition = GuiCondition.Always);

    /// <summary>
    /// 开始绘制一个 GUI 窗口。
    /// </summary>
    /// <param name="id">稳定窗口 id；用于区分同名窗口。</param>
    /// <param name="title">显示标题。</param>
    /// <param name="flags">窗口行为标志。</param>
    /// <returns>窗口未折叠时返回 true，调用方应继续绘制内容。</returns>
    bool BeginWindow(string id, string title, GuiWindowFlags flags = GuiWindowFlags.None);

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
    /// 使用 BGRA 颜色绘制一行文本。
    /// </summary>
    /// <param name="text">文本内容。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    void TextColored(string text, uint colorBgra);

    /// <summary>
    /// 后续控件与前一个控件放在同一行。
    /// </summary>
    void SameLine();

    /// <summary>
    /// 绘制分隔线。
    /// </summary>
    void Separator();

    /// <summary>
    /// 绘制按钮。
    /// </summary>
    /// <param name="label">按钮标签。</param>
    /// <returns>按钮在本帧被点击时返回 true。</returns>
    bool Button(string label);

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
    /// <param name="value01">进度值，会被 clamp 到 0..1。</param>
    /// <param name="label">可选覆盖文本。</param>
    void ProgressBar(float value01, string? label = null);

    /// <summary>
    /// 绘制颜色色块。
    /// </summary>
    /// <param name="id">稳定控件 id。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    /// <param name="size">边长，单位像素。</param>
    void ColorSwatch(string id, uint colorBgra, float size = 16f);
}

/// <summary>
/// GUI 窗口首次定位或调整尺寸的应用条件。
/// </summary>
public enum GuiCondition
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
/// 脚本可见 GUI 窗口行为标志。
/// </summary>
[Flags]
public enum GuiWindowFlags
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
}
