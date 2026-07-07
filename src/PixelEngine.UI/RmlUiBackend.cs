using System.Text;
using System.Runtime.InteropServices;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 基于 RmlUi native 核的游戏大 UI 后端。
/// </summary>
public sealed unsafe class RmlUiBackend : IGameUiBackend, IGameUiImagePreloader
{
    private const int MaxStackTextBytes = 4096;
    private const int MaxDrainEvents = 256;
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
    private const int HitTestElement = 1;
    private const int HitTestMouseInteracting = 1 << 1;
    private const int HitTestKeyboardFocus = 1 << 2;

    private readonly RenderWindow _window;
    private readonly NativeDocument[] _documents;
    private readonly UiScreenStackEntry[] _visibleScreens;
    private readonly RmlUiImageAssetCache _imageCache = new();
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

    /// <summary>
    /// 后端类型，固定为 RmlUi native 后端。
    /// </summary>
    public UiBackendKind Kind => UiBackendKind.RmlUi;

    /// <summary>
    /// RmlUi 文档或输入状态是否需要重新合成。
    /// </summary>
    public bool IsDirty => Dirty;

    /// <summary>
    /// RmlUi 后端当前不暴露独立动画时间线。
    /// </summary>
    public bool IsAnimating => false;

    private bool Dirty { get; set; }

    /// <summary>
    /// 初始化 RmlUi renderer、GL 函数表与字体。
    /// </summary>
    /// <param name="info">后端初始化信息。</param>
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

        RegisterFontFace(info.FontSelection);

