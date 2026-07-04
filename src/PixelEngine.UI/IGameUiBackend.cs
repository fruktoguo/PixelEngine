using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 游戏大 UI 后端抽象，统一纯托管、RmlUi 与 Ultralight 等实现。
/// </summary>
public interface IGameUiBackend : IDisposable
{
    /// <summary>
    /// 后端种类。
    /// </summary>
    UiBackendKind Kind { get; }

    /// <summary>
    /// 后端是否存在待重绘或待合成的脏内容。
    /// </summary>
    bool IsDirty { get; }

    /// <summary>
    /// 后端是否有正在推进的 UI 动画。
    /// </summary>
    bool IsAnimating { get; }

    /// <summary>
    /// 初始化后端。
    /// </summary>
    /// <param name="info">初始化参数。</param>
    void Initialize(in UiBackendInitializeInfo info);

    /// <summary>
    /// 调整后端视口尺寸。
    /// </summary>
    /// <param name="viewport">新的 UI 视口。</param>
    void Resize(in UiViewport viewport);

    /// <summary>
    /// 载入 UI 文档。
    /// </summary>
    /// <param name="source">文档来源。</param>
    /// <returns>文档句柄。</returns>
    UiDocumentHandle LoadDocument(in UiDocumentSource source);

    /// <summary>
    /// 卸载 UI 文档。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    void UnloadDocument(UiDocumentHandle document);

    /// <summary>
    /// 同步当前可见屏栈；后端只能绘制此栈中的文档。
    /// </summary>
    /// <param name="stack">按底到顶排列的可见屏栈。</param>
    void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack);

    /// <summary>
    /// 推进后端逻辑、布局与动画。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt，单位秒。</param>
    void Update(float deltaSeconds);

    /// <summary>
    /// 注入指针移动。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    void FeedPointerMove(float x, float y);

    /// <summary>
    /// 注入指针按钮。
    /// </summary>
    /// <param name="button">按钮。</param>
    /// <param name="isDown">是否按下。</param>
    void FeedPointerButton(UiPointerButton button, bool isDown);

    /// <summary>
    /// 注入滚轮。
    /// </summary>
    /// <param name="deltaX">水平滚动量。</param>
    /// <param name="deltaY">垂直滚动量。</param>
    void FeedScroll(float deltaX, float deltaY);

    /// <summary>
    /// 注入键盘按键。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
    void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers);

    /// <summary>
    /// 注入文本输入。
    /// </summary>
    /// <param name="text">本帧输入文本。</param>
    void FeedText(ReadOnlySpan<char> text);

    /// <summary>
    /// 命中测试并返回输入捕获意图。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    /// <returns>命中结果。</returns>
    UiHitResult HitTest(float x, float y);

    /// <summary>
    /// 写入模型值。
    /// </summary>
    /// <param name="document">目标文档。</param>
    /// <param name="path">模型路径句柄。</param>
    /// <param name="value">写入值。</param>
    void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value);

    /// <summary>
    /// 读取模型值。
    /// </summary>
    /// <param name="document">目标文档。</param>
    /// <param name="path">模型路径句柄。</param>
    /// <param name="value">读出的值。</param>
    /// <returns>读取成功则返回 true。</returns>
    bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value);

    /// <summary>
    /// 抽取 UI 到游戏的事件。
    /// </summary>
    /// <param name="destination">事件写入缓冲。</param>
    /// <returns>写入事件数量。</returns>
    int DrainEvents(Span<UiEvent> destination);

    /// <summary>
    /// 合成后端输出到渲染管线 UI 层。
    /// </summary>
    /// <param name="context">UI present 上下文。</param>
    void Composite(in UiPresentContext context);
}
