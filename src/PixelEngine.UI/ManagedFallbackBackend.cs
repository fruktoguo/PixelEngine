using PixelEngine.Gui;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 纯托管游戏 UI 后端，复用同一个 PixelEngine.Gui host 绘制 XHTML 子集控件。
/// </summary>
public sealed class ManagedFallbackBackend : IGameUiBackend, IManagedGuiDrawable, IGameUiImagePreloader
{
    private readonly IManagedFallbackGuiHost _gui;
    private readonly ManagedUiDocument?[] _documents;
    private readonly UiScreenStackEntry[] _visibleScreens;
    private readonly UiEvent[] _events;
    private readonly Action<IGuiDrawContext> _drawCallback;
    private int _documentCount;
    private int _nextDocumentHandle = 1;
    private int _visibleScreenCount;
    private int _eventRead;
    private int _eventCount;
    private float _deltaSeconds = 1f / 60f;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// 创建纯托管 UI 回退后端。
    /// </summary>
    /// <param name="gui">要复用的中性 Gui host。</param>
    /// <param name="options">容量配置。</param>
    public ManagedFallbackBackend(IManagedFallbackGuiHost gui, ManagedFallbackBackendOptions options = default)
    {
        _gui = gui ?? throw new ArgumentNullException(nameof(gui));
        Options = options.Normalize();
        _documents = new ManagedUiDocument[Options.MaxDocuments];
        _visibleScreens = new UiScreenStackEntry[Options.MaxVisibleScreens];
        _events = new UiEvent[Options.EventCapacity];
        _drawCallback = DrawVisibleScreens;
    }

    private ManagedFallbackBackendOptions Options { get; }

    private bool Dirty { get; set; }

    /// <summary>
    /// 后端类型，固定为纯托管回退后端。
    /// </summary>
    public UiBackendKind Kind => UiBackendKind.ManagedFallback;

    /// <summary>
    /// 当前托管 UI 是否需要重新绘制。
    /// </summary>
    public bool IsDirty => Dirty;

    /// <summary>
    /// 纯托管回退后端当前不维护独立动画时间线。
    /// </summary>
    public bool IsAnimating => false;

    /// <summary>
    /// 初始化纯托管回退后端并校验视口。
    /// </summary>
    /// <param name="info">后端初始化信息。</param>
    public void Initialize(in UiBackendInitializeInfo info)
    {
        ThrowIfDisposed();
        info.Viewport.Validate();
        _initialized = true;
    }

    /// <summary>
    /// 响应 UI 视口变化并标记需要重绘。
    /// </summary>
    /// <param name="viewport">新的 UI 视口。</param>
    public void Resize(in UiViewport viewport)
    {
        ThrowIfDisposed();
        viewport.Validate();
        Dirty = true;
    }

    /// <summary>
    /// 载入 XHTML 子集文档并返回托管文档句柄。
    /// </summary>
    /// <param name="source">文档来源。</param>
    /// <returns>托管文档句柄。</returns>
    public UiDocumentHandle LoadDocument(in UiDocumentSource source)
    {
        ThrowIfDisposed();
        if (_documentCount >= _documents.Length)
        {
            throw new InvalidOperationException("ManagedFallbackBackend 文档容量已满。");
        }

        UiDocumentHandle handle = new(_nextDocumentHandle++);
        ManagedUiDocument document = ManagedUiLayout.Load(handle, in source, Options.MaxControlsPerDocument);
        _documents[_documentCount++] = document;
        Dirty = true;
        return handle;
    }

    void IGameUiImagePreloader.PreloadImage(string path)
    {
        ThrowIfDisposed();
        _ = _gui.LoadImage(Path.GetFullPath(path));
    }

    /// <summary>
    /// 卸载指定托管 UI 文档。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    public void UnloadDocument(UiDocumentHandle document)
    {
        ThrowIfDisposed();
        for (int i = 0; i < _documentCount; i++)
        {
            if (_documents[i]?.Handle == document)
            {
                _documents[i] = _documents[_documentCount - 1];
                _documents[--_documentCount] = null;
                Dirty = true;
                return;
            }
        }
    }

