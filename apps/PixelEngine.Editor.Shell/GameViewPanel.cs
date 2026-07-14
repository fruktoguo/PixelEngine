using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using System.Globalization;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Game View ImGui 面板：用独立 presentation 显示运行时纹理，并提供 Unity 风格分辨率、缩放与最大化控制。
/// </summary>
internal sealed class GameViewPanel : IEditorMaximizedPanel
{
    private const string OverflowPopupName = "game-view-overflow";
    private const string CustomPresetPopupName = "game-view-custom-preset";
    private const float MinimumDisplayExtent = 1f;
    private const uint CheckerDark = 0xFF_20_22_26;
    private const uint CheckerLight = 0xFF_29_2C_31;
    private const uint DisplayBorder = 0xFF_4B_4F_58;
    private static readonly float[] ScaleValues = [0f, 25f, 50f, 75f, 100f, 200f];
    private static readonly string[] ScaleLabels = ["Fit", "25%", "50%", "75%", "100%", "200%"];

    private readonly Func<RenderViewportTexture> _textureProvider;
    private readonly Func<GamePresentationDescriptor> _descriptorProvider;
    private readonly Func<(int Width, int Height)> _playerDefaultSizeProvider;
    private readonly int _maximumTextureSize;
    private readonly EditorWorkspaceStore? _workspace;
    private readonly string? _projectPath;
    private readonly bool _requiresDescriptorMatch;
    private EditorGameViewCustomPreset[] _customPresets = [];
    private Vector2 _pan;
    private GamePresentationOverride _pendingPresentation;
    private long _requestRevision;
    private bool _hasPendingPresentation;
    private bool _focusRequested;
    private bool _panDirty;
    private bool _autoMaximizedOnPlay;
    private bool _customPopupRequested;
    private bool _editingExistingCustomPreset;
    private string _editingCustomPresetId = string.Empty;
    private string _customPresetName = "Custom";
    private int _customPresetWidth = 1280;
    private int _customPresetHeight = 720;
    private GameViewToolbarLayout _lastToolbarLayout;
    private float _lastToolbarAvailableWidth;
    private EditorMode _preparedMode = EditorMode.Edit;
    private EditorMode _lastPreparedMode = EditorMode.Edit;

    /// <summary>保留旧测试/宿主的一参数构造路径；纹理本身视为完整 world presentation。</summary>
    public GameViewPanel(Func<RenderViewportTexture> textureProvider)
        : this(
            textureProvider,
            descriptorProvider: static () => default,
            playerDefaultSizeProvider: static () => (640, 360),
            maximumTextureSize: int.MaxValue,
            workspace: null,
            projectPath: null,
            requiresDescriptorMatch: false)
    {
    }

    /// <summary>创建接入 Hosting presentation 协调器与用户 workspace 的 Game View。</summary>
    public GameViewPanel(
        Func<RenderViewportTexture> textureProvider,
        Func<GamePresentationDescriptor> descriptorProvider,
        Func<(int Width, int Height)> playerDefaultSizeProvider,
        int maximumTextureSize,
        EditorWorkspaceStore? workspace,
        string? projectPath)
        : this(
            textureProvider,
            descriptorProvider,
            playerDefaultSizeProvider,
            maximumTextureSize,
            workspace,
            projectPath,
            requiresDescriptorMatch: true)
    {
    }

