using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// 游戏大 UI 顶层门面，负责屏栈管理并把生命周期调用转发给当前后端。
/// </summary>
public sealed class GameUiHost : IDisposable
{
    private readonly IGameUiBackend _backend;
    private readonly UiScreenStackEntry[] _screenStackBuffer;
    private int _presentationFrameCountdown;
    private bool _initialized;

    /// <summary>
    /// 创建游戏 UI 宿主。
    /// </summary>
    /// <param name="backend">实际 UI 后端。</param>
    /// <param name="options">宿主容量与开关。</param>
    public GameUiHost(IGameUiBackend backend, GameUiHostOptions options = default)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Options = options.Normalize();
        Documents = new UiDocumentManager(Options.MaxDocuments, Options.MaxStackDepth);
        _screenStackBuffer = new UiScreenStackEntry[Options.MaxStackDepth];
    }

    /// <summary>
    /// 宿主配置。
    /// </summary>
    public GameUiHostOptions Options { get; }

    /// <summary>
    /// 当前屏栈管理器。
    /// </summary>
    public UiDocumentManager Documents { get; }

    /// <summary>
    /// 当前宿主使用的 UI 后端类型。
    /// </summary>
    public UiBackendKind BackendKind => _backend.Kind;

    /// <summary>
    /// 当前后端是否需要光栅化或合成更新。
    /// </summary>
    public bool NeedsComposite => Options.Enabled && (_backend.IsDirty || _backend.IsAnimating);

    /// <summary>
    /// 当前 UI present 降频间隔；1 表示每个渲染帧都允许 paint/composite。
    /// </summary>
    public int PresentationIntervalFrames { get; private set; } = 1;

    /// <summary>
    /// 因 UI present 降频而跳过的 paint/composite 帧数。
    /// </summary>
    public long SkippedPresentationFrames { get; private set; }

    /// <summary>
    /// 最近一次实际执行 UI 后端绘制/光栅化的耗时，单位毫秒；静态无脏帧为 0。
    /// </summary>
    public double LastPaintMilliseconds { get; private set; }

    private bool Disposed { get; set; }

    /// <summary>
    /// 初始化后端。
    /// </summary>
    /// <param name="info">窗口与 DPI 信息。</param>
    public void Initialize(in UiBackendInitializeInfo info)
    {
        ThrowIfDisposed();
        if (!Options.Enabled || _initialized)
        {
            return;
        }

        _backend.Initialize(in info);
        _initialized = true;
    }

    /// <summary>
    /// 通知后端尺寸变化。
    /// </summary>
    /// <param name="viewport">新的 UI 视口。</param>
    public void Resize(in UiViewport viewport)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.Resize(in viewport);
        }
    }

    /// <summary>
    /// 载入一个 UI 文档但不改变可见屏栈。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id。</param>
    /// <param name="source">文档来源。</param>
    /// <returns>文档句柄。</returns>
    public UiDocumentHandle LoadDocument(UiScreenId screenId, in UiDocumentSource source)
    {
        ThrowIfDisposed();
        EnsureEnabled();
        screenId.Validate();
        if (Documents.TryGetDocument(screenId, out UiDocumentHandle existing))
        {
            return existing;
        }

        UiDocumentHandle document = _backend.LoadDocument(in source);
        Documents.Register(screenId, document, source.Kind);
        return document;
    }

    /// <summary>
    /// 预载一张 UI 图片资产；支持的后端会立即解码、上传或转换到自身缓存，不支持则返回 false。
    /// </summary>
    /// <param name="path">图片资产绝对路径。</param>
    /// <returns>后端实际消费预载请求则返回 true。</returns>
    public bool PreloadImage(string path)
    {
        ThrowIfDisposed();
        EnsureEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_backend is not IGameUiImagePreloader preloader)
        {
            return false;
        }

        preloader.PreloadImage(path);
        return true;
    }

    /// <summary>
    /// 显示一个普通屏幕；若文档尚未载入，会先载入。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id。</param>
    /// <param name="source">文档来源。</param>
    /// <returns>屏幕句柄。</returns>
    public UiScreenHandle ShowScreen(UiScreenId screenId, in UiDocumentSource source)
    {
        ThrowIfDisposed();
        EnsureEnabled();
        UiDocumentHandle document = Documents.TryGetDocument(screenId, out UiDocumentHandle existing)
            ? existing
            : LoadDocument(screenId, in source);
        UiScreenHandle handle = Documents.Show(screenId, document, modal: false);
        PublishScreenStack();
        return handle;
    }

    /// <summary>
    /// 压入一个模态屏幕；若文档尚未载入，会先载入。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id。</param>
    /// <param name="source">文档来源。</param>
    /// <returns>屏幕句柄。</returns>
    public UiScreenHandle PushModal(UiScreenId screenId, in UiDocumentSource source)
    {
        ThrowIfDisposed();
        EnsureEnabled();
        UiDocumentHandle document = Documents.TryGetDocument(screenId, out UiDocumentHandle existing)
            ? existing
            : LoadDocument(screenId, in source);
        UiScreenHandle handle = Documents.Show(screenId, document, modal: true);
        PublishScreenStack();
        return handle;
    }

    /// <summary>
    /// 隐藏指定屏幕。
    /// </summary>
    /// <param name="screen">屏幕句柄。</param>
    /// <returns>找到并隐藏则返回 true。</returns>
    public bool HideScreen(UiScreenHandle screen)
    {
        ThrowIfDisposed();
        if (!Options.Enabled || !Documents.Hide(screen))
        {
            return false;
        }

        PublishScreenStack();
        return true;
    }

    /// <summary>
    /// 查找可见屏幕对应的后端文档。
    /// </summary>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <param name="document">后端文档句柄。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetDocument(UiScreenHandle screen, out UiDocumentHandle document)
    {
        ThrowIfDisposed();
        if (Options.Enabled && Documents.TryGetDocument(screen, out document))
        {
            return true;
        }

        document = default;
        return false;
    }

    /// <summary>
    /// 查找文档当前对应的最上层可见屏幕。
    /// </summary>
    /// <param name="document">后端文档句柄。</param>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetVisibleScreen(UiDocumentHandle document, out UiScreenHandle screen)
    {
        ThrowIfDisposed();
        if (Options.Enabled && Documents.TryGetVisibleScreen(document, out screen))
        {
            return true;
        }

        screen = default;
        return false;
    }

    /// <summary>
    /// 弹出栈顶模态屏幕。
    /// </summary>
    /// <returns>弹出成功则返回 true。</returns>
    public bool PopModal()
    {
        ThrowIfDisposed();
        if (!Options.Enabled || !Documents.PopModal(out _))
        {
            return false;
        }

        PublishScreenStack();
        return true;
    }

    /// <summary>
    /// 推进 UI 逻辑与动画。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt，单位秒。</param>
    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.Update(deltaSeconds);
        }
    }

    /// <summary>
    /// 设置 UI present 降频间隔。仅影响 paint/composite，不影响 UI update、输入泵或事件 drain。
    /// </summary>
    /// <param name="intervalFrames">间隔帧数；1 表示不降频。</param>
    public void SetPresentationFrameInterval(int intervalFrames)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalFrames);
        if (PresentationIntervalFrames == intervalFrames)
        {
            return;
        }

        PresentationIntervalFrames = intervalFrames;
        _presentationFrameCountdown = 0;
    }

    /// <summary>
    /// 抽取 UI 到游戏的事件。
    /// </summary>
    /// <param name="destination">事件写入缓冲。</param>
    /// <returns>写入事件数量。</returns>
    public int DrainEvents(Span<UiEvent> destination)
    {
        ThrowIfDisposed();
        return Options.Enabled && _initialized ? _backend.DrainEvents(destination) : 0;
    }

    /// <summary>
    /// 写入指定屏幕文档的模型值。
    /// </summary>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <param name="path">模型路径。</param>
    /// <param name="value">写入值。</param>
    public void SetModelValue(UiScreenHandle screen, UiPathId path, in UiValue value)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized && Documents.TryGetDocument(screen, out UiDocumentHandle document))
        {
            _backend.SetModelValue(document, path, in value);
        }
    }

    /// <summary>
    /// 读取指定屏幕文档的模型值。
    /// </summary>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <param name="path">模型路径。</param>
    /// <param name="value">读出值。</param>
    /// <returns>读取成功则返回 true。</returns>
    public bool TryGetModelValue(UiScreenHandle screen, UiPathId path, out UiValue value)
    {
        ThrowIfDisposed();
        if (Options.Enabled &&
            _initialized &&
            Documents.TryGetDocument(screen, out UiDocumentHandle document) &&
            _backend.TryGetModelValue(document, path, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 复制指定可见屏幕文档声明的模型路径。
    /// </summary>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <param name="destination">路径写入缓冲。</param>
    /// <returns>写入路径数量。</returns>
    public int CopyModelPaths(UiScreenHandle screen, Span<UiPathId> destination)
    {
        ThrowIfDisposed();
        return Options.Enabled &&
            _initialized &&
            Documents.TryGetDocument(screen, out UiDocumentHandle document)
            ? _backend.CopyModelPaths(document, destination)
            : 0;
    }

    /// <summary>
    /// 调用指定屏幕上的 UI action。
    /// </summary>
    /// <param name="screen">可见屏幕句柄。</param>
    /// <param name="action">动作句柄。</param>
    /// <param name="payload">动作载荷。</param>
    /// <returns>找到并执行 action 则返回 true。</returns>
    public bool InvokeAction(UiScreenHandle screen, UiActionId action, in UiValue payload)
    {
        ThrowIfDisposed();
        return Options.Enabled &&
            _initialized &&
            Documents.TryGetDocument(screen, out UiDocumentHandle document) &&
            _backend.InvokeAction(document, action, in payload);
    }

    /// <summary>
    /// 注入指针移动。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    public void FeedPointerMove(float x, float y)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.FeedPointerMove(x, y);
        }
    }

    /// <summary>
    /// 注入指针按钮。
    /// </summary>
    /// <param name="button">按钮。</param>
    /// <param name="isDown">是否按下。</param>
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.FeedPointerButton(button, isDown);
        }
    }

    /// <summary>
    /// 注入滚轮。
    /// </summary>
    /// <param name="deltaX">水平滚动量。</param>
    /// <param name="deltaY">垂直滚动量。</param>
    public void FeedScroll(float deltaX, float deltaY)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.FeedScroll(deltaX, deltaY);
        }
    }

    /// <summary>
    /// 注入键盘按键。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
    public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.FeedKey(key, isDown, modifiers);
        }
    }

    /// <summary>
    /// 注入已提交文本输入。
    /// </summary>
    /// <param name="text">本帧已提交文本。</param>
    public void FeedText(ReadOnlySpan<char> text)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.FeedText(text);
        }
    }

    /// <summary>
    /// 注入平台 IME composition 预编辑状态；已提交文本不走此通道。
    /// </summary>
    /// <param name="text">当前预编辑文本；清除 composition 时为空。</param>
    /// <param name="composition">当前预编辑状态。</param>
    public void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized)
        {
            _backend.FeedTextComposition(text, in composition);
        }
    }

    /// <summary>
    /// 执行 UI 命中测试并返回输入捕获意图。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    /// <returns>UI 命中结果。</returns>
    public UiHitResult HitTest(float x, float y)
    {
        ThrowIfDisposed();
        return Options.Enabled && _initialized ? _backend.HitTest(x, y) : UiHitResult.None;
    }

    /// <summary>
    /// 合成 UI 到当前渲染管线的 present 层。
    /// </summary>
    /// <param name="context">渲染管线提供的 UI present 上下文。</param>
    public void Composite(in PixelEngine.Rendering.UiPresentContext context)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized && NeedsComposite && ShouldPresentThisFrame())
        {
            long started = Stopwatch.GetTimestamp();
            _backend.Composite(in context);
            LastPaintMilliseconds = ElapsedMilliseconds(started);
            context.Profiler?.RecordSub(FrameSubPhase.UiPaint, LastPaintMilliseconds);
            return;
        }

        LastPaintMilliseconds = 0;
    }

    /// <summary>
    /// 在既有 Gui frame 内绘制托管 UI；后端不支持时为空操作。
    /// </summary>
    /// <param name="gui">中性 Gui 绘制上下文。</param>
    public void DrawGui(IGuiDrawContext gui)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized && NeedsComposite && _backend is IManagedGuiDrawable drawable && ShouldPresentThisFrame())
        {
            long started = Stopwatch.GetTimestamp();
            drawable.DrawGui(gui);
            LastPaintMilliseconds = ElapsedMilliseconds(started);
            return;
        }

        LastPaintMilliseconds = 0;
    }

    /// <summary>
    /// 释放 UI 后端资源并阻止后续调用。
    /// </summary>
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        _backend.Dispose();
        Disposed = true;
    }

    private void EnsureEnabled()
    {
        if (!Options.Enabled)
        {
            throw new InvalidOperationException("Game UI 已禁用，不能载入或显示 UI 文档。");
        }
    }

    private void PublishScreenStack()
    {
        int count = Documents.CopyStack(_screenStackBuffer);
        _backend.SetScreenStack(_screenStackBuffer.AsSpan(0, count));
    }

    private bool ShouldPresentThisFrame()
    {
        if (PresentationIntervalFrames <= 1)
        {
            return true;
        }

        if (_presentationFrameCountdown > 0)
        {
            _presentationFrameCountdown--;
            SkippedPresentationFrames++;
            return false;
        }

        _presentationFrameCountdown = PresentationIntervalFrames - 1;
        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
    }

    private static double ElapsedMilliseconds(long started)
    {
        long elapsed = Stopwatch.GetTimestamp() - started;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }
}