    /// <summary>
    /// 同步当前可见屏栈。
    /// </summary>
    /// <param name="stack">按底到顶排列的屏栈。</param>
    public void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack)
    {
        ThrowIfDisposed();
        if (stack.Length > _visibleScreens.Length)
        {
            throw new InvalidOperationException("ManagedFallbackBackend 可见屏栈容量不足。");
        }

        stack.CopyTo(_visibleScreens);
        _visibleScreenCount = stack.Length;
        Dirty = true;
    }

    /// <summary>
    /// 更新托管 UI 的渲染帧 dt。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt，单位秒。</param>
    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        _deltaSeconds = float.IsFinite(deltaSeconds) && deltaSeconds > 0f ? deltaSeconds : 1f / 60f;
    }

    /// <summary>
    /// 接收指针移动；当前托管回退后端通过 Gui host 处理具体控件交互。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    public void FeedPointerMove(float x, float y)
    {
    }

    /// <summary>
    /// 接收指针按钮；当前托管回退后端通过 Gui host 处理具体控件交互。
    /// </summary>
    /// <param name="button">指针按钮。</param>
    /// <param name="isDown">是否按下。</param>
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
    }

    /// <summary>
    /// 接收滚轮输入；当前托管回退后端通过 Gui host 处理具体控件交互。
    /// </summary>
    /// <param name="deltaX">水平滚动量。</param>
    /// <param name="deltaY">垂直滚动量。</param>
    public void FeedScroll(float deltaX, float deltaY)
    {
    }

    /// <summary>
    /// 接收键盘按键；当前托管回退后端通过 Gui host 处理具体控件交互。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
    public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
    {
    }

    /// <summary>
    /// 接收已提交文本；当前托管回退后端通过 Gui host 处理具体控件交互。
    /// </summary>
    /// <param name="text">本帧文本。</param>
    public void FeedText(ReadOnlySpan<char> text)
    {
    }

    /// <summary>
    /// 接收 IME composition 预编辑状态；当前托管回退后端尚无文本编辑控件，安全忽略但保留抽象边界。
    /// </summary>
    /// <param name="text">当前预编辑文本。</param>
    /// <param name="composition">当前预编辑状态。</param>
    public void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition)
    {
        _ = text;
        _ = composition;
    }

    /// <summary>
    /// 根据当前屏栈返回托管 UI 的输入捕获意图。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
    /// <returns>命中与捕获结果。</returns>
    public UiHitResult HitTest(float x, float y)
    {
        ThrowIfDisposed();
        if (_visibleScreenCount == 0)
        {
            return UiHitResult.None;
        }

        bool modal = _visibleScreens[_visibleScreenCount - 1].Modal;
        return new UiHitResult(HitsUi: true, Opaque: modal, WantsMouse: modal, WantsKeyboard: modal);
    }

    /// <summary>
    /// 写入托管控件绑定的模型值。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="path">模型路径句柄。</param>
    /// <param name="value">写入值。</param>
    public void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value)
    {
        ThrowIfDisposed();
        ManagedUiControl? control = FindControl(document, path);
        if (control is null)
        {
            return;
        }

        control.Value = value;
        Dirty = true;
    }

    /// <summary>
    /// 读取托管控件绑定的模型值。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="path">模型路径句柄。</param>
    /// <param name="value">读出的值。</param>
    /// <returns>找到模型路径则返回 true。</returns>
    public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
    {
        ThrowIfDisposed();
        ManagedUiControl? control = FindControl(document, path);
        if (control is null)
        {
            value = default;
            return false;
        }

        value = control.Value;
        return true;
    }

    /// <summary>
    /// 复制指定托管文档声明的模型路径。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="destination">路径写入缓冲。</param>
    /// <returns>写入路径数量。</returns>
    public int CopyModelPaths(UiDocumentHandle document, Span<UiPathId> destination)
    {
        ThrowIfDisposed();
        ManagedUiDocument? uiDocument = FindDocument(document);
        if (uiDocument is null || destination.IsEmpty)
        {
            return 0;
        }

        int written = 0;
        for (int i = 0; i < uiDocument.Controls.Length && written < destination.Length; i++)
        {
            UiPathId path = uiDocument.Controls[i].Path;
            if (path.Value <= 0 || ContainsPath(destination[..written], path))
            {
                continue;
            }

            destination[written++] = path;
        }

        return written;
    }

    /// <summary>
    /// 调用托管控件 action 并把载荷应用到匹配控件。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="action">动作句柄。</param>
    /// <param name="payload">动作载荷。</param>
    /// <returns>找到匹配 action 则返回 true。</returns>
    public bool InvokeAction(UiDocumentHandle document, UiActionId action, in UiValue payload)
    {
        ThrowIfDisposed();
        ManagedUiDocument? uiDocument = FindDocument(document);
        if (uiDocument is null || action.Value <= 0)
        {
            return false;
        }

        bool invoked = false;
        for (int i = 0; i < uiDocument.Controls.Length; i++)
        {
            ManagedUiControl control = uiDocument.Controls[i];
            if (control.Action != action)
            {
                continue;
            }

            control.Value = payload;
            invoked = true;
        }

        Dirty |= invoked;
        return invoked;
    }

    /// <summary>
    /// 抽取托管 UI 产生的事件。
    /// </summary>
    /// <param name="destination">事件写入缓冲。</param>
    /// <returns>写入事件数量。</returns>
    public int DrainEvents(Span<UiEvent> destination)
    {
        ThrowIfDisposed();
        int written = Math.Min(destination.Length, _eventCount);
        for (int i = 0; i < written; i++)
        {
            destination[i] = _events[_eventRead];
            _eventRead = (_eventRead + 1) % _events.Length;
        }

        _eventCount -= written;
        return written;
    }

    /// <summary>
    /// 通过复用的 Gui host 绘制当前屏栈。
    /// </summary>
    /// <param name="context">UI present 上下文。</param>
    public void Composite(in UiPresentContext context)
    {
        ThrowIfDisposed();
        if (!_initialized || _visibleScreenCount == 0)
        {
            return;
        }

        if (!_gui.IsRunning)
        {
            _gui.Initialize();
        }

        _gui.DrawFrame(_deltaSeconds, context.FramebufferWidth, context.FramebufferHeight, _drawCallback);
        Dirty = false;
    }

    /// <summary>
    /// 标记后端已释放。
    /// </summary>
    public void Dispose()
    {
        if (_gui is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// 在已有 Gui frame 内绘制托管 UI 控件。
    /// </summary>
    /// <param name="gui">GUI 绘制上下文。</param>
    public void DrawGui(IGuiDrawContext gui)
    {
        ThrowIfDisposed();
        if (!_initialized || _visibleScreenCount == 0)
        {
            return;
        }

        DrawVisibleScreens(gui);
        Dirty = false;
    }

    private void DrawVisibleScreens(IGuiDrawContext gui)
    {
        for (int i = 0; i < _visibleScreenCount; i++)
        {
            ManagedUiDocument? document = FindDocument(_visibleScreens[i].Document);
            if (document is null)
            {
                continue;
            }

            DrawDocument(gui, _visibleScreens[i], document);
        }
    }

    private void DrawDocument(IGuiDrawContext gui, UiScreenStackEntry screen, ManagedUiDocument document)
    {
        GuiDrawWindowFlags flags = GuiDrawWindowFlags.NoSavedSettings;
        if (document.RootBox.HasPositionAndSize)
        {
            gui.SetNextWindow(
                document.RootBox.X!.Value,
                document.RootBox.Y!.Value,
                document.RootBox.Width!.Value,
                document.RootBox.Height!.Value,
                GuiDrawCondition.Always);
            flags |= GuiDrawWindowFlags.NoResize | GuiDrawWindowFlags.NoMove;
        }

        if (screen.Modal)
        {
            flags |= GuiDrawWindowFlags.AlwaysAutoResize;
        }

        if (!gui.BeginWindow($"ui_{screen.Handle.Value}", document.Title, flags))
        {
            gui.EndWindow();
            return;
        }

        for (int i = 0; i < document.Controls.Length; i++)
        {
            DrawControl(gui, screen, document.Controls[i]);
        }

        gui.EndWindow();
    }

    private void DrawControl(IGuiDrawContext gui, UiScreenStackEntry screen, ManagedUiControl control)
    {
        switch (control.Kind)
        {
            case ManagedUiControlKind.Text:
                gui.Text(control.Text);
                break;
            case ManagedUiControlKind.Button:
                if (gui.Button(control.Text))
                {
                    Enqueue(new UiEvent(screen.Document, control.Element, control.Action, default));
                }

                break;
            case ManagedUiControlKind.Checkbox:
                bool checkedValue = control.Value.Kind == UiValueKind.Boolean && control.Value.AsBoolean();
                if (gui.Checkbox(control.Text, ref checkedValue))
                {
                    control.Value = UiValue.FromBoolean(checkedValue);
                    Enqueue(new UiEvent(screen.Document, control.Element, control.Action, control.Value));
                }

                break;
            case ManagedUiControlKind.Progress:
                double progress = control.Value.Kind == UiValueKind.Double ? control.Value.AsDouble() : 0.0;
                gui.ProgressBar((float)Math.Clamp(progress, 0.0, 1.0), string.IsNullOrEmpty(control.Text) ? null : control.Text);
                break;
            case ManagedUiControlKind.Image:
                ManagedFallbackImage image = _gui.LoadImage(control.ImagePath);
                image.Validate();
                gui.Image(
                    control.Id,
                    image.TextureHandle,
                    image.Width,
                    image.Height,
                    control.DisplayWidth > 0f ? control.DisplayWidth : image.Width,
                    control.DisplayHeight > 0f ? control.DisplayHeight : image.Height);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(control), control.Kind, "未知 Managed UI 控件类型。");
        }
    }

    private ManagedUiControl? FindControl(UiDocumentHandle document, UiPathId path)
    {
        ManagedUiDocument? managedDocument = FindDocument(document);
        if (managedDocument is null)
        {
            return null;
        }

        for (int i = 0; i < managedDocument.Controls.Length; i++)
        {
            ManagedUiControl control = managedDocument.Controls[i];
            if (control.Path == path)
            {
                return control;
            }
        }

        return null;
    }

    private ManagedUiDocument? FindDocument(UiDocumentHandle document)
    {
        for (int i = 0; i < _documentCount; i++)
        {
            if (_documents[i]?.Handle == document)
            {
                return _documents[i];
            }
        }

        return null;
    }

    private void Enqueue(in UiEvent uiEvent)
    {
        if (_eventCount == _events.Length)
        {
            _eventRead = (_eventRead + 1) % _events.Length;
            _eventCount--;
        }

        int write = (_eventRead + _eventCount) % _events.Length;
        _events[write] = uiEvent;
        _eventCount++;
    }

    private static bool ContainsPath(ReadOnlySpan<UiPathId> paths, UiPathId path)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == path)
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
