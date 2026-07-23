using ScriptUi = PixelEngine.Scripting;
using RuntimeUi = PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 把脚本侧 IGameUiService 契约桥接到运行时 GameUiHost。
/// </summary>
public sealed class GameUiServiceBridge : ScriptUi.IGameUiService, IGameUiEventSink, IGameUiModelPusher
{
    private static readonly ScriptUi.UiCanvasHandle LegacyCanvasHandle = new(1);
    private readonly RuntimeUi.GameUiHost? _host;
    private readonly GameUiCanvasRegistry? _registry;
    private readonly GameUiModelBridge? _modelBridge;
    private readonly RuntimeUi.UiStringPool _strings;
    private readonly string _uiRoot;
    private readonly RuntimeUi.UiManifest? _manifest;
    private readonly List<EventSubscription> _scriptEventSubscriptions = [];
    private Action<ScriptUi.UiEvent>? _directHandlers;
    private ScriptUi.IEventBus? _scriptEvents;

    /// <summary>
    /// 创建 Game UI 脚本服务桥。
    /// </summary>
    /// <param name="host">运行时 UI 宿主。</param>
    /// <param name="contentRoot">内容根目录。</param>
    /// <param name="manifest">可选的已加载 UI 清单；为 null 时若 content/ui/ui-manifest.json 存在则自动加载。</param>
    /// <param name="stringPool">脚本与运行时后端共享的 UI 字符串池。</param>
    public GameUiServiceBridge(
        RuntimeUi.GameUiHost host,
        string contentRoot,
        RuntimeUi.UiManifest? manifest = null,
        RuntimeUi.UiStringPool? stringPool = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _modelBridge = new GameUiModelBridge(_host);
        _strings = stringPool ?? new RuntimeUi.UiStringPool();
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _uiRoot = Path.Combine(Path.GetFullPath(contentRoot), "ui");
        _manifest = manifest ?? LoadManifestIfPresent(_uiRoot);
        PreloadManifestScreens();
        PreloadManifestImages();
    }

    /// <summary>
    /// 创建多 Canvas Game UI 脚本服务桥。
    /// </summary>
    /// <param name="registry">当前场景 Canvas 注册表。</param>
    public GameUiServiceBridge(GameUiCanvasRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _strings = registry.Strings;
        _uiRoot = string.Empty;
    }

    /// <summary>获取当前场景的 primary Canvas；旧单 Canvas 模式返回兼容句柄。</summary>
    public ScriptUi.UiCanvasHandle PrimaryCanvas => _registry?.PrimaryCanvas ?? LegacyCanvasHandle;

    /// <summary>按稳定 Canvas id 查询本次场景物化产生的运行时句柄。</summary>
    /// <param name="id">由场景 StableId 派生的稳定 Canvas id。</param>
    /// <param name="canvas">查询成功时返回运行时 Canvas 句柄。</param>
    /// <returns>当前场景存在对应 Canvas 时为 <see langword="true"/>。</returns>
    public bool TryGetCanvas(ScriptUi.UiCanvasId id, out ScriptUi.UiCanvasHandle canvas)
    {
        if (_registry is not null)
        {
            return _registry.TryGetCanvas(id, out canvas);
        }

        if (id == GameUiCanvasIdentity.LegacyImplicit)
        {
            canvas = LegacyCanvasHandle;
            return true;
        }

        canvas = default;
        return false;
    }

    /// <summary>把当前场景的运行时 Canvas 句柄复制到调用方缓冲区。</summary>
    /// <param name="destination">接收 Canvas 句柄的缓冲区。</param>
    /// <returns>实际复制的句柄数量。</returns>
    public int CopyCanvases(Span<ScriptUi.UiCanvasHandle> destination)
    {
        if (_registry is not null)
        {
            return _registry.CopyCanvases(destination);
        }

        if (destination.IsEmpty)
        {
            return 0;
        }

        destination[0] = LegacyCanvasHandle;
        return 1;
    }