        Dirty = true;
    }

    /// <summary>
    /// 调整 RmlUi native renderer 的视口。
    /// </summary>
    /// <param name="viewport">新的 UI 视口。</param>
    public void Resize(in UiViewport viewport)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        viewport.Validate();
        RmlUiNative.RendererSetViewport(_renderer, viewport.Width, viewport.Height);
        Dirty = true;
    }

    /// <summary>
    /// 从资产路径载入 RmlUi 文档并绑定 DOM 桥。
    /// </summary>
    /// <param name="source">文档来源。</param>
    /// <returns>文档句柄。</returns>
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

        string documentText = RmlUiDocumentPreprocessor.Prepare(source.Path, _imageCache);
        byte[] documentBytes = Encoding.UTF8.GetBytes(documentText);
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
        int bindResult = RmlUiNative.DocumentBind(_renderer, nativeDocument, handle.Value);
        if (bindResult <= 0)
        {
            string error = ReadNativeError();
            RmlUiNative.DocumentCloseBound(_renderer, nativeDocument);
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(error)
                    ? $"RmlUi 文档 DOM 绑定失败: {source.Path}"
                    : $"RmlUi 文档 DOM 绑定失败: {source.Path}: {error}");
        }

        _documents[_documentCount++] = new NativeDocument(handle, nativeDocument);
        Dirty = true;
        return handle;
    }

    void IGameUiImagePreloader.PreloadImage(string path)
    {
        ThrowIfDisposed();
        _ = _imageCache.ConvertPngToTga(path);
    }

    /// <summary>
    /// 关闭并卸载 RmlUi 文档。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    public void UnloadDocument(UiDocumentHandle document)
    {
        ThrowIfDisposed();
        for (int i = 0; i < _documentCount; i++)
        {
            if (_documents[i].Handle != document)
            {
                continue;
            }

            RmlUiNative.DocumentCloseBound(_renderer, _documents[i].Native);
            _documents[i] = _documents[_documentCount - 1];
            _documents[--_documentCount] = default;
            _visibleScreenCount = PruneVisibleScreensForDocument(_visibleScreens, _visibleScreenCount, document);
            Dirty = true;
            return;
        }
    }

    /// <summary>
    /// 同步当前可见屏栈到 RmlUi 文档显示状态。
    /// </summary>
    /// <param name="stack">按底到顶排列的屏栈。</param>
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

    /// <summary>
    /// 推进 RmlUi 文档更新。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt，当前 native shim 只需要相位推进。</param>
    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        RmlUiNative.Update(_renderer);
    }

    /// <summary>
    /// 把指针移动注入 RmlUi。
    /// </summary>
    /// <param name="x">UI 坐标 x。</param>
    /// <param name="y">UI 坐标 y。</param>
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

    /// <summary>
    /// 把指针按钮状态注入 RmlUi。
    /// </summary>
    /// <param name="button">指针按钮。</param>
    /// <param name="isDown">是否按下。</param>
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        _ = RmlUiNative.ProcessMouseButton(_renderer, (int)button, isDown ? 1 : 0, ToRmlModifiers(_lastModifiers));
        Dirty = true;
    }

    /// <summary>
    /// 把滚轮输入注入 RmlUi。
    /// </summary>
    /// <param name="deltaX">水平滚动量。</param>
    /// <param name="deltaY">垂直滚动量。</param>
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

    /// <summary>
    /// 把键盘按键注入 RmlUi。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
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

    /// <summary>
    /// 把已提交文本按 UTF-8 注入 RmlUi。
    /// </summary>
    /// <param name="text">本帧文本。</param>
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

    /// <summary>
    /// 接收 IME composition 预编辑状态；当前 RmlUi native shim 尚未暴露 composition API，安全忽略但不把 committed text 冒充 composition。
    /// </summary>
    /// <param name="text">当前预编辑文本。</param>
    /// <param name="composition">当前预编辑状态。</param>
    public void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        _ = text;
        _ = composition;
    }

    /// <summary>
    /// 通过 native DOM 命中测试返回输入捕获意图。
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

        EnsureInitialized();
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            return UiHitResult.None;
        }

        if (!TryGetTopLoadedVisibleScreen(out UiScreenStackEntry topVisibleScreen))
        {
            return UiHitResult.None;
        }

        int flags = RmlUiNative.HitTest(_renderer, x, y);
        bool hitsElement = (flags & HitTestElement) != 0;
        bool wantsMouse = hitsElement || (flags & HitTestMouseInteracting) != 0;
        bool wantsKeyboard = (flags & HitTestKeyboardFocus) != 0;
        bool modal = topVisibleScreen.Modal;
        return new UiHitResult(
            HitsUi: hitsElement || modal,
            Opaque: modal,
            WantsMouse: wantsMouse || modal,
            WantsKeyboard: wantsKeyboard || modal);
    }

    /// <summary>
    /// 写入 RmlUi DOM 数据桥模型值。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="path">模型路径句柄。</param>
    /// <param name="value">写入值。</param>
    public void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        document.Validate();
        ValidatePath(path);
        if (!TryFindDocument(document, out _))
        {
            throw new KeyNotFoundException($"RmlUi 文档不存在: {document.Value}");
        }

        RmlUiNative.NativeUiValue nativeValue = ToNativeValue(in value);
        int result = RmlUiNative.SetModelValue(_renderer, document.Value, path.Value, &nativeValue);
        if (result == 0)
        {
            throw new KeyNotFoundException($"RmlUi 文档 {document.Value} 未绑定 UI 模型路径: {path.Value}");
        }

        if (result < 0)
        {
            ThrowNativeBridgeError("设置 RmlUi UI 模型值失败。");
        }

        Dirty = true;
    }

    /// <summary>
    /// 从 RmlUi DOM 数据桥读取模型值。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="path">模型路径句柄。</param>
    /// <param name="value">读出的值。</param>
    /// <returns>找到模型路径则返回 true。</returns>
    public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        document.Validate();
        ValidatePath(path);
        if (!TryFindDocument(document, out _))
        {
            value = default;
            return false;
        }

        RmlUiNative.NativeUiValue nativeValue = default;
        int result = RmlUiNative.TryGetModelValue(_renderer, document.Value, path.Value, &nativeValue);
        if (result < 0)
        {
            ThrowNativeBridgeError("读取 RmlUi UI 模型值失败。");
        }

        if (result == 1)
        {
            value = ToUiValue(in nativeValue);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 复制 RmlUi 文档声明的模型路径。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="destination">路径写入缓冲。</param>
    /// <returns>写入路径数量。</returns>
    public int CopyModelPaths(UiDocumentHandle document, Span<UiPathId> destination)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        document.Validate();
        if (destination.IsEmpty || !TryFindDocument(document, out _))
        {
            return 0;
        }

        Span<int> paths = stackalloc int[Math.Min(destination.Length, MaxDrainEvents)];
        int count;
        fixed (int* pointer = paths)
        {
            count = RmlUiNative.CopyModelPaths(_renderer, document.Value, pointer, paths.Length);
        }

        for (int i = 0; i < count; i++)
        {
            destination[i] = new UiPathId(paths[i]);
        }

        return count;
    }

    /// <summary>
    /// 调用 RmlUi DOM action 并应用载荷。
    /// </summary>
    /// <param name="document">文档句柄。</param>
    /// <param name="action">动作句柄。</param>
    /// <param name="payload">动作载荷。</param>
    /// <returns>找到并执行 action 则返回 true。</returns>
    public bool InvokeAction(UiDocumentHandle document, UiActionId action, in UiValue payload)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        document.Validate();
        ValidateAction(action);
        if (!TryFindDocument(document, out _))
        {
            return false;
        }

        RmlUiNative.NativeUiValue nativeValue = ToNativeValue(in payload);
        int result = RmlUiNative.InvokeAction(_renderer, document.Value, action.Value, &nativeValue);
        if (result == 0)
        {
            return false;
        }

        if (result < 0)
        {
            ThrowNativeBridgeError("调用 RmlUi UI action 失败。");
        }

        Dirty = true;
        return true;
    }

    /// <summary>
    /// 从 native 事件环形缓冲抽取 UI 事件。
    /// </summary>
    /// <param name="destination">事件写入缓冲。</param>
    /// <returns>写入事件数量。</returns>
    public int DrainEvents(Span<UiEvent> destination)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (destination.IsEmpty)
        {
            return 0;
        }

        int capacity = Math.Min(destination.Length, MaxDrainEvents);
        Span<RmlUiNative.NativeUiEvent> nativeEvents = stackalloc RmlUiNative.NativeUiEvent[capacity];
        int count;
        fixed (RmlUiNative.NativeUiEvent* events = nativeEvents)
        {
            count = RmlUiNative.DrainEvents(_renderer, events, capacity);
        }

        for (int i = 0; i < count; i++)
        {
            ref readonly RmlUiNative.NativeUiEvent nativeEvent = ref nativeEvents[i];
            destination[i] = new UiEvent(
                new UiDocumentHandle(nativeEvent.Document),
                new UiElementId(nativeEvent.Element),
                new UiActionId(nativeEvent.Action),
                ToUiValue(nativeEvent.ValueKind, nativeEvent.Integer, nativeEvent.Number));
        }

        return count;
    }

    /// <summary>
    /// 在当前共享 GL context 中渲染 RmlUi 文档。
    /// </summary>
    /// <param name="context">UI present 上下文。</param>
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

    /// <summary>
    /// 关闭全部 RmlUi 文档并销毁 native renderer。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = 0; i < _documentCount; i++)
        {
            RmlUiNative.DocumentCloseBound(_renderer, _documents[i].Native);
        }

        _documentCount = 0;
        if (_renderer != IntPtr.Zero)
        {
            RmlUiNative.DestroyRenderer(_renderer);
            _renderer = IntPtr.Zero;
        }

        _imageCache.Dispose();
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

    internal static int PruneVisibleScreensForDocument(
        UiScreenStackEntry[] visibleScreens,
        int visibleScreenCount,
        UiDocumentHandle document)
    {
        if ((uint)visibleScreenCount > (uint)visibleScreens.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleScreenCount), "RmlUiBackend 可见屏栈计数越界。");
        }

        int write = 0;
        for (int read = 0; read < visibleScreenCount; read++)
        {
            UiScreenStackEntry screen = visibleScreens[read];
            if (screen.Document == document)
            {
                continue;
            }

            visibleScreens[write++] = screen;
        }

        for (int i = write; i < visibleScreenCount; i++)
        {
            visibleScreens[i] = default;
        }

        return write;
    }

    private bool TryGetTopLoadedVisibleScreen(out UiScreenStackEntry screen)
    {
        for (int i = _visibleScreenCount - 1; i >= 0; i--)
        {
            UiScreenStackEntry candidate = _visibleScreens[i];
            if (TryFindDocument(candidate.Document, out _))
            {
                screen = candidate;
                return true;
            }
        }

        screen = default;
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

    private static void ValidatePath(UiPathId path)
    {
        if (path.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(path), "UI 模型路径 id 必须为正数。");
        }
    }

    private static void ValidateAction(UiActionId action)
    {
        if (action.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(action), "UI action id 必须为正数。");
        }
    }

    private static RmlUiNative.NativeUiValue ToNativeValue(in UiValue value)
    {
        return value.Kind switch
        {
            UiValueKind.Empty => default,
            UiValueKind.Boolean => new RmlUiNative.NativeUiValue
            {
                Kind = (int)UiValueKind.Boolean,
                Integer = value.AsBoolean() ? 1 : 0,
            },
            UiValueKind.Int64 => new RmlUiNative.NativeUiValue
            {
                Kind = (int)UiValueKind.Int64,
                Integer = value.AsInt64(),
            },
            UiValueKind.Double => new RmlUiNative.NativeUiValue
            {
                Kind = (int)UiValueKind.Double,
                Integer = BitConverter.DoubleToInt64Bits(value.AsDouble()),
            },
            UiValueKind.StringHandle => throw new NotSupportedException("RmlUi DOM 数据桥尚未接入真实字符串池，不能设置 StringHandle。"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), "未知 UI 值类型。"),
        };
    }

    private static UiValue ToUiValue(in RmlUiNative.NativeUiValue value)
    {
        return ToUiValue(value.Kind, value.Integer, BitConverter.Int64BitsToDouble(value.Integer));
    }

    private static UiValue ToUiValue(int kind, long integer, double number)
    {
        return (UiValueKind)kind switch
        {
            UiValueKind.Empty => default,
            UiValueKind.Boolean => UiValue.FromBoolean(integer != 0),
            UiValueKind.Int64 => new UiValue(integer),
            UiValueKind.Double => new UiValue(number),
            UiValueKind.StringHandle => UiValue.FromStringHandle(new UiStringHandle(checked((int)integer))),
            _ => default,
        };
    }

    private string ReadNativeError()
    {
        IntPtr pointer = RmlUiNative.GetLastErrorUtf8(_renderer);
        return pointer == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringUTF8(pointer) ?? string.Empty);
    }

    private void RegisterFontFace(UiFontSelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.FontPath))
        {
            return;
        }

        byte[] pathBytes = Encoding.UTF8.GetBytes(selection.FontPath + '\0');
        int result;
        fixed (byte* fontPath = pathBytes)
        {
            result = RmlUiNative.RegisterFontFace(_renderer, fontPath);
        }

        if (result <= 0)
        {
            string error = ReadNativeError();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"RmlUi 字体注册失败: {selection.FontPath}"
                    : $"RmlUi 字体注册失败: {selection.FontPath}: {error}");
        }
    }

    private void ThrowNativeBridgeError(string fallback)
    {
        string error = ReadNativeError();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? fallback : $"{fallback} {error}");
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
