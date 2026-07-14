using Hexa.NET.ImGui;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Scripting;
using System.Security.Cryptography;
using System.Text;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 顶层门面，负责 ImGui 帧生命周期、dockspace 与面板调度。
/// </summary>
public sealed class EditorApp : IDisposable
{
    private readonly ImGuiController _controller;
    private readonly ScriptGuiContext _scriptGuiContext;
    private readonly List<IEditorPanel> _panels = [];
    private readonly List<string> _panelIds = [];
    private readonly List<bool> _defaultPanelVisibility = [];
    private string? _pendingPanelFocusId;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// 创建 Editor 门面。
    /// </summary>
    /// <param name="backend">ImGui 后端。</param>
    /// <param name="options">Editor 选项。</param>
    public EditorApp(IEditorImGuiBackend backend, EditorAppOptions options)
        : this(new ImGuiController(backend, options))
    {
    }

    /// <summary>
    /// 创建 Editor 门面。
    /// </summary>
    /// <param name="controller">ImGui 控制器。</param>
    public EditorApp(ImGuiController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Options = _controller.Options;
        Selection = new EditorSelection();
        Input = new ImGuiInputBridge(_controller.Backend);
        _scriptGuiContext = new ScriptGuiContext(1, 1, 1f / 60f, default);
    }

    /// <summary>
    /// 当前选项。
    /// </summary>
    public EditorAppOptions Options { get; }

    /// <summary>
    /// 共享选择态。
    /// </summary>
    public EditorSelection Selection { get; }

    /// <summary>
    /// 输入桥。
    /// </summary>
    public ImGuiInputBridge Input { get; }

    /// <summary>
    /// 已注册面板数量。
    /// </summary>
    public int PanelCount => _panels.Count;

    internal string? PendingPanelFocusTitle
    {
        get
        {
            if (_pendingPanelFocusId is null)
            {
                return null;
            }

            for (int i = 0; i < _panelIds.Count; i++)
            {
                if (string.Equals(_panelIds[i], _pendingPanelFocusId, StringComparison.Ordinal))
                {
                    return _panels[i].Title;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Editor 是否已初始化且启用。
    /// </summary>
    public bool IsRunning => _initialized && Options.Enabled;

    /// <summary>在下一帧开始前安全应用 UI 缩放。</summary>
    public void SetUiScale(float scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            _controller.SetUiScale(scale);
        }
    }

    /// <summary>
    /// 注册一个面板。
    /// </summary>
    /// <param name="panel">面板实例。</param>
    public void AddPanel(IEditorPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        string baseId = CreateDefaultPanelId(panel.Title);
        string stableId = baseId;
        for (int suffix = 2; _panelIds.Contains(stableId, StringComparer.Ordinal); suffix++)
        {
            string suffixText = $"-{suffix}";
            int baseLength = Math.Min(baseId.Length, 128 - suffixText.Length);
            stableId = baseId[..baseLength] + suffixText;
        }

        AddPanel(stableId, panel);
    }

    /// <summary>
    /// 以显式稳定 ID 注册一个面板。
    /// </summary>
    /// <param name="stableId">不随标题和语言变化的 semantic panel ID。</param>
    /// <param name="panel">面板实例。</param>
    public void AddPanel(string stableId, IEditorPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ValidatePanelId(stableId);
        if (_panelIds.Contains(stableId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Editor panel ID '{stableId}' 已注册。");
        }

        _panelIds.Add(stableId);
        _panels.Add(panel);
        _defaultPanelVisibility.Add(panel.Visible);
    }

    /// <summary>
    /// 捕获 panel registry，不访问或遍历 ImGui dock node 内部对象。
    /// </summary>
    /// <returns>按注册顺序排列的稳定快照。</returns>
    public EditorPanelSnapshot[] CapturePanels()
    {
        EditorDockWindowState[] dockStates = new EditorDockWindowState[_panels.Count];
        if (_initialized && Options.EnableDockSpace)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i] is not IEditorChromePanel)
                {
                    dockStates[i] = _controller.CaptureDockWindow(_panels[i].Title);
                }
            }
        }

        Dictionary<uint, string> dockGroups = CreateDockGroupIds(dockStates);
        EditorPanelSnapshot[] snapshots = new EditorPanelSnapshot[_panels.Count];
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            EditorDockWindowState dock = dockStates[i];
            snapshots[i] = new EditorPanelSnapshot(
                _panelIds[i],
                panel.Title,
                panel.Visible,
                panel is IEditorChromePanel,
                panel is IEditorMaximizedPanel { IsMaximized: true },
                string.Equals(_pendingPanelFocusId, _panelIds[i], StringComparison.Ordinal),
                dock.Known,
                dock.DockId != 0,
                dock.DockId != 0 && dockGroups.TryGetValue(dock.DockId, out string? groupId)
                    ? groupId
                    : null,
                dock.X,
                dock.Y,
                dock.Width,
                dock.Height);
        }

