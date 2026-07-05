using ScriptUi = PixelEngine.Scripting;
using RuntimeUi = PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 把脚本侧 IGameUiService 契约桥接到运行时 GameUiHost。
/// </summary>
public sealed class GameUiServiceBridge : ScriptUi.IGameUiService, IGameUiEventSink, IGameUiModelPusher
{
    private readonly RuntimeUi.GameUiHost _host;
    private readonly GameUiModelBridge _modelBridge;
    private readonly string _uiRoot;
    private readonly RuntimeUi.UiManifest? _manifest;

    /// <summary>
    /// 创建 Game UI 脚本服务桥。
    /// </summary>
    /// <param name="host">运行时 UI 宿主。</param>
    /// <param name="contentRoot">内容根目录。</param>
    /// <param name="manifest">可选的已加载 UI 清单；为 null 时若 content/ui/ui-manifest.json 存在则自动加载。</param>
    public GameUiServiceBridge(RuntimeUi.GameUiHost host, string contentRoot, RuntimeUi.UiManifest? manifest = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _modelBridge = new GameUiModelBridge(_host);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _uiRoot = Path.Combine(Path.GetFullPath(contentRoot), "ui");
        _manifest = manifest ?? LoadManifestIfPresent(_uiRoot);
    }

    /// <summary>
    /// UI 事件通知；事件由 GameUiPhaseDriver 在相位 1 drain 后派发。
    /// </summary>
    public event Action<ScriptUi.UiEvent>? UiEventRaised;

    /// <summary>
    /// 显示一个普通 UI 屏幕。
    /// </summary>
    /// <param name="screenId">屏幕资产 id 或路径。</param>
    /// <returns>可见屏幕实例句柄。</returns>
    public ScriptUi.UiScreenHandle ShowScreen(string screenId)
    {
        ResolveScreen(screenId, out RuntimeUi.UiScreenId runtimeScreen, out RuntimeUi.UiDocumentSource source);
        RuntimeUi.UiScreenHandle screen = _host.ShowScreen(runtimeScreen, in source);
        return new ScriptUi.UiScreenHandle(screen.Value);
    }

    /// <summary>
    /// 隐藏指定可见 UI 屏幕。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    public void HideScreen(ScriptUi.UiScreenHandle screen)
    {
        _ = _host.HideScreen(new RuntimeUi.UiScreenHandle(screen.Value));
    }

    /// <summary>
    /// 压入一个模态 UI 屏幕。
    /// </summary>
    /// <param name="screenId">屏幕资产 id 或路径。</param>
    /// <returns>可见屏幕实例句柄。</returns>
    public ScriptUi.UiScreenHandle PushModal(string screenId)
    {
        ResolveScreen(screenId, out RuntimeUi.UiScreenId runtimeScreen, out RuntimeUi.UiDocumentSource source);
        RuntimeUi.UiScreenHandle screen = _host.PushModal(runtimeScreen, in source);
        return new ScriptUi.UiScreenHandle(screen.Value);
    }

    /// <summary>
    /// 绑定脚本模型到 UI 屏幕。
    /// </summary>
    /// <param name="screen">可见屏幕实例句柄。</param>
    /// <param name="modelName">模型名。</param>
    /// <param name="model">脚本模型。</param>
    public void BindModel(ScriptUi.UiScreenHandle screen, ScriptUi.UiModelName modelName, ScriptUi.IUiModel model)
    {
        _modelBridge.BindModel(screen, modelName, model);
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
        _host.SetModelValue(new RuntimeUi.UiScreenHandle(screen.Value), new RuntimeUi.UiPathId(path.Value), in runtimeValue);
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
        if (_host.TryGetModelValue(
            new RuntimeUi.UiScreenHandle(screen.Value),
            new RuntimeUi.UiPathId(path.Value),
            out RuntimeUi.UiValue runtimeValue))
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
        _ = screen;
        _ = action;
        _ = payload;
        throw new NotSupportedException("当前 GameUiService 桥接尚未接入 UI Invoke 通道。");
    }

    /// <summary>
    /// 接收运行时 UI 后端 drain 出来的事件并转换为脚本事件。
    /// </summary>
    /// <param name="events">运行时 UI 事件。</param>
    public void OnGameUiEvents(ReadOnlySpan<RuntimeUi.UiEvent> events)
    {
        Action<ScriptUi.UiEvent>? handlers = UiEventRaised;
        if (handlers is null)
        {
            return;
        }

        for (int i = 0; i < events.Length; i++)
        {
            ref readonly RuntimeUi.UiEvent runtimeEvent = ref events[i];
            RuntimeUi.UiScreenHandle runtimeScreen = _host.TryGetVisibleScreen(runtimeEvent.Document, out RuntimeUi.UiScreenHandle visible)
                ? visible
                : new RuntimeUi.UiScreenHandle(runtimeEvent.Document.Value);
            RuntimeUi.UiValue payload = runtimeEvent.Payload;
            handlers(new ScriptUi.UiEvent(
                new ScriptUi.UiScreenHandle(runtimeScreen.Value),
                new ScriptUi.UiElementId(runtimeEvent.Element.Value),
                new ScriptUi.UiActionId(runtimeEvent.Action.Value),
                ToScriptValue(in payload)));
        }
    }

    /// <summary>
    /// 将已绑定的脚本 UI 模型推送到运行时 UI 后端。
    /// </summary>
    public void PushGameUiModels()
    {
        _modelBridge.PushGameUiModels();
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
        string path = Path.IsPathRooted(screenId) ? screenId : Path.Combine(_uiRoot, screenId);
        if (File.Exists(path))
        {
            return RuntimeUi.UiDocumentSource.Asset(path, runtimeScreen.Value);
        }

        string xhtml = Path.ChangeExtension(path, ".xhtml");
        if (File.Exists(xhtml))
        {
            return RuntimeUi.UiDocumentSource.Asset(xhtml, runtimeScreen.Value);
        }

        string html = Path.ChangeExtension(path, ".html");
        return File.Exists(html)
            ? RuntimeUi.UiDocumentSource.Asset(html, runtimeScreen.Value)
            : throw new FileNotFoundException($"找不到 Game UI 屏幕资产：{screenId}。", path);
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
}
