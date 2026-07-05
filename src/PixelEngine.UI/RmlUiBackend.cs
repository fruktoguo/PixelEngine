using System.Text;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 基于 RmlUi native 核的游戏大 UI 后端。
/// </summary>
public sealed unsafe class RmlUiBackend : IGameUiBackend
{
    private const int MaxStackTextBytes = 4096;
    private const int RmlKeyUnknown = 0;
    private const int RmlKeySpace = 1;
    private const int RmlKey0 = 2;
    private const int RmlKeyA = 12;
    private const int RmlKeyBackspace = 69;
    private const int RmlKeyTab = 70;
    private const int RmlKeyReturn = 72;
    private const int RmlKeyEscape = 81;
    private const int RmlKeyPageUp = 86;
    private const int RmlKeyPageDown = 87;
    private const int RmlKeyEnd = 88;
    private const int RmlKeyHome = 89;
    private const int RmlKeyLeft = 90;
    private const int RmlKeyUp = 91;
    private const int RmlKeyRight = 92;
    private const int RmlKeyDown = 93;
    private const int RmlKeyInsert = 98;
    private const int RmlKeyDelete = 99;
    private const int RmlModifierControl = 1 << 0;
    private const int RmlModifierShift = 1 << 1;
    private const int RmlModifierAlt = 1 << 2;
    private const int RmlModifierMeta = 1 << 3;

    private readonly RenderWindow _window;
    private readonly NativeDocument[] _documents;
    private readonly UiScreenStackEntry[] _visibleScreens;
    private UiKeyModifiers _lastModifiers;
    private IntPtr _renderer;
    private int _documentCount;
    private int _visibleScreenCount;
    private int _nextDocumentHandle = 1;
    private bool _disposed;