    private GameViewPanel(
        Func<RenderViewportTexture> textureProvider,
        Func<GamePresentationDescriptor> descriptorProvider,
        Func<(int Width, int Height)> playerDefaultSizeProvider,
        int maximumTextureSize,
        EditorWorkspaceStore? workspace,
        string? projectPath,
        bool requiresDescriptorMatch)
    {
        _textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
        _descriptorProvider = descriptorProvider ?? throw new ArgumentNullException(nameof(descriptorProvider));
        _playerDefaultSizeProvider = playerDefaultSizeProvider ?? throw new ArgumentNullException(nameof(playerDefaultSizeProvider));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextureSize);
        _maximumTextureSize = maximumTextureSize;
        _workspace = workspace;
        _projectPath = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFullPath(projectPath);
        _requiresDescriptorMatch = requiresDescriptorMatch;
        LoadWorkspaceState();
    }

    public string Title => EditorDockSpace.GameViewWindowTitle;

    public bool Visible
    {
        get;
        set
        {
            field = value;
            if (!value)
            {
                IsMaximized = false;
                _autoMaximizedOnPlay = false;
                ClearInputState();
            }
        }
    } = true;

    /// <inheritdoc />
    public bool IsMaximized { get; private set; }

    /// <summary>Game View 窗口是否拥有 gameplay 键盘焦点。</summary>
    public bool KeyboardFocused { get; private set; }

    /// <summary>指针是否位于实际可见 presentation 图像内。</summary>
    public bool PointerHovered { get; private set; }

    /// <summary>toolbar 的 popup/input 是否正在消费键盘。</summary>
    public bool ToolbarCapturesInput { get; private set; }

    /// <summary>兼容旧调用方的键盘焦点别名。</summary>
    public bool InputFocused => KeyboardFocused;

    /// <summary>兼容旧调用方的图像 hover 别名。</summary>
    public bool InputHovered => PointerHovered;

    public Vector2 LastPointerPanelPoint { get; private set; }

    public Vector2 LastPanelOriginFramebuffer { get; private set; }

    public Vector2 LastFramebufferScale { get; private set; } = Vector2.One;

    public GameViewViewportSnapshot LastViewportSnapshot { get; private set; } = GameViewViewportSnapshot.Empty;

    internal string SelectedPresetId { get; private set; } = EditorGameViewWorkspaceState.DefaultPresetId;

    internal float ScalePercent { get; private set; }

    internal Vector2 Pan => _pan;

    internal bool MaximizeOnPlay { get; private set; }

    internal string LastDiagnostic { get; private set; } = string.Empty;

    /// <summary>捕获 toolbar 请求、已提交 presentation 与当前可见 viewport 的同帧探针快照。</summary>
    internal ScriptedGameViewPresentationSnapshot CaptureScriptedPresentationSnapshot()
    {
        GamePresentationDescriptor descriptor = _descriptorProvider();
        GameViewViewportSnapshot viewport = LastViewportSnapshot;
        return ScriptedGameViewPresentationSnapshot.Create(
            SelectedPresetId,
            ScalePercent,
            MaximizeOnPlay,
            IsMaximized,
            in descriptor,
            in viewport,
            LastFramebufferScale,
            _lastToolbarAvailableWidth,
            in _lastToolbarLayout);
    }

    public EditorViewportContract CaptureContract(EditorMode mode)
    {
        return EditorGameViewContract.GameView(mode);
    }

    public void RequestFocus()
    {
        _focusRequested = true;
    }

    /// <summary>在 panel Draw 之外推进 Play/Stop 生命周期，关闭 Game View 时也能恢复自动最大化。</summary>
    public void PrepareFrame(EditorMode mode)
    {
        _preparedMode = mode;
        if (_lastPreparedMode == EditorMode.Edit && mode == EditorMode.Play && MaximizeOnPlay && !IsMaximized)
        {
            IsMaximized = true;
            _autoMaximizedOnPlay = true;
            _focusRequested = true;
        }
        else if (_lastPreparedMode is EditorMode.Play or EditorMode.Paused && mode == EditorMode.Edit)
        {
            if (_autoMaximizedOnPlay)
            {
                IsMaximized = false;
            }

            _autoMaximizedOnPlay = false;
        }

        _lastPreparedMode = mode;
    }

    /// <summary>读取最近一份 panel 解析出的 pending presentation。</summary>
    public bool TryGetPendingPresentation(out GamePresentationOverride request)
    {
        request = _pendingPresentation;
        return _hasPendingPresentation;
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        ApplyMaximizedWindowPlacement();
        if (_focusRequested)
        {
            ImGui.SetNextWindowFocus();
            _focusRequested = false;
        }

        string visibleTitle = EditorLocalization.Get("window.game", "Game View");
        string windowTitle = IsMaximized
            ? $"{visibleTitle}###PixelEngineGameViewMaximized"
            : $"{visibleTitle}###{Title}";
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoNavInputs;
        if (IsMaximized)
        {
            flags |= ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoSavedSettings;
        }

        if (!ImGui.Begin(windowTitle, flags))
        {
            ClearInputState();
            ImGui.End();
            return;
        }

        HandleMaximizeShortcut();
        bool toolbarPopupOpen = DrawToolbar();
        ToolbarCapturesInput = ImGui.IsAnyItemActive() || toolbarPopupOpen;

        Vector2 displayMinScreen = ImGui.GetCursorScreenPos();
        Vector2 available = ImGui.GetContentRegionAvail();
        available = new Vector2(
            MathF.Max(MinimumDisplayExtent, available.X),
            MathF.Max(MinimumDisplayExtent, available.Y));
        Vector2 panelOriginScreen = ImGui.GetWindowPos();
        Vector2 framebufferScale = ImGui.GetIO().DisplayFramebufferScale;
        framebufferScale = new Vector2(NormalizeScale(framebufferScale.X), NormalizeScale(framebufferScale.Y));
        LastFramebufferScale = framebufferScale;
        LastPanelOriginFramebuffer = panelOriginScreen * framebufferScale;

        int availableFramebufferWidth = Math.Max(1, (int)MathF.Floor(available.X * framebufferScale.X));
        int availableFramebufferHeight = Math.Max(1, (int)MathF.Floor(available.Y * framebufferScale.Y));
        ResolvePendingPresentation(availableFramebufferWidth, availableFramebufferHeight);

        ImGui.SetCursorScreenPos(displayMinScreen);
        _ = ImGui.InvisibleButton("##game-view-display", available);
        bool displayHovered = ImGui.IsItemHovered();
        Vector2 mousePanel = ImGui.GetIO().MousePos - panelOriginScreen;
        LastPointerPanelPoint = mousePanel;

        RenderViewportTexture texture = _textureProvider();
        if (!TryResolveTextureDescriptor(texture, out PresentationViewport worldContentRect, out long revision))
        {
            LastViewportSnapshot = GameViewViewportSnapshot.Empty;
            PointerHovered = false;
            KeyboardFocused = ImGui.IsWindowFocused() && !ToolbarCapturesInput;
            DrawWaitingSurface(displayMinScreen, available, texture.IsValid);
            ImGui.End();
            return;
        }

        GameViewViewportSnapshot snapshot = CreateSnapshot(
            texture,
            revision,
            in worldContentRect,
            displayMinScreen - panelOriginScreen,
            available,
            framebufferScale);
        if (ScalePercent > 0f && displayHovered && ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            Vector2 delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
            {
                _pan -= new Vector2(
                    delta.X / MathF.Max(float.Epsilon, snapshot.DisplayScale.X),
                    delta.Y / MathF.Max(float.Epsilon, snapshot.DisplayScale.Y));
                snapshot = CreateSnapshot(
                    texture,
                    revision,
                    in worldContentRect,
                    displayMinScreen - panelOriginScreen,
                    available,
                    framebufferScale);
                _pan = snapshot.Pan;
                _panDirty = true;
            }
        }

        if (_panDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
        {
            _panDirty = false;
            PersistWorkspaceState();
        }

        LastViewportSnapshot = snapshot;
        PointerHovered = displayHovered && snapshot.ContainsPanelPoint(mousePanel);
        KeyboardFocused = ImGui.IsWindowFocused() && !ToolbarCapturesInput;
        DrawPresentation(texture, in snapshot, panelOriginScreen);
        ImGui.End();
    }

    internal void SelectPreset(string presetId)
    {
        if (!GameViewPresentationPreset.TryResolve(presetId, _customPresets, out _))
        {
            throw new ArgumentException("未知 Game View preset。", nameof(presetId));
        }

        if (string.Equals(SelectedPresetId, presetId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedPresetId = presetId;
        _pan = Vector2.Zero;
        PersistWorkspaceState();
    }

    internal void SetScalePercent(float scalePercent)
    {
        if (!float.IsFinite(scalePercent) || scalePercent is < 0f or > 2000f)
        {
            throw new ArgumentOutOfRangeException(nameof(scalePercent));
        }

        if (MathF.Abs(ScalePercent - scalePercent) < 0.001f)
        {
            return;
        }

        ScalePercent = scalePercent;
        _pan = Vector2.Zero;
        PersistWorkspaceState();
    }

    internal void SetMaximizeOnPlay(bool enabled)
    {
        if (MaximizeOnPlay == enabled)
        {
            return;
        }

        MaximizeOnPlay = enabled;
        if (enabled && _preparedMode == EditorMode.Play && !IsMaximized)
        {
            IsMaximized = true;
            _autoMaximizedOnPlay = true;
            _focusRequested = true;
        }

        PersistWorkspaceState();
    }

    internal void ToggleMaximized()
    {
        IsMaximized = !IsMaximized;
        _autoMaximizedOnPlay = false;
        _focusRequested = true;
        ClearInputState();
    }

    private void ApplyMaximizedWindowPlacement()
    {
        if (!IsMaximized)
        {
            return;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.WorkSize, ImGuiCond.Always);
    }

    private void HandleMaximizeShortcut()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (ImGui.IsWindowFocused() && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.Space))
        {
            ToggleMaximized();
        }
    }

    private bool DrawToolbar()
    {
        float available = ImGui.GetContentRegionAvail().X;
        ImGuiStylePtr style = ImGui.GetStyle();
        float horizontalFramePadding = style.FramePadding.X * 2f;
        float comboArrowWidth = ImGui.GetFrameHeight();
        float widestScaleLabel = 0f;
        for (int i = 0; i < ScaleLabels.Length; i++)
        {
            widestScaleLabel = MathF.Max(widestScaleLabel, ImGui.CalcTextSize(ScaleLabels[i]).X);
        }

        float measuredScaleWidth = widestScaleLabel + comboArrowWidth + horizontalFramePadding;
        float compactScaleWidth = MathF.Max(72f, measuredScaleWidth);
        float fullScaleWidth = MathF.Max(88f, measuredScaleWidth);
        string fullMaximizeLabel = IsMaximized ? "Restore" : "Maximize";
        string compactMaximizeLabel = IsMaximized ? "Restore" : "Max";
        float fullMaximizeWidth = ImGui.CalcTextSize(fullMaximizeLabel).X + horizontalFramePadding;
        float compactMaximizeWidth = ImGui.CalcTextSize(compactMaximizeLabel).X + horizontalFramePadding;
        float overflowWidth = MathF.Max(
            ImGui.GetFrameHeight(),
            ImGui.CalcTextSize("...").X + horizontalFramePadding);
        float maximizeOnPlayWidth = ImGui.GetFrameHeight() +
            style.ItemInnerSpacing.X +
            ImGui.CalcTextSize("Maximize On Play").X;
        float minimumPresetWidth = MathF.Max(
            96f,
            ImGui.CalcTextSize("Free Aspect").X + comboArrowWidth + horizontalFramePadding);
        float fullPresetMinimumWidth = MathF.Max(140f, minimumPresetWidth);
        GameViewToolbarMetrics metrics = new(
            style.ItemSpacing.X,
            overflowWidth,
            compactScaleWidth,
            fullScaleWidth,
            compactMaximizeWidth,
            fullMaximizeWidth,
            maximizeOnPlayWidth,
            minimumPresetWidth,
            fullPresetMinimumWidth,
            MathF.Max(260f, fullPresetMinimumWidth));
        GameViewToolbarLayout layout = ResolveToolbarLayout(available, in metrics);
        _lastToolbarAvailableWidth = available;
        _lastToolbarLayout = layout;
        _ = GameViewPresentationPreset.TryResolve(SelectedPresetId, _customPresets, out GameViewPresentationPreset selected);
        bool popupOpen = false;
        bool drewControl = false;

        if (layout.ShowPreset)
        {
            ImGui.SetNextItemWidth(layout.PresetWidth);
            if (ImGui.BeginCombo("##game-view-preset", selected.Label))
            {
                popupOpen = true;
                DrawPresetChoices(GameViewPresentationPreset.BuiltIns);
                if (_customPresets.Length != 0)
                {
                    ImGui.Separator();
                    for (int i = 0; i < _customPresets.Length; i++)
                    {
                        EditorGameViewCustomPreset custom = _customPresets[i];
                        DrawPresetChoice(new GameViewPresentationPreset(
                            custom.Id,
                            custom.Name,
                            GameViewPresentationPresetKind.FixedResolution,
                            custom.Width,
                            custom.Height));
                    }
                }

                ImGui.EndCombo();
            }

            drewControl = true;
        }

        if (layout.ShowScale)
        {
            DrawNextToolbarControl(drewControl);
            ImGui.SetNextItemWidth(layout.ScaleWidth);
            if (ImGui.BeginCombo("##game-view-scale", FormatScale(ScalePercent)))
            {
                popupOpen = true;
                for (int i = 0; i < ScaleValues.Length; i++)
                {
                    bool selectedScale = MathF.Abs(ScalePercent - ScaleValues[i]) < 0.001f;
                    if (ImGui.Selectable(ScaleLabels[i], selectedScale))
                    {
                        SetScalePercent(ScaleValues[i]);
                    }

                    if (selectedScale)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            drewControl = true;
        }

        if (layout.ShowMaximize)
        {
            DrawNextToolbarControl(drewControl);
            string label = layout.Density == GameViewToolbarDensity.Full
                ? fullMaximizeLabel
                : compactMaximizeLabel;
            if (ImGui.Button($"{label}##game-view-maximize", new Vector2(layout.MaximizeWidth, 0f)))
            {
                ToggleMaximized();
            }

            drewControl = true;
        }

        if (layout.ShowMaximizeOnPlay)
        {
            DrawNextToolbarControl(drewControl);
            bool maximizeOnPlay = MaximizeOnPlay;
            if (ImGui.Checkbox("Maximize On Play##game-view-max-on-play", ref maximizeOnPlay))
            {
                SetMaximizeOnPlay(maximizeOnPlay);
            }

            drewControl = true;
        }

        DrawNextToolbarControl(drewControl);
        if (ImGui.Button("...##game-view-overflow-button", new Vector2(layout.OverflowWidth, 0f)))
        {
            ImGui.OpenPopup(OverflowPopupName);
        }

        popupOpen |= DrawOverflowPopup(in layout, in selected);
        popupOpen |= DrawCustomPresetPopup();
        ImGui.Separator();
        return popupOpen;
    }

    private static void DrawNextToolbarControl(bool hasPreviousControl)
    {
        if (hasPreviousControl)
        {
            ImGui.SameLine();
        }
    }

    /// <summary>
    /// 按当前字体与 ImGui spacing 的实测尺寸选择工具栏密度；任何降级都先保留 overflow，
    /// 避免窄 dock 把最后一个入口裁掉后令隐藏功能无法访问。
    /// </summary>
    internal static GameViewToolbarLayout ResolveToolbarLayout(
        float availableWidth,
        in GameViewToolbarMetrics metrics)
    {
        if (!metrics.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Game View toolbar 尺寸必须是有限正值、spacing 非负且 preset 宽度范围有效。");
        }

        float available = float.IsFinite(availableWidth) && availableWidth > 0f
            ? availableWidth
            : 1f;
        float fullRequired = metrics.FullPresetMinimumWidth +
            metrics.FullScaleWidth +
            metrics.FullMaximizeWidth +
            metrics.MaximizeOnPlayWidth +
            metrics.OverflowWidth +
            (metrics.ItemSpacing * 4f);
        if (available >= fullRequired)
        {
            float presetWidth = Math.Clamp(
                available - fullRequired + metrics.FullPresetMinimumWidth,
                metrics.FullPresetMinimumWidth,
                metrics.MaximumPresetWidth);
            return new GameViewToolbarLayout(
                GameViewToolbarDensity.Full,
                ShowPreset: true,
                ShowScale: true,
                ShowMaximize: true,
                ShowMaximizeOnPlay: true,
                presetWidth,
                metrics.FullScaleWidth,
                metrics.FullMaximizeWidth,
                metrics.MaximizeOnPlayWidth,
                metrics.OverflowWidth,
                metrics.ItemSpacing);
        }

        float compactRequired = metrics.MinimumPresetWidth +
            metrics.CompactScaleWidth +
            metrics.CompactMaximizeWidth +
            metrics.OverflowWidth +
            (metrics.ItemSpacing * 3f);
        if (available >= compactRequired)
        {
            float presetWidth = Math.Clamp(
                available - compactRequired + metrics.MinimumPresetWidth,
                metrics.MinimumPresetWidth,
                metrics.MaximumPresetWidth);
            return new GameViewToolbarLayout(
                GameViewToolbarDensity.Compact,
                ShowPreset: true,
                ShowScale: true,
                ShowMaximize: true,
                ShowMaximizeOnPlay: false,
                presetWidth,
                metrics.CompactScaleWidth,
                metrics.CompactMaximizeWidth,
                MaximizeOnPlayWidth: 0f,
                metrics.OverflowWidth,
                metrics.ItemSpacing);
        }

        float narrowRequired = metrics.MinimumPresetWidth + metrics.OverflowWidth + metrics.ItemSpacing;
        if (available >= narrowRequired)
        {
            float presetWidth = Math.Clamp(
                available - metrics.OverflowWidth - metrics.ItemSpacing,
                metrics.MinimumPresetWidth,
                metrics.MaximumPresetWidth);
            return new GameViewToolbarLayout(
                GameViewToolbarDensity.Narrow,
                ShowPreset: true,
                ShowScale: false,
                ShowMaximize: false,
                ShowMaximizeOnPlay: false,
                presetWidth,
                ScaleWidth: 0f,
                MaximizeWidth: 0f,
                MaximizeOnPlayWidth: 0f,
                metrics.OverflowWidth,
                metrics.ItemSpacing);
        }

        return new GameViewToolbarLayout(
            GameViewToolbarDensity.OverflowOnly,
            ShowPreset: false,
            ShowScale: false,
            ShowMaximize: false,
            ShowMaximizeOnPlay: false,
            PresetWidth: 0f,
            ScaleWidth: 0f,
            MaximizeWidth: 0f,
            MaximizeOnPlayWidth: 0f,
            OverflowWidth: MathF.Min(metrics.OverflowWidth, available),
            metrics.ItemSpacing);
    }

    private void DrawPresetChoices(ReadOnlySpan<GameViewPresentationPreset> presets)
    {
        for (int i = 0; i < presets.Length; i++)
        {
            DrawPresetChoice(presets[i]);
        }
    }

    private void DrawPresetChoice(in GameViewPresentationPreset preset)
    {
        bool selected = string.Equals(SelectedPresetId, preset.Id, StringComparison.Ordinal);
        if (ImGui.Selectable(preset.Label, selected))
        {
            SelectPreset(preset.Id);
        }

        if (selected)
        {
            ImGui.SetItemDefaultFocus();
        }
    }

    private bool DrawOverflowPopup(
        in GameViewToolbarLayout layout,
        in GameViewPresentationPreset selected)
    {
        if (!ImGui.BeginPopup(OverflowPopupName))
        {
            return false;
        }

        bool hasResponsiveActions = false;
        if (!layout.ShowPreset)
        {
            hasResponsiveActions = true;
            if (ImGui.BeginMenu($"Resolution: {selected.Label}"))
            {
                DrawPresetMenuChoices(GameViewPresentationPreset.BuiltIns);
                if (_customPresets.Length != 0)
                {
                    ImGui.Separator();
                    for (int i = 0; i < _customPresets.Length; i++)
                    {
                        EditorGameViewCustomPreset custom = _customPresets[i];
                        DrawPresetMenuChoice(new GameViewPresentationPreset(
                            custom.Id,
                            custom.Name,
                            GameViewPresentationPresetKind.FixedResolution,
                            custom.Width,
                            custom.Height));
                    }
                }

                ImGui.EndMenu();
            }
        }

        if (!layout.ShowScale)
        {
            hasResponsiveActions = true;
            if (ImGui.BeginMenu($"Scale: {FormatScale(ScalePercent)}"))
            {
                for (int i = 0; i < ScaleValues.Length; i++)
                {
                    bool selectedScale = MathF.Abs(ScalePercent - ScaleValues[i]) < 0.001f;
                    if (ImGui.MenuItem(ScaleLabels[i], string.Empty, selectedScale, enabled: true))
                    {
                        SetScalePercent(ScaleValues[i]);
                    }
                }

                ImGui.EndMenu();
            }
        }

        if (!layout.ShowMaximize)
        {
            hasResponsiveActions = true;
            if (ImGui.MenuItem(IsMaximized ? "Restore Game View" : "Maximize Game View"))
            {
                ToggleMaximized();
            }
        }

        if (!layout.ShowMaximizeOnPlay)
        {
            hasResponsiveActions = true;
            bool maximizeOnPlay = MaximizeOnPlay;
            if (ImGui.MenuItem("Maximize On Play", string.Empty, ref maximizeOnPlay))
            {
                SetMaximizeOnPlay(maximizeOnPlay);
            }
        }

        if (hasResponsiveActions)
        {
            ImGui.Separator();
        }

        if (ImGui.MenuItem("Add Custom Resolution..."))
        {
            BeginCustomPresetEdit(null);
        }

        if (selected.Kind == GameViewPresentationPresetKind.FixedResolution &&
            IsCustomPreset(selected.Id))
        {
            if (ImGui.MenuItem("Edit Selected Resolution..."))
            {
                BeginCustomPresetEdit(selected.Id);
            }

            if (ImGui.MenuItem("Delete Selected Resolution"))
            {
                DeleteCustomPreset(selected.Id);
            }
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Fit Scale"))
        {
            SetScalePercent(0f);
        }

        if (ImGui.MenuItem("100% Pixel Scale"))
        {
            SetScalePercent(100f);
        }

        ImGui.EndPopup();
        return true;
    }

    private void DrawPresetMenuChoices(ReadOnlySpan<GameViewPresentationPreset> presets)
    {
        for (int i = 0; i < presets.Length; i++)
        {
            DrawPresetMenuChoice(presets[i]);
        }
    }

    private void DrawPresetMenuChoice(in GameViewPresentationPreset preset)
    {
        bool selected = string.Equals(SelectedPresetId, preset.Id, StringComparison.Ordinal);
        if (ImGui.MenuItem(preset.Label, string.Empty, selected, enabled: true))
        {
            SelectPreset(preset.Id);
        }
    }

    private void BeginCustomPresetEdit(string? presetId)
    {
        _editingExistingCustomPreset = false;
        _editingCustomPresetId = string.Empty;
        _customPresetName = "Custom";
        (_customPresetWidth, _customPresetHeight) = NormalizePlayerDefaultSize();
        if (presetId is not null)
        {
            for (int i = 0; i < _customPresets.Length; i++)
            {
                EditorGameViewCustomPreset preset = _customPresets[i];
                if (!string.Equals(preset.Id, presetId, StringComparison.Ordinal))
                {
                    continue;
                }

                _editingExistingCustomPreset = true;
                _editingCustomPresetId = preset.Id;
                _customPresetName = preset.Name;
                _customPresetWidth = preset.Width;
                _customPresetHeight = preset.Height;
                break;
            }
        }

        _customPopupRequested = true;
    }

    private bool DrawCustomPresetPopup()
    {
        if (_customPopupRequested)
        {
            ImGui.OpenPopup(CustomPresetPopupName);
            _customPopupRequested = false;
        }

        if (!ImGui.BeginPopup(CustomPresetPopupName))
        {
            return false;
        }

        ImGui.TextUnformatted(_editingExistingCustomPreset ? "Edit Custom Resolution" : "Add Custom Resolution");
        ImGui.Separator();
        ImGui.SetNextItemWidth(240f);
        _ = ImGui.InputText("Name", ref _customPresetName, 96);
        ImGui.SetNextItemWidth(160f);
        _ = ImGui.InputInt("Width", ref _customPresetWidth);
        ImGui.SetNextItemWidth(160f);
        _ = ImGui.InputInt("Height", ref _customPresetHeight);
        if (!string.IsNullOrEmpty(LastDiagnostic))
        {
            ImGui.TextColored(new Vector4(0.95f, 0.60f, 0.30f, 1f), LastDiagnostic);
        }

        bool valid = !string.IsNullOrWhiteSpace(_customPresetName) &&
            _customPresetWidth > 0 &&
            _customPresetHeight > 0 &&
            _customPresetWidth <= _maximumTextureSize &&
            _customPresetHeight <= _maximumTextureSize;
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button(_editingExistingCustomPreset ? "Save" : "Add"))
        {
            SaveCustomPreset();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            LastDiagnostic = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return true;
    }

    private void SaveCustomPreset()
    {
        string id = _editingExistingCustomPreset
            ? _editingCustomPresetId
            : $"custom-{Guid.NewGuid():N}";
        EditorGameViewCustomPreset next = new()
        {
            Id = id,
            Name = _customPresetName.Trim(),
            Width = _customPresetWidth,
            Height = _customPresetHeight,
        };
        List<EditorGameViewCustomPreset> presets = new(_customPresets.Length + 1);
        bool replaced = false;
        for (int i = 0; i < _customPresets.Length; i++)
        {
            if (string.Equals(_customPresets[i].Id, id, StringComparison.Ordinal))
            {
                presets.Add(next);
                replaced = true;
            }
            else
            {
                presets.Add(_customPresets[i]);
            }
        }

        if (!replaced)
        {
            presets.Add(next);
        }

        _customPresets = [.. presets];
        SelectedPresetId = id;
        _pan = Vector2.Zero;
        LastDiagnostic = string.Empty;
        PersistWorkspaceState();
    }

    private void DeleteCustomPreset(string id)
    {
        List<EditorGameViewCustomPreset> presets = new(_customPresets.Length);
        for (int i = 0; i < _customPresets.Length; i++)
        {
            if (!string.Equals(_customPresets[i].Id, id, StringComparison.Ordinal))
            {
                presets.Add(_customPresets[i]);
            }
        }

        _customPresets = [.. presets];
        if (string.Equals(SelectedPresetId, id, StringComparison.Ordinal))
        {
            SelectedPresetId = EditorGameViewWorkspaceState.DefaultPresetId;
            _pan = Vector2.Zero;
        }

        PersistWorkspaceState();
    }

    private bool IsCustomPreset(string id)
    {
        for (int i = 0; i < _customPresets.Length; i++)
        {
            if (string.Equals(_customPresets[i].Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ResolvePendingPresentation(int availableFramebufferWidth, int availableFramebufferHeight)
    {
        if (!GameViewPresentationPreset.TryResolve(
                SelectedPresetId,
                _customPresets,
                out GameViewPresentationPreset preset))
        {
            SelectedPresetId = EditorGameViewWorkspaceState.DefaultPresetId;
            preset = GameViewPresentationPreset.BuiltIns[0];
        }

        (int playerDefaultWidth, int playerDefaultHeight) = NormalizePlayerDefaultSize();
        long candidateRevision = _requestRevision + 1;
        if (!GameViewPresentationResolver.TryResolve(
                in preset,
                playerDefaultWidth,
                playerDefaultHeight,
                availableFramebufferWidth,
                availableFramebufferHeight,
                _maximumTextureSize,
                candidateRevision,
                out GamePresentationOverride candidate,
                out string diagnostic))
        {
            LastDiagnostic = diagnostic;
            return;
        }

        bool changed = !_hasPendingPresentation ||
            candidate.Width != _pendingPresentation.Width ||
            candidate.Height != _pendingPresentation.Height ||
            candidate.Source != _pendingPresentation.Source;
        if (changed)
        {
            _requestRevision = candidateRevision;
            _pendingPresentation = candidate;
            _hasPendingPresentation = true;
        }

        LastDiagnostic = string.Empty;
    }

    private (int Width, int Height) NormalizePlayerDefaultSize()
    {
        (int width, int height) = _playerDefaultSizeProvider();
        return (
            Math.Clamp(width, 1, _maximumTextureSize),
            Math.Clamp(height, 1, _maximumTextureSize));
    }

    private bool TryResolveTextureDescriptor(
        in RenderViewportTexture texture,
        out PresentationViewport worldContentRect,
        out long revision)
    {
        worldContentRect = default;
        revision = 0;
        if (!texture.IsValid)
        {
            return false;
        }

        if (!_requiresDescriptorMatch)
        {
            worldContentRect = PresentationViewport.Fit(
                texture.Width,
                texture.Height,
                texture.Width,
                texture.Height);
            revision = texture.Revision;
            return true;
        }

        GamePresentationDescriptor descriptor = _descriptorProvider();
        if (!descriptor.IsValid ||
            descriptor.PresentationWidth != texture.Width ||
            descriptor.PresentationHeight != texture.Height ||
            descriptor.PresentationRevision != texture.Revision)
        {
            LastDiagnostic = descriptor.IsValid
                ? "Game View 正在等待同一 revision 的 presentation texture。"
                : "Game View presentation 尚未提交。";
            return false;
        }

        worldContentRect = descriptor.WorldContentRect;
        revision = descriptor.PresentationRevision;
        LastDiagnostic = string.Empty;
        return true;
    }

    private GameViewViewportSnapshot CreateSnapshot(
        in RenderViewportTexture texture,
        long revision,
        in PresentationViewport worldContentRect,
        Vector2 imageMinPanel,
        Vector2 available,
        Vector2 framebufferScale)
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            texture.Width,
            texture.Height,
            revision,
            in worldContentRect,
            imageMinPanel,
            available,
            framebufferScale,
            ScalePercent,
            _pan);
        _pan = snapshot.Pan;
        return snapshot;
    }

    private static void DrawPresentation(
        in RenderViewportTexture texture,
        in GameViewViewportSnapshot snapshot,
        Vector2 panelOriginScreen)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 displayMin = panelOriginScreen + snapshot.DisplayAreaRect.Position;
        Vector2 displayMax = displayMin + snapshot.DisplayAreaRect.Size;
        DrawCheckerboard(drawList, displayMin, displayMax);
        drawList.PushClipRect(displayMin, displayMax, true);
        Vector2 imageMin = panelOriginScreen + snapshot.ImageRect.Position;
        Vector2 imageMax = imageMin + snapshot.ImageRect.Size;
        drawList.AddImage(
            ViewportPanel.CreateTextureRef(texture.Handle),
            imageMin,
            imageMax,
            new Vector2(0f, 1f),
            new Vector2(1f, 0f),
            0xFFFFFFFF);
        drawList.PopClipRect();
        drawList.AddRect(displayMin, displayMax, DisplayBorder);
    }

    private void DrawWaitingSurface(Vector2 min, Vector2 size, bool textureValid)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 max = min + size;
        DrawCheckerboard(drawList, min, max);
        string message = textureValid
            ? LastDiagnostic
            : EditorLocalization.Get("game.waiting", "Waiting for the Game View texture");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "Waiting for presentation";
        }

        Vector2 textSize = ImGui.CalcTextSize(message);
        drawList.AddText(min + ((size - textSize) * 0.5f), 0xFF_C8_CB_D1, message);
        drawList.AddRect(min, max, DisplayBorder);
    }

    private static void DrawCheckerboard(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        const float Cell = 16f;
        drawList.AddRectFilled(min, max, CheckerDark);
        int row = 0;
        for (float y = min.Y; y < max.Y; y += Cell, row++)
        {
            int column = 0;
            for (float x = min.X; x < max.X; x += Cell, column++)
            {
                if (((row + column) & 1) == 0)
                {
                    continue;
                }

                drawList.AddRectFilled(
                    new Vector2(x, y),
                    new Vector2(MathF.Min(max.X, x + Cell), MathF.Min(max.Y, y + Cell)),
                    CheckerLight);
            }
        }
    }

    private void LoadWorkspaceState()
    {
        if (_workspace is null || _projectPath is null ||
            !_workspace.TryGetGameViewState(_projectPath, out EditorGameViewWorkspaceState state))
        {
            return;
        }

        SelectedPresetId = state.PresetId;
        ScalePercent = state.ScalePercent;
        _pan = new Vector2(state.PanX, state.PanY);
        MaximizeOnPlay = state.MaximizeOnPlay;
        _customPresets = state.CustomPresets;
    }

    private void PersistWorkspaceState()
    {
        if (_workspace is null || _projectPath is null)
        {
            return;
        }

        EditorGameViewWorkspaceState state = new()
        {
            PresetId = SelectedPresetId,
            ScalePercent = ScalePercent,
            PanX = _pan.X,
            PanY = _pan.Y,
            MaximizeOnPlay = MaximizeOnPlay,
            CustomPresets = _customPresets,
        };
        if (!_workspace.TrySetGameViewState(_projectPath, state, out string diagnostic))
        {
            LastDiagnostic = diagnostic;
        }
    }

    private void ClearInputState()
    {
        KeyboardFocused = false;
        PointerHovered = false;
        ToolbarCapturesInput = false;
        LastPointerPanelPoint = default;
        LastPanelOriginFramebuffer = default;
        LastFramebufferScale = Vector2.One;
        LastViewportSnapshot = GameViewViewportSnapshot.Empty;
        _lastToolbarAvailableWidth = 0f;
        _lastToolbarLayout = default;
    }

    private static string FormatScale(float scalePercent)
    {
        return scalePercent <= 0f
            ? "Fit"
            : string.Create(CultureInfo.InvariantCulture, $"{scalePercent:0.#}%");
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}

/// <summary>Game View 工具栏响应式密度。</summary>
internal enum GameViewToolbarDensity
{
    Full,
    Compact,
    Narrow,
    OverflowOnly,
}

/// <summary>由当前 ImGui 字体、padding 与 spacing 计算的工具栏控件尺寸。</summary>
internal readonly record struct GameViewToolbarMetrics(
    float ItemSpacing,
    float OverflowWidth,
    float CompactScaleWidth,
    float FullScaleWidth,
    float CompactMaximizeWidth,
    float FullMaximizeWidth,
    float MaximizeOnPlayWidth,
    float MinimumPresetWidth,
    float FullPresetMinimumWidth,
    float MaximumPresetWidth)
{
    internal bool IsValid =>
        IsFiniteNonNegative(ItemSpacing) &&
        IsFinitePositive(OverflowWidth) &&
        IsFinitePositive(CompactScaleWidth) &&
        IsFinitePositive(FullScaleWidth) &&
        IsFinitePositive(CompactMaximizeWidth) &&
        IsFinitePositive(FullMaximizeWidth) &&
        IsFinitePositive(MaximizeOnPlayWidth) &&
        IsFinitePositive(MinimumPresetWidth) &&
        IsFinitePositive(FullPresetMinimumWidth) &&
        IsFinitePositive(MaximumPresetWidth) &&
        CompactScaleWidth <= FullScaleWidth &&
        MinimumPresetWidth <= FullPresetMinimumWidth &&
        FullPresetMinimumWidth <= MaximumPresetWidth;

    private static bool IsFinitePositive(float value)
    {
        return float.IsFinite(value) && value > 0f;
    }

    private static bool IsFiniteNonNegative(float value)
    {
        return float.IsFinite(value) && value >= 0f;
    }
}

/// <summary>单帧 Game View 工具栏布局；<see cref="OccupiedWidth"/> 不得超过可用宽度。</summary>
internal readonly record struct GameViewToolbarLayout(
    GameViewToolbarDensity Density,
    bool ShowPreset,
    bool ShowScale,
    bool ShowMaximize,
    bool ShowMaximizeOnPlay,
    float PresetWidth,
    float ScaleWidth,
    float MaximizeWidth,
    float MaximizeOnPlayWidth,
    float OverflowWidth,
    float ItemSpacing)
{
    internal float OccupiedWidth
    {
        get
        {
            int controlCount = 1;
            float width = OverflowWidth;
            if (ShowPreset)
            {
                controlCount++;
                width += PresetWidth;
            }

            if (ShowScale)
            {
                controlCount++;
                width += ScaleWidth;
            }

            if (ShowMaximize)
            {
                controlCount++;
                width += MaximizeWidth;
            }

            if (ShowMaximizeOnPlay)
            {
                controlCount++;
                width += MaximizeOnPlayWidth;
            }

            return width + (ItemSpacing * (controlCount - 1));
        }
    }
}

/// <summary>
/// Game View 真实窗口探针的原子 presentation 快照；请求 preset、Hosting commit 与面板纹理 revision 必须一致。
/// </summary>
internal readonly record struct ScriptedGameViewPresentationSnapshot(
    bool IsSynchronized,
    string PresetId,
    float ScalePercent,
    bool MaximizeOnPlay,
    bool IsMaximized,
    GamePresentationSource Source,
    int PresentationWidth,
    int PresentationHeight,
    long PresentationRevision,
    PresentationViewport WorldContentRect,
    GameViewRect DisplayAreaRect,
    GameViewRect ImageRect,
    GameViewRect VisibleViewportRect,
    Vector2 FramebufferScale,
    GameViewToolbarDensity ToolbarDensity,
    bool ToolbarFits,
    float ToolbarAvailableWidth,
    float ToolbarOccupiedWidth,
    bool ToolbarOverflowVisible)
{
    public static ScriptedGameViewPresentationSnapshot Missing => new(
        IsSynchronized: false,
        PresetId: string.Empty,
        ScalePercent: 0f,
        MaximizeOnPlay: false,
        IsMaximized: false,
        Source: default,
        PresentationWidth: 0,
        PresentationHeight: 0,
        PresentationRevision: 0,
        WorldContentRect: default,
        DisplayAreaRect: default,
        ImageRect: default,
        VisibleViewportRect: default,
        FramebufferScale: Vector2.One,
        ToolbarDensity: GameViewToolbarDensity.OverflowOnly,
        ToolbarFits: false,
        ToolbarAvailableWidth: 0f,
        ToolbarOccupiedWidth: 0f,
        ToolbarOverflowVisible: false);

    public static ScriptedGameViewPresentationSnapshot Create(
        string presetId,
        float scalePercent,
        bool maximizeOnPlay,
        bool isMaximized,
        in GamePresentationDescriptor descriptor,
        in GameViewViewportSnapshot viewport,
        Vector2 framebufferScale)
    {
        GameViewToolbarLayout toolbarLayout = default;
        return Create(
            presetId,
            scalePercent,
            maximizeOnPlay,
            isMaximized,
            in descriptor,
            in viewport,
            framebufferScale,
            toolbarAvailableWidth: 0f,
            in toolbarLayout);
    }

    public static ScriptedGameViewPresentationSnapshot Create(
        string presetId,
        float scalePercent,
        bool maximizeOnPlay,
        bool isMaximized,
        in GamePresentationDescriptor descriptor,
        in GameViewViewportSnapshot viewport,
        Vector2 framebufferScale,
        float toolbarAvailableWidth,
        in GameViewToolbarLayout toolbarLayout)
    {
        bool synchronized = descriptor.IsValid &&
            viewport.IsValid &&
            viewport.TextureWidth == descriptor.PresentationWidth &&
            viewport.TextureHeight == descriptor.PresentationHeight &&
            viewport.PresentationRevision == descriptor.PresentationRevision &&
            viewport.WorldContentRect == descriptor.WorldContentRect;
        float toolbarOccupiedWidth = toolbarLayout.OccupiedWidth;
        bool toolbarOverflowVisible = toolbarLayout.OverflowWidth > 0f;
        bool toolbarFits = float.IsFinite(toolbarAvailableWidth) &&
            toolbarAvailableWidth > 0f &&
            toolbarOverflowVisible &&
            toolbarOccupiedWidth <= toolbarAvailableWidth + 0.01f;
        return new ScriptedGameViewPresentationSnapshot(
            synchronized,
            presetId,
            scalePercent,
            maximizeOnPlay,
            isMaximized,
            descriptor.Source,
            descriptor.PresentationWidth,
            descriptor.PresentationHeight,
            descriptor.PresentationRevision,
            descriptor.WorldContentRect,
            viewport.DisplayAreaRect,
            viewport.ImageRect,
            viewport.VisibleViewportRect,
            framebufferScale,
            toolbarLayout.Density,
            toolbarFits,
            toolbarAvailableWidth,
            toolbarOccupiedWidth,
            toolbarOverflowVisible);
    }
}
