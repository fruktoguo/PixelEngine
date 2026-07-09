using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// Ultralight 可选高保真 UI 后端的未激活占位实现。
/// </summary>
public sealed class UltralightBackend : IGameUiBackend
{
    private bool _disposed;

    /// <summary>
    /// 当前 Ultralight profile 是否具备 native SDK、许可和发行证据并允许执行。
    /// </summary>
    public bool IsAvailable => UltralightOptionalProfileGate.IsActive;

    /// <summary>
    /// 当前 profile 未激活时的可审计失败原因。
    /// </summary>
    public string ActivationFailureReason => UltralightOptionalProfileGate.InactiveReason;

    /// <summary>
    /// 后端类型，固定为 Ultralight。
    /// </summary>
    public UiBackendKind Kind => UiBackendKind.Ultralight;

    /// <summary>
    /// 未激活 profile 不会产生待合成内容。
    /// </summary>
    public bool IsDirty => false;

    /// <summary>
    /// 未激活 profile 不会推进动画。
    /// </summary>
    public bool IsAnimating => false;

    /// <summary>
    /// 初始化 Ultralight 后端。当前 profile 未激活，调用会抛出明确诊断。
    /// </summary>
    /// <param name="info">初始化信息。</param>
    public void Initialize(in UiBackendInitializeInfo info)
    {
        _ = info;
        ThrowIfDisposed();
        ThrowInactive();
    }

    /// <summary>
    /// 调整 Ultralight surface 尺寸。当前 profile 未激活，调用会抛出明确诊断。
    /// </summary>
    /// <param name="viewport">目标 UI 视口。</param>
    public void Resize(in UiViewport viewport)
    {
        _ = viewport;
        ThrowIfDisposed();
        ThrowInactive();
    }

    /// <summary>
    /// 载入 Ultralight 文档。当前 profile 未激活，调用会抛出明确诊断。
    /// </summary>
    /// <param name="source">文档来源。</param>
    /// <returns>永不返回。</returns>
    public UiDocumentHandle LoadDocument(in UiDocumentSource source)
    {
        _ = source;
        ThrowIfDisposed();
        ThrowInactive();
        return default;
    }

    /// <summary>
    /// 卸载 Ultralight 文档；未激活 profile 没有已加载文档，因此为空操作。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    public void UnloadDocument(UiDocumentHandle document)
    {
        _ = document;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 同步可见屏栈；未激活 profile 不保留屏栈。
    /// </summary>
    /// <param name="stack">可见屏栈。</param>
    public void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack)
    {
        _ = stack;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 推进 UI；未激活 profile 不执行脚本、布局或动画。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt。</param>
    public void Update(float deltaSeconds)
    {
        _ = deltaSeconds;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 注入指针移动；未激活 profile 不捕获输入。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    public void FeedPointerMove(float x, float y)
    {
        _ = x;
        _ = y;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 注入指针按钮；未激活 profile 不捕获输入。
    /// </summary>
    /// <param name="button">按钮。</param>
    /// <param name="isDown">是否按下。</param>
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
        _ = button;
        _ = isDown;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 注入滚轮；未激活 profile 不捕获输入。
    /// </summary>
    /// <param name="deltaX">水平滚动量。</param>
    /// <param name="deltaY">垂直滚动量。</param>
    public void FeedScroll(float deltaX, float deltaY)
    {
        _ = deltaX;
        _ = deltaY;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 注入键盘按键；未激活 profile 不捕获输入。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
    public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
    {
        _ = key;
        _ = isDown;
        _ = modifiers;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 注入提交文本；未激活 profile 不消费文本。
    /// </summary>
    /// <param name="text">提交文本。</param>
    public void FeedText(ReadOnlySpan<char> text)
    {
        _ = text;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 注入 IME composition；未激活 profile 不消费预编辑文本。
    /// </summary>
    /// <param name="text">预编辑文本。</param>
    /// <param name="composition">预编辑状态。</param>
    public void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition)
    {
        _ = text;
        _ = composition;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 尝试读取 IME 几何；未激活 profile 不提供候选窗锚点。
    /// </summary>
    /// <param name="geometry">输出几何。</param>
    /// <returns>始终返回 false。</returns>
    public bool TryGetImeGeometry(out UiImeGeometry geometry)
    {
        ThrowIfDisposed();
        geometry = UiImeGeometry.None;
        return false;
    }

    /// <summary>
    /// 执行命中测试；未激活 profile 不命中 UI。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    /// <returns>始终返回空命中。</returns>
    public UiHitResult HitTest(float x, float y)
    {
        _ = x;
        _ = y;
        ThrowIfDisposed();
        return UiHitResult.None;
    }

    /// <summary>
    /// 写入模型值；未激活 profile 不保留模型。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="path">模型路径。</param>
    /// <param name="value">模型值。</param>
    public void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value)
    {
        _ = document;
        _ = path;
        _ = value;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 读取模型值；未激活 profile 没有模型数据。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="path">模型路径。</param>
    /// <param name="value">输出模型值。</param>
    /// <returns>始终返回 false。</returns>
    public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
    {
        _ = document;
        _ = path;
        ThrowIfDisposed();
        value = default;
        return false;
    }

    /// <summary>
    /// 复制模型路径；未激活 profile 没有模型路径。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>始终返回 0。</returns>
    public int CopyModelPaths(UiDocumentHandle document, Span<UiPathId> destination)
    {
        _ = document;
        _ = destination;
        ThrowIfDisposed();
        return 0;
    }

    /// <summary>
    /// 调用 UI action；未激活 profile 没有可执行 action。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="action">动作句柄。</param>
    /// <param name="payload">动作载荷。</param>
    /// <returns>始终返回 false。</returns>
    public bool InvokeAction(UiDocumentHandle document, UiActionId action, in UiValue payload)
    {
        _ = document;
        _ = action;
        _ = payload;
        ThrowIfDisposed();
        return false;
    }

    /// <summary>
    /// 抽取 UI 事件；未激活 profile 不产生事件。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>始终返回 0。</returns>
    public int DrainEvents(Span<UiEvent> destination)
    {
        _ = destination;
        ThrowIfDisposed();
        return 0;
    }

    /// <summary>
    /// 合成 UI 输出；未激活 profile 没有 surface，因此为空操作。
    /// </summary>
    /// <param name="context">UI present 上下文。</param>
    public void Composite(in UiPresentContext context)
    {
        _ = context;
        ThrowIfDisposed();
    }

    /// <summary>
    /// 标记后端已释放。
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowInactive()
    {
        throw new NotSupportedException(ActivationFailureReason);
    }
}