        return snapshots;
    }

    /// <summary>捕获当前完整 Dear ImGui layout 文本。</summary>
    public string CaptureDockLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _initialized && Options.EnableDockSpace
            ? _controller.CaptureDockLayout()
            : throw new InvalidOperationException("Editor dockspace 尚未初始化。");
    }

    /// <summary>应用经过宿主校验的完整 Dear ImGui layout 文本。</summary>
    /// <param name="layout">ini layout 文本。</param>
    public void ApplyDockLayout(string layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized && Options.EnableDockSpace)
        {
            _controller.ApplyDockLayout(layout);
            return;
        }

        throw new InvalidOperationException("Editor dockspace 尚未初始化。");
    }

    /// <summary>按稳定 panel ID 执行 Tab、拆分或 Floating。</summary>
    /// <param name="panelId">源稳定 panel ID。</param>
    /// <param name="targetPanelId">非 Floating 时的目标稳定 panel ID。</param>
    /// <param name="request">已验证的停靠参数；窗口标题将由 registry 覆盖。</param>
    /// <param name="diagnostic">失败诊断。</param>
    /// <returns>dock tree 已变更时为 true。</returns>
    public bool TrySetPanelDock(
        string panelId,
        string? targetPanelId,
        EditorDockWindowRequest request,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        int sourceIndex = FindPanelIndex(panelId);
        if (sourceIndex < 0 || _panels[sourceIndex] is IEditorChromePanel)
        {
            diagnostic = "源 panel 不存在或属于不可停靠 chrome。";
            return false;
        }

        int targetIndex = -1;
        if (request.Placement != EditorDockPlacement.Floating)
        {
            if (string.IsNullOrWhiteSpace(targetPanelId))
            {
                diagnostic = "非 Floating 停靠必须提供目标 panel。";
                return false;
            }

            targetIndex = FindPanelIndex(targetPanelId);
            if (targetIndex < 0 ||
                targetIndex == sourceIndex ||
                _panels[targetIndex] is IEditorChromePanel)
            {
                diagnostic = "目标 panel 不存在、与源相同或属于不可停靠 chrome。";
                return false;
            }
        }

        _panels[sourceIndex].Visible = true;
        EditorDockWindowRequest resolved = request with
        {
            WindowTitle = _panels[sourceIndex].Title,
            TargetWindowTitle = targetIndex >= 0 ? _panels[targetIndex].Title : null,
        };
        return _controller.TrySetDockWindow(resolved, out diagnostic);
    }

    /// <summary>
    /// 按稳定 ID 显示并请求聚焦 panel。
    /// </summary>
    /// <param name="stableId">稳定 panel ID。</param>
    /// <returns>是否找到 panel。</returns>
    public bool TryShowPanelById(string stableId)
    {
        return TrySetPanelById(stableId, visible: true, focus: true);
    }

    /// <summary>
    /// 按稳定 ID 读取 panel 可见性。
    /// </summary>
    /// <param name="stableId">稳定 panel ID。</param>
    /// <param name="visible">找到时返回可见性。</param>
    /// <returns>是否找到 panel。</returns>
    public bool TryGetPanelVisibilityById(string stableId, out bool visible)
    {
        int index = FindPanelIndex(stableId);
        if (index >= 0)
        {
            visible = _panels[index].Visible;
            return true;
        }

        visible = false;
        return false;
    }

    /// <summary>
    /// 按稳定 ID 设置 panel 可见性，并可请求聚焦。
    /// </summary>
    /// <param name="stableId">稳定 panel ID。</param>
    /// <param name="visible">目标可见性。</param>
    /// <param name="focus">可见时是否请求聚焦。</param>
    /// <returns>是否找到 panel。</returns>
    public bool TrySetPanelById(string stableId, bool visible, bool focus)
    {
        int index = FindPanelIndex(stableId);
        if (index < 0)
        {
            return false;
        }

        _panels[index].Visible = visible;
        if (visible && focus)
        {
            _pendingPanelFocusId = _panelIds[index];
        }
        else if (!visible && string.Equals(
            _pendingPanelFocusId,
            _panelIds[index],
            StringComparison.Ordinal))
        {
            _pendingPanelFocusId = null;
        }

        return true;
    }

    /// <summary>
    /// 原子恢复先前捕获的完整 panel registry 状态。
    /// </summary>
    /// <param name="snapshots">由 <see cref="CapturePanels" /> 取得的完整快照。</param>
    /// <returns>registry 与快照仍完全匹配时为 true。</returns>
    public bool TryRestorePanels(IReadOnlyList<EditorPanelSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count != _panels.Count)
        {
            return false;
        }

        string? pendingFocusId = null;
        for (int i = 0; i < snapshots.Count; i++)
        {
            EditorPanelSnapshot snapshot = snapshots[i];
            if (!string.Equals(snapshot.Id, _panelIds[i], StringComparison.Ordinal) ||
                (snapshot.FocusPending && pendingFocusId is not null))
            {
                return false;
            }

            if (snapshot.FocusPending)
            {
                pendingFocusId = _panelIds[i];
            }
        }

        for (int i = 0; i < snapshots.Count; i++)
        {
            _panels[i].Visible = snapshots[i].Visible;
        }

        _pendingPanelFocusId = pendingFocusId;
        return true;
    }

    /// <summary>
    /// 将指定标题的已注册面板设为可见。
    /// </summary>
    /// <param name="title">面板标题。</param>
    /// <returns>找到并打开面板时为 true。</returns>
    public bool TryShowPanel(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        for (int i = 0; i < _panels.Count; i++)
        {
            if (!string.Equals(_panels[i].Title, title, StringComparison.Ordinal))
            {
                continue;
            }

            _panels[i].Visible = true;
            // Unity-like Window/File 命令既显示窗口也激活对应 dock tab；请求延迟到该面板
            // 下一次 Begin 之前消费，避免在菜单绘制期间把焦点给当前菜单宿主。
            _pendingPanelFocusId = _panelIds[i];
            return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试读取指定标题面板的当前可见性。
    /// </summary>
    /// <param name="title">面板标题。</param>
    /// <param name="visible">找到面板时返回其当前可见性；否则为 false。</param>
    /// <returns>找到面板时为 true。</returns>
    public bool TryGetPanelVisibility(string title, out bool visible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            if (string.Equals(panel.Title, title, StringComparison.Ordinal))
            {
                visible = panel.Visible;
                return true;
            }
        }

        visible = false;
        return false;
    }

    /// <summary>
    /// 尝试设置指定标题面板的可见性。
    /// </summary>
    /// <param name="title">面板标题。</param>
    /// <param name="visible">目标可见性。</param>
    /// <returns>找到并更新面板时为 true。</returns>
    public bool TrySetPanelVisibility(string title, bool visible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            if (string.Equals(panel.Title, title, StringComparison.Ordinal))
            {
                panel.Visible = visible;
                if (!visible && string.Equals(
                    _pendingPanelFocusId,
                    _panelIds[i],
                    StringComparison.Ordinal))
                {
                    _pendingPanelFocusId = null;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 将当前 dockspace 中注册的所有面板设为可见。
    /// </summary>
    /// <returns>被打开的面板数量。</returns>
    public int ShowAllPanels()
    {
        int count = 0;
        for (int i = 0; i < _panels.Count; i++)
        {
            if (!_panels[i].Visible)
            {
                _panels[i].Visible = true;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// 重置当前 Editor dockspace 布局并显示所有已注册面板。
    /// </summary>
    public void ResetDockLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized && Options.EnableDockSpace)
        {
            _controller.ResetDockLayout();
        }

        for (int i = 0; i < _panels.Count; i++)
        {
            _panels[i].Visible = _defaultPanelVisibility[i];
        }
    }

    /// <summary>
    /// 设置关闭 Editor backend 时是否保存当前布局。
    /// </summary>
    /// <param name="enabled">是否持久化布局。</param>
    public void SetLayoutPersistence(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetLayoutPersistence(enabled);
    }

    /// <summary>
    /// 初始化 Editor。禁用时不触碰 ImGui 后端。
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized || !Options.Enabled)
        {
            return;
        }

        _controller.Initialize();
        _initialized = true;
    }

    /// <summary>
    /// 绘制一帧 Editor UI。
    /// </summary>
    /// <param name="deltaSeconds">帧间隔秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <param name="frameIndex">当前帧索引。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        DrawFrame(
            deltaSeconds,
            width,
            height,
            counters,
            frameIndex,
            EditorPerformanceSnapshot.FromCounters(counters),
            framebufferScaleX,
            framebufferScaleY);
    }

    /// <summary>
    /// 绘制一帧 Editor UI。
    /// </summary>
    /// <param name="deltaSeconds">帧间隔秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <param name="frameIndex">当前帧索引。</param>
    /// <param name="performance">性能 HUD 只读诊断快照。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        EditorPerformanceSnapshot performance,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            return;
        }

        _controller.NewFrame(deltaSeconds, width, height, framebufferScaleX, framebufferScaleY);
        // ImGui 帧内顺序：Chrome → DockSpace 宿主 → 各可见面板 Draw → Render 提交 draw data。
        EditorContext context = new(counters, Selection, frameIndex, performance);
        if (Options.EnableDockSpace)
        {
            DrawPanels(in context, chromeOnly: true);
            _controller.DrawDockSpace();
        }

        if (Options.EnableDockSpace)
        {
            DrawPanels(in context, chromeOnly: false);
        }

        _controller.Render();
    }

    /// <summary>
    /// 绘制一帧 Editor UI，并在同一 ImGui frame 内调度脚本 GUI。
    /// </summary>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        EditorPerformanceSnapshot performance,
        Action<IGuiContext>? drawScriptGui,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            return;
        }

        _controller.NewFrame(deltaSeconds, width, height, framebufferScaleX, framebufferScaleY);
        EditorContext context = new(counters, Selection, frameIndex, performance);
        if (Options.EnableDockSpace)
        {
            DrawPanels(in context, chromeOnly: true);
            _controller.DrawDockSpace();
        }

        if (Options.EnableDockSpace)
        {
            DrawPanels(in context, chromeOnly: false);
        }

        if (drawScriptGui is not null)
        {
            _scriptGuiContext.ResetFrame(width, height, deltaSeconds, Input.Capture);
            drawScriptGui(_scriptGuiContext);
        }

        _controller.Render();
    }

    private void DrawPanels(in EditorContext context, bool chromeOnly)
    {
        IEditorPanel? maximizedPanel = chromeOnly ? null : ResolveMaximizedPanel();
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            bool isChrome = panel is IEditorChromePanel;
            if (panel.Visible &&
                isChrome == chromeOnly &&
                (maximizedPanel is null || ReferenceEquals(panel, maximizedPanel)))
            {
                if (string.Equals(_pendingPanelFocusId, _panelIds[i], StringComparison.Ordinal))
                {
                    ImGui.SetNextWindowFocus();
                    _pendingPanelFocusId = null;
                }

                panel.Draw(in context);
            }
        }
    }

    private IEditorPanel? ResolveMaximizedPanel()
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            if (panel.Visible && panel is IEditorMaximizedPanel { IsMaximized: true })
            {
                return panel;
            }
        }

        return null;
    }

    private int FindPanelIndex(string stableId)
    {
        ValidatePanelId(stableId);
        for (int i = 0; i < _panelIds.Count; i++)
        {
            if (string.Equals(_panelIds[i], stableId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private Dictionary<uint, string> CreateDockGroupIds(EditorDockWindowState[] states)
    {
        Dictionary<uint, List<string>> members = [];
        for (int i = 0; i < states.Length; i++)
        {
            uint dockId = states[i].DockId;
            if (dockId == 0)
            {
                continue;
            }

            if (!members.TryGetValue(dockId, out List<string>? ids))
            {
                ids = [];
                members.Add(dockId, ids);
            }

            ids.Add(_panelIds[i]);
        }

        Dictionary<uint, string> result = new(members.Count);
        foreach ((uint dockId, List<string> ids) in members)
        {
            ids.Sort(StringComparer.Ordinal);
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ids)));
            result.Add(dockId, $"dock-group:{Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant()}");
        }

        return result;
    }

    private static string CreateDefaultPanelId(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        System.Text.StringBuilder builder = new("editor.panel.");
        bool separatorPending = false;
        for (int i = 0; i < title.Length; i++)
        {
            char character = title[i];
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (separatorPending && builder[^1] != '.')
                {
                    _ = builder.Append('.');
                }

                _ = builder.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }

        string result = builder.ToString().TrimEnd('.');
        ValidatePanelId(result);
        return result;
    }

    private static void ValidatePanelId(string stableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        if (stableId.Length > 128 ||
            !char.IsAsciiLetter(stableId[0]) ||
            !stableId.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            throw new ArgumentException("Panel ID 必须是 1..128 字符的 ASCII semantic ID。", nameof(stableId));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            _controller.Shutdown();
            _initialized = false;
        }

        _disposed = true;
    }
}