    /// <summary>
    /// UI 事件通知；事件由 GameUiPhaseDriver 在相位 1 drain 后派发。
    /// </summary>
    public event Action<ScriptUi.UiEvent>? UiEventRaised
    {
        add
        {
            if (value is null)
            {
                return;
            }

            if (_scriptEvents is not null)
            {
                PruneReleasedSubscriptions();
                _scriptEventSubscriptions.Add(new EventSubscription(new WeakReference<Action<ScriptUi.UiEvent>>(value), _scriptEvents.Subscribe(value)));
                return;
            }

            _directHandlers += value;
        }

        remove
        {
            if (value is null)
            {
                return;
            }

            for (int i = _scriptEventSubscriptions.Count - 1; i >= 0; i--)
            {
                if (!_scriptEventSubscriptions[i].Handler.TryGetTarget(out Action<ScriptUi.UiEvent>? handler))
                {
                    _scriptEventSubscriptions.RemoveAt(i);
                    continue;
                }

                if (handler != value)
                {
                    continue;
                }

                _scriptEventSubscriptions[i].Subscription.Dispose();
                _scriptEventSubscriptions.RemoveAt(i);
                return;
            }

            _directHandlers -= value;
        }
    }

    /// <summary>
    /// 接入脚本事件总线，使 UI 事件在脚本相位 drain 时派发并复用 Behaviour 异常隔离。
    /// </summary>
    /// <param name="events">脚本事件总线。</param>
    public void AttachScriptEventBus(ScriptUi.IEventBus events)
    {
        _scriptEvents = events ?? throw new ArgumentNullException(nameof(events));
        Action<ScriptUi.UiEvent>? handlers = _directHandlers;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate invocation in handlers.GetInvocationList())
        {
            if (invocation is Action<ScriptUi.UiEvent> handler)
            {
                _scriptEventSubscriptions.Add(new EventSubscription(new WeakReference<Action<ScriptUi.UiEvent>>(handler), _scriptEvents.Subscribe(handler)));
            }
        }

