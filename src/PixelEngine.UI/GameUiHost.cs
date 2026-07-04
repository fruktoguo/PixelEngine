namespace PixelEngine.UI;

/// <summary>
/// 游戏大 UI 顶层门面，负责屏栈管理并把生命周期调用转发给当前后端。
/// </summary>
public sealed class GameUiHost : IDisposable
{
    private readonly IGameUiBackend _backend;
    private readonly UiScreenStackEntry[] _screenStackBuffer;
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
    /// 当前后端是否需要光栅化或合成更新。
    /// </summary>
    public bool NeedsComposite => Options.Enabled && (_backend.IsDirty || _backend.IsAnimating);

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
        UiDocumentHandle document = _backend.LoadDocument(in source);
        Documents.Register(screenId, document, source.Kind);
        return document;
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
    /// 合成 UI 到当前渲染管线的 present 层。
    /// </summary>
    /// <param name="context">渲染管线提供的 UI present 上下文。</param>
    public void Composite(in PixelEngine.Rendering.UiPresentContext context)
    {
        ThrowIfDisposed();
        if (Options.Enabled && _initialized && NeedsComposite)
        {
            _backend.Composite(in context);
        }
    }

    /// <inheritdoc />
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
    }
}