    /// <summary>
    /// 创建 RmlUi 后端。后端复用传入窗口的 OpenGL context，不创建新 context。
    /// </summary>
    /// <param name="window">已初始化的渲染窗口。</param>
    /// <param name="maxDocuments">最大文档数。</param>
    /// <param name="maxVisibleScreens">最大可见屏栈深度。</param>
    public RmlUiBackend(RenderWindow window, int maxDocuments = 64, int maxVisibleScreens = 32)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        if (maxDocuments <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDocuments));
        }

        if (maxVisibleScreens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVisibleScreens));
        }

        _documents = new NativeDocument[maxDocuments];
        _visibleScreens = new UiScreenStackEntry[maxVisibleScreens];
    }

    /// <inheritdoc />
    public UiBackendKind Kind => UiBackendKind.RmlUi;

    /// <inheritdoc />
    public bool IsDirty => Dirty || _visibleScreenCount > 0;

    /// <inheritdoc />
    public bool IsAnimating => false;

    private bool Dirty { get; set; }

    /// <inheritdoc />
    public void Initialize(in UiBackendInitializeInfo info)
    {
        ThrowIfDisposed();
        info.Viewport.Validate();
        if (!RmlUiGlBootstrap.TryLoad(_window, out _))
        {
            throw new InvalidOperationException("RmlUi native GL 函数表初始化失败。");
        }

        _renderer = RmlUiNative.CreateRenderer(info.Viewport.Width, info.Viewport.Height);
        if (_renderer == IntPtr.Zero)
        {
            throw new InvalidOperationException("RmlUi native renderer 创建失败。");
        }

        Dirty = true;
    }

    /// <inheritdoc />
    public void Resize(in UiViewport viewport)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        viewport.Validate();
        RmlUiNative.RendererSetViewport(_renderer, viewport.Width, viewport.Height);
        Dirty = true;
    }

    /// <inheritdoc />
    public UiDocumentHandle LoadDocument(in UiDocumentSource source)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (source.Kind != UiDocumentSourceKind.Asset)
        {
            throw new NotSupportedException("RmlUiBackend 当前只支持资产文档来源。");
        }

        if (_documentCount >= _documents.Length)
        {
            throw new InvalidOperationException("RmlUiBackend 文档容量已满。");
        }

        byte[] documentBytes = Encoding.UTF8.GetBytes(File.ReadAllText(source.Path));
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source.Path + '\0');
        IntPtr nativeDocument;
        fixed (byte* document = documentBytes)
        fixed (byte* sourceUrl = sourceBytes)
        {
            nativeDocument = RmlUiNative.LoadDocumentMemory(_renderer, document, documentBytes.Length, sourceUrl);
        }

        if (nativeDocument == IntPtr.Zero)
        {
            throw new InvalidDataException($"RmlUi 文档载入失败: {source.Path}");
        }

        UiDocumentHandle handle = new(_nextDocumentHandle++);
        _documents[_documentCount++] = new NativeDocument(handle, nativeDocument);
        Dirty = true;
        return handle;
    }

    /// <inheritdoc />
    public void UnloadDocument(UiDocumentHandle document)
    {
        ThrowIfDisposed();
        for (int i = 0; i < _documentCount; i++)
        {
            if (_documents[i].Handle != document)
            {
                continue;
            }

            RmlUiNative.DocumentClose(_documents[i].Native);
            _documents[i] = _documents[_documentCount - 1];
            _documents[--_documentCount] = default;
            Dirty = true;
            return;
        }
    }

    /// <inheritdoc />
    public void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack)
    {
        ThrowIfDisposed();
        if (stack.Length > _visibleScreens.Length)
        {
            throw new InvalidOperationException("RmlUiBackend 可见屏栈容量不足。");
        }

        for (int i = 0; i < _visibleScreenCount; i++)
        {
            if (!ContainsDocument(stack, _visibleScreens[i].Document) &&
                TryFindDocument(_visibleScreens[i].Document, out NativeDocument oldDocument))
            {
                RmlUiNative.DocumentHide(oldDocument.Native);
            }
        }

        stack.CopyTo(_visibleScreens);
        _visibleScreenCount = stack.Length;
        for (int i = 0; i < _visibleScreenCount; i++)
        {
            if (TryFindDocument(_visibleScreens[i].Document, out NativeDocument document))
            {
                RmlUiNative.DocumentShow(document.Native, _visibleScreens[i].Modal ? 1 : 0);
            }
        }

        Dirty = true;
    }

    /// <inheritdoc />
    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        RmlUiNative.Update(_renderer);
    }

    /// <inheritdoc />
    public void FeedPointerMove(float x, float y)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            return;
        }

        _ = RmlUiNative.ProcessMouseMove(
            _renderer,
            (int)MathF.Round(x),
            (int)MathF.Round(y),
            ToRmlModifiers(_lastModifiers));
        Dirty = true;
    }

    /// <inheritdoc />
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        _ = RmlUiNative.ProcessMouseButton(_renderer, (int)button, isDown ? 1 : 0, ToRmlModifiers(_lastModifiers));
        Dirty = true;
    }

    /// <inheritdoc />
    public void FeedScroll(float deltaX, float deltaY)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (!float.IsFinite(deltaX) || !float.IsFinite(deltaY))
        {
            return;
        }

        _ = RmlUiNative.ProcessMouseWheel(_renderer, deltaX, deltaY, ToRmlModifiers(_lastModifiers));
        Dirty = true;
    }

    /// <inheritdoc />
    public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        _lastModifiers = modifiers;
        int rmlKey = ToRmlKey(key);
        if (rmlKey == RmlKeyUnknown)
        {
            return;
        }

        _ = RmlUiNative.ProcessKey(_renderer, rmlKey, isDown ? 1 : 0, ToRmlModifiers(modifiers));
        Dirty = true;
    }

    /// <inheritdoc />
    public void FeedText(ReadOnlySpan<char> text)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (text.IsEmpty)
        {
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount > MaxStackTextBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "单帧 UI 文本输入超过 RmlUi 栈缓冲上限。");
        }

        Span<byte> utf8 = stackalloc byte[byteCount];
        int written = Encoding.UTF8.GetBytes(text, utf8);
        fixed (byte* pointer = utf8)
        {
            _ = RmlUiNative.ProcessTextUtf8(_renderer, pointer, written);
        }

        Dirty = true;
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
    }

    /// <inheritdoc />
    public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
    {
        ThrowIfDisposed();
        value = default;
        return false;
    }

    /// <inheritdoc />
    public int DrainEvents(Span<UiEvent> destination)
    {
        ThrowIfDisposed();
        return 0;
    }

    /// <inheritdoc />
    public void Composite(in UiPresentContext context)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (_visibleScreenCount == 0)
        {
            return;
        }

        RmlUiNative.Render(_renderer);
        Dirty = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = 0; i < _documentCount; i++)
        {
            RmlUiNative.DocumentClose(_documents[i].Native);
        }

        _documentCount = 0;
        if (_renderer != IntPtr.Zero)
        {
            RmlUiNative.DestroyRenderer(_renderer);
            _renderer = IntPtr.Zero;
        }

        _disposed = true;
    }

    private bool TryFindDocument(UiDocumentHandle handle, out NativeDocument document)
    {
        for (int i = 0; i < _documentCount; i++)
        {
            if (_documents[i].Handle == handle)
            {
                document = _documents[i];
                return true;
            }
        }

        document = default;
        return false;
    }

    private static bool ContainsDocument(ReadOnlySpan<UiScreenStackEntry> stack, UiDocumentHandle document)
    {
        for (int i = 0; i < stack.Length; i++)
        {
            if (stack[i].Document == document)
            {
                return true;
            }
        }

        return false;
    }

    private static int ToRmlModifiers(UiKeyModifiers modifiers)
    {
        int result = 0;
        if ((modifiers & UiKeyModifiers.Control) != 0)
        {
            result |= RmlModifierControl;
        }

        if ((modifiers & UiKeyModifiers.Shift) != 0)
        {
            result |= RmlModifierShift;
        }

        if ((modifiers & UiKeyModifiers.Alt) != 0)
        {
            result |= RmlModifierAlt;
        }

        if ((modifiers & UiKeyModifiers.Super) != 0)
        {
            result |= RmlModifierMeta;
        }

        return result;
    }

    private static int ToRmlKey(UiKey key)
    {
        int value = key.Value;
        return value switch
        {
            >= 48 and <= 57 => RmlKey0 + value - 48,
            >= 65 and <= 90 => RmlKeyA + value - 65,
            32 => RmlKeySpace,
            256 => RmlKeyEscape,
            257 => RmlKeyReturn,
            258 => RmlKeyTab,
            259 => RmlKeyBackspace,
            260 => RmlKeyInsert,
            261 => RmlKeyDelete,
            262 => RmlKeyRight,
            263 => RmlKeyLeft,
            264 => RmlKeyDown,
            265 => RmlKeyUp,
            266 => RmlKeyPageUp,
            267 => RmlKeyPageDown,
            268 => RmlKeyHome,
            269 => RmlKeyEnd,
            _ => RmlKeyUnknown,
        };
    }

    private void EnsureInitialized()
    {
        if (_renderer == IntPtr.Zero)
        {
            throw new InvalidOperationException("RmlUiBackend 尚未初始化。");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private readonly record struct NativeDocument(UiDocumentHandle Handle, IntPtr Native);
}