        _directHandlers = null;
    }

    /// <summary>
    /// 显示一个普通 UI 屏幕。
    /// </summary>
    /// <param name="screenId">屏幕资产 id 或路径。</param>
    /// <returns>可见屏幕实例句柄。</returns>
    public ScriptUi.UiScreenHandle ShowScreen(string screenId)
    {
        if (_registry is not null)
        {
            return _registry.ShowScreen(_registry.PrimaryCanvas, screenId);
        }

        ResolveScreen(screenId, out RuntimeUi.UiScreenId runtimeScreen, out RuntimeUi.UiDocumentSource source);
        RuntimeUi.UiScreenHandle screen = _host!.ShowScreen(runtimeScreen, in source);
        return new ScriptUi.UiScreenHandle(screen.Value);
    }

    /// <summary>在指定 Canvas 上显示一个普通 UI 屏幕。</summary>
    /// <param name="canvas">目标 Canvas 运行时句柄。</param>
    /// <param name="screenId">屏幕资产 id 或路径。</param>
    /// <returns>全局唯一的可见屏幕实例句柄；Canvas 无效时返回默认值。</returns>
    public ScriptUi.UiScreenHandle ShowScreen(ScriptUi.UiCanvasHandle canvas, string screenId)
    {
        return _registry is not null
            ? _registry.ShowScreen(canvas, screenId)
            : canvas == LegacyCanvasHandle ? ShowScreen(screenId) : default;
    }

    /// <summary>
    /// 隐藏指定可见 UI 屏幕。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    public void HideScreen(ScriptUi.UiScreenHandle screen)
    {
        if (_registry is not null)
        {
            _ = _registry.HideScreen(screen);
            return;
        }

        _ = _host!.HideScreen(new RuntimeUi.UiScreenHandle(screen.Value));
    }

    /// <summary>
    /// 压入一个模态 UI 屏幕。
    /// </summary>
    /// <param name="screenId">屏幕资产 id 或路径。</param>
    /// <returns>可见屏幕实例句柄。</returns>
    public ScriptUi.UiScreenHandle PushModal(string screenId)
    {
        if (_registry is not null)
        {
            return _registry.PushModal(_registry.PrimaryCanvas, screenId);
        }

        ResolveScreen(screenId, out RuntimeUi.UiScreenId runtimeScreen, out RuntimeUi.UiDocumentSource source);
        RuntimeUi.UiScreenHandle screen = _host!.PushModal(runtimeScreen, in source);
        return new ScriptUi.UiScreenHandle(screen.Value);
    }

    /// <summary>在指定 Canvas 上压入一个模态 UI 屏幕。</summary>
    /// <param name="canvas">目标 Canvas 运行时句柄。</param>
    /// <param name="screenId">屏幕资产 id 或路径。</param>
    /// <returns>全局唯一的可见屏幕实例句柄；Canvas 无效时返回默认值。</returns>
    public ScriptUi.UiScreenHandle PushModal(ScriptUi.UiCanvasHandle canvas, string screenId)
    {
        return _registry is not null
            ? _registry.PushModal(canvas, screenId)
            : canvas == LegacyCanvasHandle ? PushModal(screenId) : default;
    }

    /// <summary>
    /// 绑定脚本模型到 UI 屏幕。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    /// <param name="modelName">模型名。</param>
    /// <param name="model">脚本模型。</param>
    public void BindModel(ScriptUi.UiScreenHandle screen, ScriptUi.UiModelName modelName, ScriptUi.IUiModel model)
    {
        if (_registry is not null)
        {
            _registry.BindModel(screen, modelName, model);
            return;
        }

        _modelBridge!.BindModel(screen, modelName, model);
    }

    /// <summary>
    /// 把脚本文本登记到运行时字符串池，并返回脚本可写入模型的同值句柄。
    /// </summary>
    /// <param name="value">要显示的文本。</param>
    /// <returns>脚本侧字符串句柄。</returns>
    public ScriptUi.UiStringHandle InternString(string value)
    {
        RuntimeUi.UiStringHandle handle = _strings.Intern(value);
        return new ScriptUi.UiStringHandle(handle.Value);
    }

    /// <summary>
    /// 向指定 UI 屏幕写入模型值。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    /// <param name="path">模型路径。</param>
    /// <param name="value">写入值。</param>
    public void SetValue(ScriptUi.UiScreenHandle screen, ScriptUi.UiPathId path, in ScriptUi.UiValue value)
    {
        RuntimeUi.UiValue runtimeValue = ToRuntimeValue(in value);
        if (_registry is not null)
        {
            _registry.SetValue(screen, new RuntimeUi.UiPathId(path.Value), in runtimeValue);
            return;
        }

        _host!.SetModelValue(new RuntimeUi.UiScreenHandle(screen.Value), new RuntimeUi.UiPathId(path.Value), in runtimeValue);
    }

    /// <summary>
    /// 从指定 UI 屏幕读取模型值。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    /// <param name="path">模型路径。</param>
    /// <param name="value">读出值。</param>
    /// <returns>读取成功则返回 true。</returns>
    public bool TryGetValue(ScriptUi.UiScreenHandle screen, ScriptUi.UiPathId path, out ScriptUi.UiValue value)
    {
        bool found = _registry is not null
            ? _registry.TryGetValue(screen, new RuntimeUi.UiPathId(path.Value), out RuntimeUi.UiValue runtimeValue)
            : _host!.TryGetModelValue(
                new RuntimeUi.UiScreenHandle(screen.Value),
                new RuntimeUi.UiPathId(path.Value),
                out runtimeValue);

        if (found)
        {
            value = ToScriptValue(in runtimeValue);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 调用 UI 屏幕上的动作。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    /// <param name="action">动作 id。</param>
    /// <param name="payload">动作载荷。</param>
    public void Invoke(ScriptUi.UiScreenHandle screen, ScriptUi.UiActionId action, in ScriptUi.UiValue payload)
    {
        RuntimeUi.UiValue runtimePayload = ToRuntimeValue(in payload);
        bool invoked = _registry is not null
            ? _registry.Invoke(screen, new RuntimeUi.UiActionId(action.Value), in runtimePayload)
            : _host!.InvokeAction(
                new RuntimeUi.UiScreenHandle(screen.Value),
                new RuntimeUi.UiActionId(action.Value),
                in runtimePayload);
        if (!invoked)
        {
            throw new KeyNotFoundException($"Game UI 屏幕 {screen.Value} 未绑定 action: {action.Value}");
        }
    }

    /// <summary>
    /// 验证并应用一个真实 UI action，再把同一事件排入脚本事件总线。
    /// </summary>
    /// <param name="screen">当前可见屏幕实例。</param>
    /// <param name="action">稳定 action id。</param>
    /// <param name="payload">标量 action 载荷。</param>
    /// <param name="diagnostic">成功或失败诊断。</param>
    /// <returns>action 存在且事件已排入脚本分发路径时为 <see langword="true"/>。</returns>
    public bool TryDispatchAction(
        ScriptUi.UiScreenHandle screen,
        ScriptUi.UiActionId action,
        in ScriptUi.UiValue payload,
        out string diagnostic)
    {
        if (_scriptEvents is null && _directHandlers is null)
        {
            diagnostic = "Game UI action 没有已连接的脚本事件接收器。";
            return false;
        }

        ScriptUi.UiCanvasHandle canvas;
        bool invoked;
        if (_registry is not null)
        {
            if (!_registry.TryGetScreenCanvas(screen, out canvas))
            {
                diagnostic = $"Game UI 屏幕 {screen.Value} 已失效。";
                return false;
            }

            RuntimeUi.UiValue runtimePayload = ToRuntimeValue(in payload);
            invoked = _registry.Invoke(
                screen,
                new RuntimeUi.UiActionId(action.Value),
                in runtimePayload);
        }
        else
        {
            canvas = LegacyCanvasHandle;
            RuntimeUi.UiValue runtimePayload = ToRuntimeValue(in payload);
            invoked = _host!.InvokeAction(
                new RuntimeUi.UiScreenHandle(screen.Value),
                new RuntimeUi.UiActionId(action.Value),
                in runtimePayload);
        }

        if (!invoked)
        {
            diagnostic = $"Game UI 屏幕 {screen.Value} 未绑定 action: {action.Value}";
            return false;
        }

        ScriptUi.UiEvent scriptEvent = new(canvas, screen, default, action, payload);
        if (_scriptEvents is not null)
        {
            if (!_scriptEvents.TryPublish(in scriptEvent))
            {
                diagnostic = "Game UI 脚本事件队列已满。";
                return false;
            }
        }
        else
        {
            _directHandlers!(scriptEvent);
        }

        diagnostic = $"Game UI action {action.Value} 已排入 screen {screen.Value} 的脚本事件队列。";
        return true;
    }

    /// <summary>
    /// 接收运行时 UI 后端 drain 出来的事件并转换为脚本事件。
    /// </summary>
    /// <param name="events">运行时 UI 事件。</param>
    public void OnGameUiEvents(ReadOnlySpan<RuntimeUi.UiEvent> events)
    {
        DispatchGameUiEvents(LegacyCanvasHandle, events);
    }

    /// <summary>接收指定 Canvas 后端 drain 出来的事件并转换为脚本事件。</summary>
    /// <param name="canvas">事件来源 Canvas。</param>
    /// <param name="events">运行时 UI 事件。</param>
    public void OnGameUiEvents(
        ScriptUi.UiCanvasHandle canvas,
        ReadOnlySpan<RuntimeUi.UiEvent> events)
    {
        DispatchGameUiEvents(canvas, events);
    }

    private void DispatchGameUiEvents(
        ScriptUi.UiCanvasHandle canvas,
        ReadOnlySpan<RuntimeUi.UiEvent> events)
    {
        ScriptUi.IEventBus? scriptEvents = _scriptEvents;
        Action<ScriptUi.UiEvent>? directHandlers = _directHandlers;
        if (scriptEvents is null && directHandlers is null)
        {
            return;
        }

        for (int i = 0; i < events.Length; i++)
        {
            ref readonly RuntimeUi.UiEvent runtimeEvent = ref events[i];
            ScriptUi.UiScreenHandle scriptScreen;
            if (_registry is not null)
            {
                _ = _registry.TryResolveEventScreen(canvas, runtimeEvent.Document, out scriptScreen);
            }
            else
            {
                RuntimeUi.UiScreenHandle runtimeScreen = _host!.TryGetVisibleScreen(
                    runtimeEvent.Document,
                    out RuntimeUi.UiScreenHandle visible)
                        ? visible
                        : new RuntimeUi.UiScreenHandle(runtimeEvent.Document.Value);
                scriptScreen = new ScriptUi.UiScreenHandle(runtimeScreen.Value);
            }

            RuntimeUi.UiValue payload = runtimeEvent.Payload;
            ScriptUi.UiEvent scriptEvent = new(
                canvas,
                scriptScreen,
                new ScriptUi.UiElementId(runtimeEvent.Element.Value),
                new ScriptUi.UiActionId(runtimeEvent.Action.Value),
                ToScriptValue(in payload));
            if (scriptEvents is not null)
            {
                _ = scriptEvents.TryPublish(in scriptEvent);
                continue;
            }

            directHandlers!(scriptEvent);
        }
    }

    /// <summary>
    /// 将已绑定的脚本 UI 模型推送到运行时 UI 后端。
    /// </summary>
    public void PushGameUiModels()
    {
        if (_registry is not null)
        {
            _registry.PushGameUiModels();
            return;
        }

        _modelBridge!.PushGameUiModels();
    }

    private void ResolveScreen(
        string screenId,
        out RuntimeUi.UiScreenId runtimeScreen,
        out RuntimeUi.UiDocumentSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        if (!Path.IsPathRooted(screenId) &&
            _manifest is not null &&
            _manifest.TryGetScreen(screenId, out RuntimeUi.UiManifestScreen manifestScreen))
        {
            runtimeScreen = manifestScreen.ScreenId;
            source = manifestScreen.ToDocumentSource();
            return;
        }

        runtimeScreen = ToRuntimeScreenId(screenId);
        source = ResolveSourceByConvention(screenId, runtimeScreen);
    }

    private RuntimeUi.UiDocumentSource ResolveSourceByConvention(string screenId, RuntimeUi.UiScreenId runtimeScreen)
    {
        string path = ResolveUiAssetPath(screenId);
        if (File.Exists(path))
        {
            return RuntimeUi.UiDocumentSource.Asset(path, runtimeScreen.Value);
        }

        string xhtml = ResolveUiAssetPath(Path.ChangeExtension(screenId, ".xhtml") ?? screenId);
        if (File.Exists(xhtml))
        {
            return RuntimeUi.UiDocumentSource.Asset(xhtml, runtimeScreen.Value);
        }

        string html = ResolveUiAssetPath(Path.ChangeExtension(screenId, ".html") ?? screenId);
        return File.Exists(html)
            ? RuntimeUi.UiDocumentSource.Asset(html, runtimeScreen.Value)
            : throw new FileNotFoundException($"找不到 Game UI 屏幕资产：{screenId}。", path);
    }

    private string ResolveUiAssetPath(string screenId)
    {
        if (Path.IsPathRooted(screenId))
        {
            throw new InvalidDataException($"Game UI 屏幕资产路径必须相对 content/ui 根目录：{screenId}");
        }

        string fullPath = Path.GetFullPath(Path.Combine(_uiRoot, screenId));
        string normalizedFullPath = Path.TrimEndingDirectorySeparator(fullPath);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_uiRoot));
        string rootWithSeparator = root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return normalizedFullPath.Equals(root, comparison) || !fullPath.StartsWith(rootWithSeparator, comparison)
            ? throw new InvalidDataException($"Game UI 屏幕资产路径逃逸 content/ui 根目录：{screenId}")
            : fullPath;
    }

    private void PreloadManifestScreens()
    {
        if (_manifest is null)
        {
            return;
        }

        RuntimeUi.GameUiHost host = _host!;
        foreach (RuntimeUi.UiManifestScreen screen in _manifest.Screens)
        {
            if (!screen.Preload || host.Documents.TryGetDocument(screen.ScreenId, out _))
            {
                continue;
            }

            RuntimeUi.UiDocumentSource source = screen.ToDocumentSource();
            _ = host.LoadDocument(screen.ScreenId, in source);
        }
    }

    private void PreloadManifestImages()
    {
        if (_manifest is null)
        {
            return;
        }

        RuntimeUi.GameUiHost host = _host!;
        foreach (RuntimeUi.UiManifestImage image in _manifest.Images)
        {
            if (image.Preload)
            {
                _ = host.PreloadImage(image.FullPath);
            }
        }
    }

    private static RuntimeUi.UiManifest? LoadManifestIfPresent(string uiRoot)
    {
        string manifestPath = Path.Combine(uiRoot, RuntimeUi.UiManifestLoader.ManifestFileName);
        return File.Exists(manifestPath) ? RuntimeUi.UiManifestLoader.Load(manifestPath) : null;
    }

    private static RuntimeUi.UiScreenId ToRuntimeScreenId(string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        return new RuntimeUi.UiScreenId(RuntimeUi.UiStableId.Hash(screenId));
    }

    private static RuntimeUi.UiValue ToRuntimeValue(in ScriptUi.UiValue value)
    {
        return value.Kind switch
        {
            ScriptUi.UiValueKind.Empty => default,
            ScriptUi.UiValueKind.Boolean => RuntimeUi.UiValue.FromBoolean(value.AsBoolean()),
            ScriptUi.UiValueKind.Int64 => new RuntimeUi.UiValue(value.AsInt64()),
            ScriptUi.UiValueKind.Double => new RuntimeUi.UiValue(value.AsDouble()),
            ScriptUi.UiValueKind.StringHandle => RuntimeUi.UiValue.FromStringHandle(new RuntimeUi.UiStringHandle(value.AsStringHandle().Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "未知脚本 UI 值类型。"),
        };
    }

    private static ScriptUi.UiValue ToScriptValue(in RuntimeUi.UiValue value)
    {
        return value.Kind switch
        {
            RuntimeUi.UiValueKind.Empty => default,
            RuntimeUi.UiValueKind.Boolean => ScriptUi.UiValue.FromBoolean(value.AsBoolean()),
            RuntimeUi.UiValueKind.Int64 => new ScriptUi.UiValue(value.AsInt64()),
            RuntimeUi.UiValueKind.Double => new ScriptUi.UiValue(value.AsDouble()),
            RuntimeUi.UiValueKind.StringHandle => ScriptUi.UiValue.FromStringHandle(new ScriptUi.UiStringHandle(value.AsStringHandle().Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "未知运行时 UI 值类型。"),
        };
    }

    private void PruneReleasedSubscriptions()
    {
        for (int i = _scriptEventSubscriptions.Count - 1; i >= 0; i--)
        {
            if (!_scriptEventSubscriptions[i].Handler.TryGetTarget(out _))
            {
                _scriptEventSubscriptions.RemoveAt(i);
            }
        }
    }

    private readonly record struct EventSubscription(
        WeakReference<Action<ScriptUi.UiEvent>> Handler,
        IDisposable Subscription);
}
