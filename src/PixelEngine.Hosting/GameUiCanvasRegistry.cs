using PixelEngine.Gui;
using PixelEngine.Rendering;
using RuntimeUi = PixelEngine.UI;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 当前场景的固定容量 Web Canvas 运行时注册表。每个 Canvas 拥有独立 host、文档栈、模型桥与输入状态，
/// 脚本屏幕句柄在所有 Canvas 间全局唯一，并在重新物化场景后失效。
/// </summary>
public sealed class GameUiCanvasRegistry :
    RuntimeUi.IGameUiInputTarget,
    RuntimeUi.IGameUiPresentationTarget,
    IGameUiModelPusher,
    IDisposable
{
    private readonly string _contentRoot;
    private readonly string _defaultUiRoot;
    private readonly Func<EngineSceneCanvasDefinition, RuntimeUi.GameUiHost> _hostFactory;
    private readonly Func<string, string?>? _manifestAssetResolver;
    private readonly int _maxBindingsPerCanvas;
    private readonly int _maxPathsPerScreen;
    private CanvasSlot?[] _slots;
    private CanvasSlot?[] _stagingSlots;
    private readonly ScreenBinding[] _screenBindings;
    private readonly PendingScreenBinding[] _pendingScreenBindings;
    private int _screenBindingCount;
    private int _nextCanvasHandle;
    private int _nextScreenHandle;
    private int _pointerTargetIndex = -1;
    private int _keyboardTargetIndex = -1;
    private int _pointerCaptureIndex = -1;
    private int _pressedPointerButtons;
    private float _pointerX;
    private float _pointerY;
    private long _pointerButtonCalls;
    private long _forwardedPointerButtonCalls;
    private long _leftPressCalls;
    private long _leftReleaseCalls;
    private int _lastButtonTargetIndex = -1;
    private int _lastButtonTargetCanvas;
    private RuntimeUi.UiBackendKind _lastButtonTargetBackend;
    private RuntimeUi.UiHitResult _lastButtonTargetHit;
    private bool _disposed;

    /// <summary>
    /// 创建固定容量 Canvas 注册表。
    /// </summary>
    /// <param name="contentRoot">引擎 content 根目录。</param>
    /// <param name="hostFactory">按 Canvas 定义创建且完成 Initialize 的独立 host 工厂。</param>
    /// <param name="stringPool">所有 Canvas 与脚本共享的稳定字符串池。</param>
    /// <param name="manifestAssetResolver">可选 stable asset id 到 manifest 文件路径的解析器。</param>
    /// <param name="maxCanvases">单场景已启用 Canvas 上限。</param>
    /// <param name="maxScreenInstances">跨 Canvas 同时可见的屏幕实例上限。</param>
    /// <param name="maxBindingsPerCanvas">单 Canvas 模型绑定上限。</param>
    /// <param name="maxPathsPerScreen">单屏每帧模型路径推送上限。</param>
    public GameUiCanvasRegistry(
        string contentRoot,
        Func<EngineSceneCanvasDefinition, RuntimeUi.GameUiHost> hostFactory,
        RuntimeUi.UiStringPool? stringPool = null,
        Func<string, string?>? manifestAssetResolver = null,
        int maxCanvases = 16,
        int maxScreenInstances = 512,
        int maxBindingsPerCanvas = 64,
        int maxPathsPerScreen = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(hostFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCanvases);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxScreenInstances);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBindingsPerCanvas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPathsPerScreen);

        _contentRoot = NormalizeDirectory(contentRoot);
        _defaultUiRoot = NormalizeDirectory(Path.Combine(_contentRoot, "ui"));
        _hostFactory = hostFactory;
        _manifestAssetResolver = manifestAssetResolver;
        _maxBindingsPerCanvas = maxBindingsPerCanvas;
        _maxPathsPerScreen = maxPathsPerScreen;
        _slots = new CanvasSlot[maxCanvases];
        _stagingSlots = new CanvasSlot[maxCanvases];
        _screenBindings = new ScreenBinding[maxScreenInstances];
        _pendingScreenBindings = new PendingScreenBinding[maxCanvases];
        Strings = stringPool ?? new RuntimeUi.UiStringPool();
    }

    /// <summary>脚本与所有 Canvas 后端共享的 UI 字符串池。</summary>
    public RuntimeUi.UiStringPool Strings { get; }

    /// <summary>当前场景已物化的 Canvas 数量。</summary>
    public int Count { get; private set; }

    /// <summary>最近一帧所有 Canvas 后端 paint 耗时之和。</summary>
    public double LastPaintMilliseconds
    {
        get
        {
            ThrowIfDisposed();
            double total = 0d;
            for (int i = 0; i < Count; i++)
            {
                total += GetRequiredSlot(i).Host.LastPaintMilliseconds;
            }

            return total;
        }
    }

    /// <summary>当前 Canvas 共用的 UI presentation 间隔；无 Canvas 时为 0。</summary>
    public int PresentationIntervalFrames => Count > 0
        ? GetRequiredSlot(FindPrimarySlotIndex()).Host.PresentationIntervalFrames
        : 0;

    /// <summary>所有 Canvas 实际跳过的 paint 帧累计值。</summary>
    public long SkippedPresentationFrames
    {
        get
        {
            ThrowIfDisposed();
            long total = 0;
            for (int i = 0; i < Count; i++)
            {
                total += GetRequiredSlot(i).Host.SkippedPresentationFrames;
            }

            return total;
        }
    }

    /// <summary>当前 primary Canvas；全部显式 Canvas disabled 时返回默认句柄。</summary>
    public ScriptUi.UiCanvasHandle PrimaryCanvas
    {
        get
        {
            ThrowIfDisposed();
            int index = FindPrimarySlotIndex();
            return index >= 0 ? GetRequiredSlot(index).Handle : default;
        }
    }

    /// <summary>
    /// 原子物化一个场景解析出的 Canvas 集合。任何 host、manifest 或初始屏幕失败时保留旧场景运行态。
    /// </summary>
    /// <param name="canvasSet">已校验且按合成顺序排序的 Canvas 集合。</param>
    public void Configure(EngineSceneCanvasSet canvasSet)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(canvasSet);
        if (canvasSet.Count > _stagingSlots.Length)
        {
            throw new InvalidOperationException(
                $"场景启用了 {canvasSet.Count} 个 Web Canvas，超过固定容量 {_stagingSlots.Length}。");
        }

        int stagedCount = 0;
        int pendingCount = 0;
        try
        {
            ReadOnlySpan<EngineSceneCanvasDefinition> definitions = canvasSet.Canvases;
            for (int i = 0; i < definitions.Length; i++)
            {
                EngineSceneCanvasDefinition definition = definitions[i];
                RuntimeUi.GameUiHost host = _hostFactory(definition) ??
                    throw new InvalidOperationException("Canvas host 工厂返回了 null。");
                bool hostStored = false;
                try
                {
                    // 工厂必须完成 Initialize；这里再次应用权威 scaler，以防工厂误用了全局默认值。
                    RuntimeUi.UiCanvasScalerSettings scaler = definition.ScalerSettings;
                    host.SetCanvasScaler(in scaler);
                    ResolveManifest(definition, out RuntimeUi.UiManifest? manifest, out string uiRoot);
                    PreloadManifest(host, manifest);
                    ScriptUi.UiCanvasHandle handle = new(NextHandle(ref _nextCanvasHandle, "Canvas"));
                    CanvasSlot slot = new(
                        definition,
                        handle,
                        host,
                        new GameUiModelBridge(host, _maxBindingsPerCanvas, _maxPathsPerScreen),
                        manifest,
                        uiRoot);
                    _stagingSlots[stagedCount++] = slot;
                    hostStored = true;

                    if (definition.InitialScreenId is string initialScreenId)
                    {
                        if (pendingCount == _pendingScreenBindings.Length ||
                            pendingCount == _screenBindings.Length)
                        {
                            throw new InvalidOperationException("初始 UI 屏幕数量超过固定屏幕实例容量。");
                        }

                        ResolveScreen(slot, initialScreenId, out RuntimeUi.UiScreenId screenId, out RuntimeUi.UiDocumentSource source);
                        RuntimeUi.UiScreenHandle localScreen = host.ShowScreen(screenId, in source);
                        ScriptUi.UiScreenHandle globalScreen = new(NextHandle(ref _nextScreenHandle, "Screen"));
                        _pendingScreenBindings[pendingCount++] = new PendingScreenBinding(
                            handle,
                            localScreen,
                            globalScreen);
                    }
                }
                catch
                {
                    if (!hostStored)
                    {
                        host.Dispose();
                    }

                    throw;
                }
            }
        }
        catch
        {
            DisposeSlots(_stagingSlots, stagedCount);
            ClearPendingBindings(pendingCount);
            throw;
        }

        DisposeSlots(_slots, Count);
        (_slots, _stagingSlots) = (_stagingSlots, _slots);
        Count = stagedCount;
        ClearScreenBindings();
        for (int i = 0; i < pendingCount; i++)
        {
            PendingScreenBinding pending = _pendingScreenBindings[i];
            AddScreenBinding(pending.Canvas, pending.LocalScreen, pending.GlobalScreen);
        }

        ClearPendingBindings(pendingCount);
        ResetInputState();
    }

    /// <summary>按稳定 Canvas id 查找当前场景实例句柄。</summary>
    public bool TryGetCanvas(ScriptUi.UiCanvasId id, out ScriptUi.UiCanvasHandle canvas)
    {
        ThrowIfDisposed();
        for (int i = 0; i < Count; i++)
        {
            CanvasSlot slot = GetRequiredSlot(i);
            if (slot.Definition.Id == id)
            {
                canvas = slot.Handle;
                return true;
            }
        }

        canvas = default;
        return false;
    }

    /// <summary>按确定性合成顺序复制当前 Canvas 句柄。</summary>
    public int CopyCanvases(Span<ScriptUi.UiCanvasHandle> destination)
    {
        ThrowIfDisposed();
        int count = Math.Min(destination.Length, Count);
        for (int i = 0; i < count; i++)
        {
            destination[i] = GetRequiredSlot(i).Handle;
        }

        return count;
    }

    /// <summary>取得 primary host，供旧单 Canvas Hosting 服务只读兼容。</summary>
    public bool TryGetPrimaryHost(out RuntimeUi.GameUiHost host)
    {
        ThrowIfDisposed();
        int index = FindPrimarySlotIndex();
        if (index >= 0)
        {
            host = GetRequiredSlot(index).Host;
            return true;
        }

        host = null!;
        return false;
    }

    /// <summary>向所有 Canvas 下发同一 UI paint 降频偏好。</summary>
    public void SetPresentationFrameInterval(int intervalFrames)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalFrames);
        for (int i = 0; i < Count; i++)
        {
            GetRequiredSlot(i).Host.SetPresentationFrameInterval(intervalFrames);
        }
    }

    /// <summary>在指定 Canvas 显示普通屏幕并返回跨 Canvas 全局唯一句柄。</summary>
    public ScriptUi.UiScreenHandle ShowScreen(ScriptUi.UiCanvasHandle canvas, string screenId)
    {
        return ShowScreenCore(canvas, screenId, modal: false);
    }

    /// <summary>在指定 Canvas 压入模态屏幕并返回跨 Canvas 全局唯一句柄。</summary>
    public ScriptUi.UiScreenHandle PushModal(ScriptUi.UiCanvasHandle canvas, string screenId)
    {
        return ShowScreenCore(canvas, screenId, modal: true);
    }

    /// <summary>隐藏全局屏幕句柄对应的 Canvas 内屏幕。</summary>
    public bool HideScreen(ScriptUi.UiScreenHandle screen)
    {
        ThrowIfDisposed();
        int bindingIndex = FindScreenBindingIndex(screen);
        if (bindingIndex < 0)
        {
            return false;
        }

        ScreenBinding binding = _screenBindings[bindingIndex];
        int slotIndex = FindSlotIndex(binding.Canvas);
        bool hidden = slotIndex >= 0 && GetRequiredSlot(slotIndex).Host.HideScreen(binding.LocalScreen);
        RemoveScreenBindingAt(bindingIndex);
        return hidden;
    }

    /// <summary>把脚本模型绑定到全局屏幕句柄。</summary>
    public void BindModel(
        ScriptUi.UiScreenHandle screen,
        ScriptUi.UiModelName modelName,
        ScriptUi.IUiModel model)
    {
        ThrowIfDisposed();
        ScreenBinding binding = GetRequiredScreenBinding(screen);
        int slotIndex = FindSlotIndex(binding.Canvas);
        if (slotIndex < 0)
        {
            throw new KeyNotFoundException($"Canvas {binding.Canvas.Value} 已失效。");
        }

        GetRequiredSlot(slotIndex).ModelBridge.BindModel(
            new ScriptUi.UiScreenHandle(binding.LocalScreen.Value),
            modelName,
            model);
    }

    /// <summary>写入全局屏幕实例的模型值。</summary>
    public void SetValue(ScriptUi.UiScreenHandle screen, RuntimeUi.UiPathId path, in RuntimeUi.UiValue value)
    {
        ThrowIfDisposed();
        if (TryResolveScreen(screen, out CanvasSlot slot, out RuntimeUi.UiScreenHandle localScreen))
        {
            slot.Host.SetModelValue(localScreen, path, in value);
        }
    }

    /// <summary>读取全局屏幕实例的模型值。</summary>
    public bool TryGetValue(
        ScriptUi.UiScreenHandle screen,
        RuntimeUi.UiPathId path,
        out RuntimeUi.UiValue value)
    {
        ThrowIfDisposed();
        if (TryResolveScreen(screen, out CanvasSlot slot, out RuntimeUi.UiScreenHandle localScreen) &&
            slot.Host.TryGetModelValue(localScreen, path, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>调用全局屏幕实例上的动作。</summary>
    public bool Invoke(
        ScriptUi.UiScreenHandle screen,
        RuntimeUi.UiActionId action,
        in RuntimeUi.UiValue payload)
    {
        ThrowIfDisposed();
        return TryResolveScreen(screen, out CanvasSlot slot, out RuntimeUi.UiScreenHandle localScreen) &&
            slot.Host.InvokeAction(localScreen, action, in payload);
    }

    /// <summary>把所有 Canvas 已绑定脚本模型推送到各自后端。</summary>
    public void PushGameUiModels()
    {
        ThrowIfDisposed();
        for (int i = 0; i < Count; i++)
        {
            GetRequiredSlot(i).ModelBridge.PushGameUiModels();
        }
    }

    /// <summary>以同一 render dt 推进所有 Canvas。</summary>
    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        for (int i = 0; i < Count; i++)
        {
            GetRequiredSlot(i).Host.Update(deltaSeconds);
        }
    }

    /// <summary>
    /// 从指定确定性 Canvas 索引 drain 事件，避免稳态枚举与临时集合分配。
    /// </summary>
    public int DrainEventsAt(
        int canvasIndex,
        Span<RuntimeUi.UiEvent> destination,
        out ScriptUi.UiCanvasHandle canvas)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(canvasIndex);
        if (canvasIndex >= Count)
        {
            canvas = default;
            return 0;
        }

        CanvasSlot slot = GetRequiredSlot(canvasIndex);
        canvas = slot.Handle;
        return slot.Host.DrainEvents(destination);
    }

    /// <summary>将后端文档事件映射回跨 Canvas 全局屏幕句柄。</summary>
    public bool TryResolveEventScreen(
        ScriptUi.UiCanvasHandle canvas,
        RuntimeUi.UiDocumentHandle document,
        out ScriptUi.UiScreenHandle screen)
    {
        ThrowIfDisposed();
        int slotIndex = FindSlotIndex(canvas);
        if (slotIndex >= 0 &&
            GetRequiredSlot(slotIndex).Host.TryGetVisibleScreen(document, out RuntimeUi.UiScreenHandle localScreen))
        {
            for (int i = 0; i < _screenBindingCount; i++)
            {
                ScreenBinding binding = _screenBindings[i];
                if (binding.Canvas == canvas && binding.LocalScreen == localScreen)
                {
                    screen = binding.GlobalScreen;
                    return true;
                }
            }
        }

        screen = default;
        return false;
    }

    /// <summary>将同一帧边界的显示度量应用到所有已物化 Canvas。</summary>
    /// <param name="displayMetrics">物理 framebuffer、DPI 与 CanvasScaler 解析所需的显示度量。</param>
    public void Resize(in RuntimeUi.UiDisplayMetrics displayMetrics)
    {
        ThrowIfDisposed();
        for (int i = 0; i < Count; i++)
        {
            GetRequiredSlot(i).Host.Resize(in displayMetrics);
        }
    }

    /// <summary>按 sorting order 将所有 native Canvas 合成到当前呈现目标。</summary>
    /// <param name="context">当前 UI 呈现上下文。</param>
    public void Composite(in UiPresentContext context)
    {
        ThrowIfDisposed();
        for (int i = 0; i < Count; i++)
        {
            CanvasSlot slot = GetRequiredSlot(i);
            if (slot.Host.BackendKind != RuntimeUi.UiBackendKind.ManagedFallback)
            {
                slot.Host.Composite(in context);
            }
        }
    }

    /// <summary>按 sorting order 将所有托管回退 Canvas 绘制到脚本 GUI 上下文。</summary>
    /// <param name="gui">当前脚本 GUI 绘制上下文。</param>
    public void DrawGui(IGuiDrawContext gui)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(gui);
        for (int i = 0; i < Count; i++)
        {
            CanvasSlot slot = GetRequiredSlot(i);
            if (slot.Host.BackendKind == RuntimeUi.UiBackendKind.ManagedFallback)
            {
                slot.Host.DrawGui(gui);
            }
        }
    }

    /// <summary>向所有 Canvas 转发指针移动，并重新解析最上层命中目标。</summary>
    /// <param name="x">UI 逻辑坐标 X。</param>
    /// <param name="y">UI 逻辑坐标 Y。</param>
    public void FeedPointerMove(float x, float y)
    {
        ThrowIfDisposed();
        _pointerX = x;
        _pointerY = y;
        for (int i = 0; i < Count; i++)
        {
            // 所有 Canvas 都接收 move，确保离开顶层 Canvas 后旧 hover 能可靠清除。
            GetRequiredSlot(i).Host.FeedPointerMove(x, y);
        }

        if (_pointerCaptureIndex < 0)
        {
            _pointerTargetIndex = FindTopPointerTarget(x, y);
        }
    }

    /// <summary>把指针按键事件路由到捕获目标或最上层命中 Canvas。</summary>
    /// <param name="button">指针按键。</param>
    /// <param name="isDown">是否为按下事件。</param>
    public void FeedPointerButton(RuntimeUi.UiPointerButton button, bool isDown)
    {
        ThrowIfDisposed();
        _pointerButtonCalls++;
        if (button == RuntimeUi.UiPointerButton.Left)
        {
            if (isDown)
            {
                _leftPressCalls++;
            }
            else
            {
                _leftReleaseCalls++;
            }
        }

        int mask = 1 << (int)button;
        int targetIndex = _pointerCaptureIndex >= 0
            ? _pointerCaptureIndex
            : _pointerTargetIndex >= 0
                ? _pointerTargetIndex
                : FindTopPointerTarget(_pointerX, _pointerY);
        _lastButtonTargetIndex = targetIndex;
        if (targetIndex >= 0)
        {
            CanvasSlot target = GetRequiredSlot(targetIndex);
            _lastButtonTargetCanvas = target.Handle.Value;
            _lastButtonTargetBackend = target.Host.BackendKind;
            _lastButtonTargetHit = target.Host.HitTest(_pointerX, _pointerY);
            target.Host.FeedPointerButton(button, isDown);
            _forwardedPointerButtonCalls++;
        }
        else
        {
            _lastButtonTargetCanvas = 0;
            _lastButtonTargetBackend = default;
            _lastButtonTargetHit = RuntimeUi.UiHitResult.None;
        }

        if (isDown)
        {
            _pressedPointerButtons |= mask;
            _pointerCaptureIndex = targetIndex;
            _keyboardTargetIndex = targetIndex;
        }
        else
        {
            _pressedPointerButtons &= ~mask;
            if (_pressedPointerButtons == 0)
            {
                _pointerCaptureIndex = -1;
                _pointerTargetIndex = FindTopPointerTarget(_pointerX, _pointerY);
            }
        }
    }

    /// <summary>捕获多 Canvas 指针目标与按钮转发的只读诊断。</summary>
    /// <returns>当前累计输入诊断快照。</returns>
    public GameUiCanvasInputDiagnostics CaptureInputDiagnostics()
    {
        ThrowIfDisposed();
        return new GameUiCanvasInputDiagnostics(
            _pointerX,
            _pointerY,
            _pointerTargetIndex,
            _pointerCaptureIndex,
            _lastButtonTargetIndex,
            _lastButtonTargetCanvas,
            _lastButtonTargetBackend,
            _lastButtonTargetHit,
            _pointerButtonCalls,
            _forwardedPointerButtonCalls,
            _leftPressCalls,
            _leftReleaseCalls,
            _pressedPointerButtons);
    }

    /// <summary>把滚轮增量路由到捕获目标或最上层命中 Canvas。</summary>
    /// <param name="deltaX">水平滚动增量。</param>
    /// <param name="deltaY">垂直滚动增量。</param>
    public void FeedScroll(float deltaX, float deltaY)
    {
        ThrowIfDisposed();
        int targetIndex = _pointerCaptureIndex >= 0 ? _pointerCaptureIndex : _pointerTargetIndex;
        if (targetIndex < 0)
        {
            targetIndex = FindTopPointerTarget(_pointerX, _pointerY);
        }

        if (targetIndex >= 0)
        {
            GetRequiredSlot(targetIndex).Host.FeedScroll(deltaX, deltaY);
        }
    }

    /// <summary>把键盘事件路由到当前拥有键盘焦点的 Canvas。</summary>
    /// <param name="key">规范化 UI 按键。</param>
    /// <param name="isDown">是否为按下事件。</param>
    /// <param name="modifiers">当前修饰键状态。</param>
    public void FeedKey(RuntimeUi.UiKey key, bool isDown, RuntimeUi.UiKeyModifiers modifiers)
    {
        ThrowIfDisposed();
        int targetIndex = ResolveKeyboardTargetIndex();
        if (targetIndex >= 0)
        {
            GetRequiredSlot(targetIndex).Host.FeedKey(key, isDown, modifiers);
        }
    }

    /// <summary>把已经提交的文本输入路由到当前键盘目标 Canvas。</summary>
    /// <param name="text">UTF-16 文本片段。</param>
    public void FeedText(ReadOnlySpan<char> text)
    {
        ThrowIfDisposed();
        int targetIndex = ResolveKeyboardTargetIndex();
        if (targetIndex >= 0)
        {
            GetRequiredSlot(targetIndex).Host.FeedText(text);
        }
    }

    /// <summary>把 IME 预编辑文本与选区状态路由到当前键盘目标 Canvas。</summary>
    /// <param name="text">IME 预编辑文本。</param>
    /// <param name="composition">预编辑选区与光标状态。</param>
    public void FeedTextComposition(ReadOnlySpan<char> text, in RuntimeUi.UiTextComposition composition)
    {
        ThrowIfDisposed();
        int targetIndex = ResolveKeyboardTargetIndex();
        if (targetIndex >= 0)
        {
            GetRequiredSlot(targetIndex).Host.FeedTextComposition(text, in composition);
        }
    }

    /// <summary>查询当前键盘目标或最上层可编辑 Canvas 的 IME 候选框几何。</summary>
    /// <param name="geometry">成功时返回逻辑坐标下的 IME 几何。</param>
    /// <returns>找到有效 IME 几何时为 <see langword="true"/>。</returns>
    public bool TryGetImeGeometry(out RuntimeUi.UiImeGeometry geometry)
    {
        ThrowIfDisposed();
        int targetIndex = ResolveKeyboardTargetIndex();
        if (targetIndex >= 0 && GetRequiredSlot(targetIndex).Host.TryGetImeGeometry(out geometry))
        {
            return true;
        }

        for (int i = Count - 1; i >= 0; i--)
        {
            if (i != targetIndex && GetRequiredSlot(i).Host.TryGetImeGeometry(out geometry))
            {
                return true;
            }
        }

        geometry = RuntimeUi.UiImeGeometry.None;
        return false;
    }

    /// <summary>按从上到下的 Canvas 顺序合并指定位置的 UI 命中结果。</summary>
    /// <param name="x">UI 逻辑坐标 X。</param>
    /// <param name="y">UI 逻辑坐标 Y。</param>
    /// <returns>聚合后的命中、遮挡与输入意图。</returns>
    public RuntimeUi.UiHitResult HitTest(float x, float y)
    {
        ThrowIfDisposed();
        bool hitsUi = false;
        bool opaque = false;
        bool wantsMouse = false;
        bool wantsKeyboard = false;
        for (int i = Count - 1; i >= 0; i--)
        {
            RuntimeUi.UiHitResult hit = GetRequiredSlot(i).Host.HitTest(x, y);
            if (!hit.HitsUi)
            {
                continue;
            }

            hitsUi = true;
            opaque |= hit.Opaque;
            wantsMouse |= hit.WantsMouse;
            wantsKeyboard |= hit.WantsKeyboard;
            if (hit.Opaque)
            {
                break;
            }
        }

        return hitsUi
            ? new RuntimeUi.UiHitResult(hitsUi, opaque, wantsMouse, wantsKeyboard)
            : RuntimeUi.UiHitResult.None;
    }

    /// <summary>解析当前 presentation 指针的真实 Game UI 后端所有者。</summary>
    internal bool TryResolvePointerInputOwner(
        float x,
        float y,
        out RuntimeUi.UiBackendKind backendKind)
    {
        ThrowIfDisposed();
        int targetIndex = _pointerCaptureIndex >= 0
            ? _pointerCaptureIndex
            : FindTopPointerTarget(x, y);
        if (targetIndex < 0)
        {
            backendKind = default;
            return false;
        }

        CanvasSlot target = GetRequiredSlot(targetIndex);
        if (_pointerCaptureIndex < 0)
        {
            RuntimeUi.UiHitResult hit = target.Host.HitTest(x, y);
            if (!hit.WantsMouse && !hit.Opaque)
            {
                backendKind = default;
                return false;
            }
        }

        backendKind = target.Host.BackendKind;
        return true;
    }

    /// <summary>释放当前与 staging Canvas host。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeSlots(_slots, Count);
        DisposeSlots(_stagingSlots, _stagingSlots.Length);
        ClearScreenBindings();
        Count = 0;
        _disposed = true;
    }

    private ScriptUi.UiScreenHandle ShowScreenCore(
        ScriptUi.UiCanvasHandle canvas,
        string screenId,
        bool modal)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        int slotIndex = FindSlotIndex(canvas);
        if (slotIndex < 0)
        {
            return default;
        }

        if (_screenBindingCount == _screenBindings.Length)
        {
            throw new InvalidOperationException("跨 Canvas 可见屏幕实例容量已满。");
        }

        CanvasSlot slot = GetRequiredSlot(slotIndex);
        ResolveScreen(slot, screenId, out RuntimeUi.UiScreenId runtimeScreen, out RuntimeUi.UiDocumentSource source);
        RuntimeUi.UiScreenHandle localScreen = modal
            ? slot.Host.PushModal(runtimeScreen, in source)
            : slot.Host.ShowScreen(runtimeScreen, in source);
        ScriptUi.UiScreenHandle globalScreen = new(NextHandle(ref _nextScreenHandle, "Screen"));
        AddScreenBinding(canvas, localScreen, globalScreen);
        return globalScreen;
    }

    private bool TryResolveScreen(
        ScriptUi.UiScreenHandle screen,
        out CanvasSlot slot,
        out RuntimeUi.UiScreenHandle localScreen)
    {
        int bindingIndex = FindScreenBindingIndex(screen);
        if (bindingIndex >= 0)
        {
            ScreenBinding binding = _screenBindings[bindingIndex];
            int slotIndex = FindSlotIndex(binding.Canvas);
            if (slotIndex >= 0)
            {
                slot = GetRequiredSlot(slotIndex);
                localScreen = binding.LocalScreen;
                return true;
            }
        }

        slot = null!;
        localScreen = default;
        return false;
    }

    private void ResolveManifest(
        in EngineSceneCanvasDefinition definition,
        out RuntimeUi.UiManifest? manifest,
        out string uiRoot)
    {
        string? manifestPath = null;
        if (definition.ManifestAssetId is string assetId)
        {
            manifestPath = _manifestAssetResolver?.Invoke(assetId);
            if (string.IsNullOrWhiteSpace(manifestPath) && definition.ManifestPath is string fallbackPath)
            {
                manifestPath = ResolveUnderRoot(_contentRoot, fallbackPath, "Web Canvas manifest");
            }

            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new FileNotFoundException(
                    $"找不到 Web Canvas manifest asset id：{assetId}，且场景没有可用的 manifestPath 回退。");
            }
        }
        else if (definition.ManifestPath is string relativePath)
        {
            manifestPath = ResolveUnderRoot(_contentRoot, relativePath, "Web Canvas manifest");
        }

        if (manifestPath is not null)
        {
            string fullPath = Path.IsPathRooted(manifestPath)
                ? Path.GetFullPath(manifestPath)
                : Path.GetFullPath(Path.Combine(_contentRoot, manifestPath));
            EnsureUnderRoot(_contentRoot, fullPath, "Web Canvas manifest");
            manifest = RuntimeUi.UiManifestLoader.Load(fullPath);
            uiRoot = NormalizeDirectory(Path.GetDirectoryName(fullPath) ?? _defaultUiRoot);
            return;
        }

        string defaultManifestPath = Path.Combine(_defaultUiRoot, RuntimeUi.UiManifestLoader.ManifestFileName);
        manifest = File.Exists(defaultManifestPath)
            ? RuntimeUi.UiManifestLoader.Load(defaultManifestPath)
            : null;
        uiRoot = _defaultUiRoot;
    }

    private static void PreloadManifest(RuntimeUi.GameUiHost host, RuntimeUi.UiManifest? manifest)
    {
        if (manifest is null)
        {
            return;
        }

        ReadOnlySpan<RuntimeUi.UiManifestScreen> screens = manifest.Screens;
        for (int i = 0; i < screens.Length; i++)
        {
            RuntimeUi.UiManifestScreen screen = screens[i];
            if (screen.Preload && !host.Documents.TryGetDocument(screen.ScreenId, out _))
            {
                RuntimeUi.UiDocumentSource source = screen.ToDocumentSource();
                _ = host.LoadDocument(screen.ScreenId, in source);
            }
        }

        ReadOnlySpan<RuntimeUi.UiManifestImage> images = manifest.Images;
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].Preload)
            {
                _ = host.PreloadImage(images[i].FullPath);
            }
        }
    }

    private static void ResolveScreen(
        CanvasSlot slot,
        string screenId,
        out RuntimeUi.UiScreenId runtimeScreen,
        out RuntimeUi.UiDocumentSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        if (!Path.IsPathRooted(screenId) &&
            slot.Manifest is not null &&
            slot.Manifest.TryGetScreen(screenId, out RuntimeUi.UiManifestScreen manifestScreen))
        {
            runtimeScreen = manifestScreen.ScreenId;
            source = manifestScreen.ToDocumentSource();
            return;
        }

        if (Path.IsPathRooted(screenId))
        {
            throw new InvalidDataException($"Game UI 屏幕资产路径必须相对 Canvas UI 根目录：{screenId}");
        }

        runtimeScreen = new RuntimeUi.UiScreenId(RuntimeUi.UiStableId.Hash(screenId));
        string requestedPath = ResolveUnderRoot(slot.UiRoot, screenId, "Game UI 屏幕资产");
        if (File.Exists(requestedPath))
        {
            source = RuntimeUi.UiDocumentSource.Asset(requestedPath, runtimeScreen.Value);
            return;
        }

        string xhtmlName = Path.ChangeExtension(screenId, ".xhtml") ?? screenId;
        string xhtmlPath = ResolveUnderRoot(slot.UiRoot, xhtmlName, "Game UI 屏幕资产");
        if (File.Exists(xhtmlPath))
        {
            source = RuntimeUi.UiDocumentSource.Asset(xhtmlPath, runtimeScreen.Value);
            return;
        }

        string htmlName = Path.ChangeExtension(screenId, ".html") ?? screenId;
        string htmlPath = ResolveUnderRoot(slot.UiRoot, htmlName, "Game UI 屏幕资产");
        source = File.Exists(htmlPath)
            ? RuntimeUi.UiDocumentSource.Asset(htmlPath, runtimeScreen.Value)
            : throw new FileNotFoundException($"找不到 Game UI 屏幕资产：{screenId}。", requestedPath);
    }

    private int FindTopPointerTarget(float x, float y)
    {
        int firstHit = -1;
        for (int i = Count - 1; i >= 0; i--)
        {
            RuntimeUi.UiHitResult hit = GetRequiredSlot(i).Host.HitTest(x, y);
            if (!hit.HitsUi)
            {
                continue;
            }

            firstHit = firstHit < 0 ? i : firstHit;
            if (hit.WantsMouse || hit.Opaque)
            {
                return i;
            }
        }

        return firstHit;
    }

    private int ResolveKeyboardTargetIndex()
    {
        return _keyboardTargetIndex >= 0 && _keyboardTargetIndex < Count
            ? _keyboardTargetIndex
            : FindPrimarySlotIndex();
    }

    private int FindPrimarySlotIndex()
    {
        for (int i = 0; i < Count; i++)
        {
            if (GetRequiredSlot(i).Definition.IsPrimary)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSlotIndex(ScriptUi.UiCanvasHandle handle)
    {
        if (handle.Value <= 0)
        {
            return -1;
        }

        for (int i = 0; i < Count; i++)
        {
            if (GetRequiredSlot(i).Handle == handle)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindScreenBindingIndex(ScriptUi.UiScreenHandle screen)
    {
        if (screen.Value <= 0)
        {
            return -1;
        }

        for (int i = 0; i < _screenBindingCount; i++)
        {
            if (_screenBindings[i].GlobalScreen == screen)
            {
                return i;
            }
        }

        return -1;
    }

    private ScreenBinding GetRequiredScreenBinding(ScriptUi.UiScreenHandle screen)
    {
        int index = FindScreenBindingIndex(screen);
        return index >= 0
            ? _screenBindings[index]
            : throw new KeyNotFoundException($"Game UI 屏幕句柄 {screen.Value} 不存在或已失效。");
    }

    private CanvasSlot GetRequiredSlot(int index)
    {
        return _slots[index] ?? throw new InvalidOperationException($"Canvas slot {index} 未物化。");
    }

    private void AddScreenBinding(
        ScriptUi.UiCanvasHandle canvas,
        RuntimeUi.UiScreenHandle localScreen,
        ScriptUi.UiScreenHandle globalScreen)
    {
        if (_screenBindingCount == _screenBindings.Length)
        {
            throw new InvalidOperationException("跨 Canvas 可见屏幕实例容量已满。");
        }

        _screenBindings[_screenBindingCount++] = new ScreenBinding(canvas, localScreen, globalScreen);
    }

    private void RemoveScreenBindingAt(int index)
    {
        int moveCount = _screenBindingCount - index - 1;
        if (moveCount > 0)
        {
            _screenBindings.AsSpan(index + 1, moveCount).CopyTo(_screenBindings.AsSpan(index, moveCount));
        }

        _screenBindings[--_screenBindingCount] = default;
    }

    private void ClearScreenBindings()
    {
        _screenBindings.AsSpan(0, _screenBindingCount).Clear();
        _screenBindingCount = 0;
    }

    private void ClearPendingBindings(int count)
    {
        _pendingScreenBindings.AsSpan(0, count).Clear();
    }

    private void ResetInputState()
    {
        _pointerTargetIndex = -1;
        _keyboardTargetIndex = -1;
        _pointerCaptureIndex = -1;
        _pressedPointerButtons = 0;
        _pointerX = 0f;
        _pointerY = 0f;
    }

    private static void DisposeSlots(CanvasSlot?[] slots, int count)
    {
        int safeCount = Math.Min(count, slots.Length);
        for (int i = 0; i < safeCount; i++)
        {
            slots[i]?.Host.Dispose();
            slots[i] = null;
        }
    }

    private static int NextHandle(ref int current, string kind)
    {
        return current == int.MaxValue
            ? throw new InvalidOperationException($"{kind} 运行时句柄空间已耗尽，必须重启进程。")
            : ++current;
    }

    private static string ResolveUnderRoot(string root, string relativePath, string fieldName)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"{fieldName}路径必须为相对路径：{relativePath}");
        }

        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        EnsureUnderRoot(root, fullPath, fieldName);
        return fullPath;
    }

    private static void EnsureUnderRoot(string root, string fullPath, string fieldName)
    {
        string normalizedRoot = NormalizeDirectory(root);
        string normalizedPath = Path.GetFullPath(fullPath);
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!normalizedPath.StartsWith(normalizedRoot, comparison))
        {
            throw new InvalidDataException($"{fieldName}路径逃逸 content 根目录：{fullPath}");
        }
    }

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath + Path.DirectorySeparatorChar;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class CanvasSlot
    {
        internal CanvasSlot(
            EngineSceneCanvasDefinition definition,
            ScriptUi.UiCanvasHandle handle,
            RuntimeUi.GameUiHost host,
            GameUiModelBridge modelBridge,
            RuntimeUi.UiManifest? manifest,
            string uiRoot)
        {
            Definition = definition;
            Handle = handle;
            Host = host;
            ModelBridge = modelBridge;
            Manifest = manifest;
            UiRoot = uiRoot;
        }

        internal EngineSceneCanvasDefinition Definition { get; }

        internal ScriptUi.UiCanvasHandle Handle { get; }

        internal RuntimeUi.GameUiHost Host { get; }

        internal GameUiModelBridge ModelBridge { get; }

        internal RuntimeUi.UiManifest? Manifest { get; }

        internal string UiRoot { get; }
    }

    private readonly record struct ScreenBinding(
        ScriptUi.UiCanvasHandle Canvas,
        RuntimeUi.UiScreenHandle LocalScreen,
        ScriptUi.UiScreenHandle GlobalScreen);

    private readonly record struct PendingScreenBinding(
        ScriptUi.UiCanvasHandle Canvas,
        RuntimeUi.UiScreenHandle LocalScreen,
        ScriptUi.UiScreenHandle GlobalScreen);
}

