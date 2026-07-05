using PixelEngine.Gui;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 纯托管游戏 UI 后端，复用同一个 PixelEngine.Gui host 绘制 XHTML 子集控件。
/// </summary>
public sealed class ManagedFallbackBackend : IGameUiBackend, IManagedGuiDrawable
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

    /// <inheritdoc />
    public UiBackendKind Kind => UiBackendKind.ManagedFallback;

    /// <inheritdoc />
    public bool IsDirty => Dirty;

    /// <inheritdoc />
    public bool IsAnimating => false;

    /// <inheritdoc />
    public void Initialize(in UiBackendInitializeInfo info)
    {
        ThrowIfDisposed();
        info.Viewport.Validate();
        _initialized = true;
    }

    /// <inheritdoc />
    public void Resize(in UiViewport viewport)
    {
        ThrowIfDisposed();
        viewport.Validate();
        Dirty = true;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        _deltaSeconds = float.IsFinite(deltaSeconds) && deltaSeconds > 0f ? deltaSeconds : 1f / 60f;
    }

    /// <inheritdoc />
    public void FeedPointerMove(float x, float y)
    {
    }

    /// <inheritdoc />
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
    }

    /// <inheritdoc />
    public void FeedScroll(float deltaX, float deltaY)
    {
    }

    /// <inheritdoc />
    public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
    {
    }

    /// <inheritdoc />
    public void FeedText(ReadOnlySpan<char> text)
    {
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    /// <inheritdoc />
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