/// <summary>多 Canvas 指针捕获与按钮目标的只读诊断快照。</summary>
/// <param name="PointerX">最近一次 presentation 指针 X。</param>
/// <param name="PointerY">最近一次 presentation 指针 Y。</param>
/// <param name="PointerTargetIndex">当前 hover 目标索引；无目标时为 -1。</param>
/// <param name="PointerCaptureIndex">当前拖拽捕获目标索引；无捕获时为 -1。</param>
/// <param name="LastButtonTargetIndex">最近一次按钮边沿的目标索引。</param>
/// <param name="LastButtonTargetCanvas">最近一次按钮边沿的稳定 Canvas handle。</param>
/// <param name="LastButtonTargetBackend">最近一次按钮边沿的后端类型。</param>
/// <param name="LastButtonTargetHit">最近一次按钮边沿位置的命中快照。</param>
/// <param name="PointerButtonCalls">累计按钮边沿调用数。</param>
/// <param name="ForwardedPointerButtonCalls">累计实际转发到 Canvas 的按钮边沿数。</param>
/// <param name="LeftPressCalls">累计左键按下边沿数。</param>
/// <param name="LeftReleaseCalls">累计左键释放边沿数。</param>
/// <param name="PressedPointerButtons">当前按下按钮 bit mask。</param>
public readonly record struct GameUiCanvasInputDiagnostics(
    float PointerX,
    float PointerY,
    int PointerTargetIndex,
    int PointerCaptureIndex,
    int LastButtonTargetIndex,
    int LastButtonTargetCanvas,
    RuntimeUi.UiBackendKind LastButtonTargetBackend,
    RuntimeUi.UiHitResult LastButtonTargetHit,
    long PointerButtonCalls,
    long ForwardedPointerButtonCalls,
    long LeftPressCalls,
    long LeftReleaseCalls,
    int PressedPointerButtons);
