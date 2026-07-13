using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Settings;

/// <summary>
/// Player Settings ImGui 面板。
/// </summary>
internal sealed class PlayerSettingsPanel : IEditorPanel
{
    public const string PanelTitle = EditorDockSpace.PlayerSettingsWindowTitle;
    private static readonly UiBackendKind[] UiBackendOptions = [UiBackendKind.ManagedFallback, UiBackendKind.RmlUi, UiBackendKind.Ultralight];
    private static readonly string[] UiBackendLabels = [.. UiBackendOptions.Select(UltralightOptionalProfileGate.GetDisplayLabel)];
    private static readonly PlayerReleaseChannel[] ReleaseOptions = [PlayerReleaseChannel.Development, PlayerReleaseChannel.Production];
    private static readonly string[] ReleaseLabels = ["Development", "Production"];
    private readonly PlayerSettingsStore _store;
    private readonly Func<float> _uiScaleProvider;
    private PlayerSettingsDto _settings;
    private string _persistentDiagnostic = string.Empty;
    private bool _draftIsValid = true;
    private float _lastWindowScale = float.NaN;

    public PlayerSettingsPanel(EditorProject project, Func<float>? uiScaleProvider = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _store = new PlayerSettingsStore(project);
        _uiScaleProvider = uiScaleProvider ?? (static () => EditorUiScale.Default);
        _settings = _store.LoadRecoverable(out _persistentDiagnostic);
        RequiresRepair = !string.IsNullOrWhiteSpace(_persistentDiagnostic);
        DraftSettings = _settings;
        RefreshDraftState();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    public string ValidationMessage { get; private set; } = string.Empty;

    internal bool HasPendingChanges { get; private set; }

    internal bool HasDraftChanges { get; private set; }

    internal bool RequiresRepair { get; private set; }

    internal PlayerSettingsDto DraftSettings { get; private set; }

    internal Vector2 LastWindowPosition { get; private set; }

    internal Vector2 LastWindowSize { get; private set; }

    public ScriptedPlayerSettingsProbeSnapshot ApplyScriptedPlayerSettingsProbe()
    {
        PlayerSettingsDto next = _settings with
        {
            WindowTitle = "PixelEngine Player Settings Probe",
            WindowWidth = 1600,
            WindowHeight = 900,
            VSync = false,
            IconPath = "icons/player-probe.ico",
            Version = "4.5.6",
            StartupScene = "scenes/player-settings-probe.scene",
            InputDefaults = new PlayerInputDefaultsDto
            {
                EnableKeyboardMouse = true,
                EnableGamepad = false,
            },
            RuntimeUiBackend = UiBackendKind.ManagedFallback,
            ReleaseChannel = PlayerReleaseChannel.Production,
        };
        return !TryApplyPlayerSettings(next, out string diagnostic)
            ? throw new InvalidOperationException(diagnostic)
            : CaptureScriptedPlayerSettingsProbe();
    }

    public ScriptedPlayerSettingsProbeSnapshot CaptureScriptedPlayerSettingsProbe()
    {
        return new ScriptedPlayerSettingsProbeSnapshot
        {
            WindowTitle = _settings.WindowTitle,
            WindowWidth = _settings.WindowWidth,
            WindowHeight = _settings.WindowHeight,
            VSync = _settings.VSync,
            IconPath = _settings.IconPath ?? string.Empty,
            Version = _settings.Version,
            StartupScene = _settings.StartupScene,
            EnableKeyboardMouse = _settings.InputDefaults.EnableKeyboardMouse,
            EnableGamepad = _settings.InputDefaults.EnableGamepad,
            RuntimeUiBackend = _settings.RuntimeUiBackend,
            ReleaseChannel = _settings.ReleaseChannel,
            Diagnostic = ValidationMessage,
        };
    }

    public bool TryApplyPlayerSettings(PlayerSettingsDto settings, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryNormalize(out diagnostic))
        {
            ValidationMessage = diagnostic;
            return false;
        }

        PlayerSettingsDto normalized = settings.Normalize();
        try
        {
            _store.Save(normalized);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostic = $"保存 Player Settings 失败：{exception.Message}";
            _persistentDiagnostic = diagnostic;
            RequiresRepair = true;
            ValidationMessage = diagnostic;
            return false;
        }

        _settings = normalized;
        DraftSettings = normalized;
        _persistentDiagnostic = string.Empty;
        RequiresRepair = false;
        HasPendingChanges = false;
        HasDraftChanges = false;
        _draftIsValid = true;
        ValidationMessage = string.Empty;
        diagnostic = string.Empty;
        return true;
    }

    internal void StagePlayerSettings(PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DraftSettings = settings;
        RefreshDraftState();
    }

    internal bool TryApplyDraft(out string diagnostic)
    {
        return TryApplyPlayerSettings(DraftSettings, out diagnostic);
    }

    internal void RevertDraft()
    {
        DraftSettings = _settings;
        RefreshDraftState();
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        float scale = EditorUiScale.Normalize(_uiScaleProvider());
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        EditorSettingsWindowPlacement placement = EditorSettingsWindowLayout.Resolve(
            viewport.WorkPos,
            viewport.WorkSize,
            scale);
        ImGui.SetNextWindowDockID(0, ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(placement.MinimumSize, placement.MaximumSize);
        ImGuiCond placementCondition = MathF.Abs(scale - _lastWindowScale) > 0.0001f
            ? ImGuiCond.Always
            : ImGuiCond.Appearing;
        ImGui.SetNextWindowPos(placement.Position, placementCondition);
        ImGui.SetNextWindowSize(placement.Size, placementCondition);
        _lastWindowScale = scale;
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        LastWindowPosition = ImGui.GetWindowPos();
        LastWindowSize = ImGui.GetWindowSize();
        float footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y + 2f;
        float bodyHeight = MathF.Max(1f, ImGui.GetContentRegionAvail().Y - footerHeight);
        _ = ImGui.BeginChild("player_settings_body", new Vector2(0f, bodyHeight));
        ImGui.SeparatorText("Player");
        ImGui.TextWrapped("玩家包运行时与发布设置。修改会先保留在草稿中，点击 Apply 后才写入工程文件。");
        DrawSettings(scale);
        if (!string.IsNullOrWhiteSpace(ValidationMessage))
        {
            ImGui.SeparatorText("诊断");
            ImGui.TextWrapped(ValidationMessage);
        }

        ImGui.EndChild();
        ImGui.Separator();
        DrawActions(scale);
        ImGui.End();
    }

    private void DrawSettings(float scale)
    {
        if (!ImGui.BeginTable(
            "player_settings_fields",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.BordersInnerH))
        {
            return;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, EditorUiScale.Scale(210f, scale));
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        string title = DraftSettings.WindowTitle;
        NextProperty("窗口标题 / Product Name");
        if (ImGui.InputText("##player-window-title", ref title, 128))
        {
            UpdateDraft(DraftSettings with { WindowTitle = title });
        }

        int width = DraftSettings.WindowWidth;
        NextProperty("窗口宽度");
        if (ImGui.InputInt("##player-window-width", ref width))
        {
            UpdateDraft(DraftSettings with { WindowWidth = width });
        }

        int height = DraftSettings.WindowHeight;
        NextProperty("窗口高度");
        if (ImGui.InputInt("##player-window-height", ref height))
        {
            UpdateDraft(DraftSettings with { WindowHeight = height });
        }

        bool vSync = DraftSettings.VSync;
        NextProperty("VSync");
        if (ImGui.Checkbox("##player-vsync", ref vSync))
        {
            UpdateDraft(DraftSettings with { VSync = vSync });
        }

        string iconPath = DraftSettings.IconPath ?? string.Empty;
        NextProperty("图标路径");
        if (ImGui.InputText("##player-icon-path", ref iconPath, 512))
        {
            UpdateDraft(DraftSettings with { IconPath = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath });
        }

        string version = DraftSettings.Version;
        NextProperty("版本");
        if (ImGui.InputText("##player-version", ref version, 64))
        {
            UpdateDraft(DraftSettings with { Version = version });
        }

        string startupScene = DraftSettings.StartupScene;
        NextProperty("启动场景");
        if (ImGui.InputText("##player-startup-scene", ref startupScene, 512))
        {
            UpdateDraft(DraftSettings with { StartupScene = startupScene });
        }

        bool keyboardMouse = DraftSettings.InputDefaults.EnableKeyboardMouse;
        NextProperty("键盘鼠标输入");
        if (ImGui.Checkbox("##player-keyboard-mouse", ref keyboardMouse))
        {
            UpdateDraft(DraftSettings with
            {
                InputDefaults = DraftSettings.InputDefaults with { EnableKeyboardMouse = keyboardMouse },
            });
        }

        bool gamepad = DraftSettings.InputDefaults.EnableGamepad;
        NextProperty("手柄输入");
        if (ImGui.Checkbox("##player-gamepad", ref gamepad))
        {
            UpdateDraft(DraftSettings with
            {
                InputDefaults = DraftSettings.InputDefaults with { EnableGamepad = gamepad },
            });
        }

        int backend = IndexOf(UiBackendOptions, DraftSettings.RuntimeUiBackend);
        NextProperty("运行时 UI 后端");
        if (ImGui.Combo("##player-runtime-ui", ref backend, UiBackendLabels, UiBackendLabels.Length) && backend >= 0)
        {
            UpdateDraft(DraftSettings with { RuntimeUiBackend = UiBackendOptions[backend] });
        }

        if (DraftSettings.RuntimeUiBackend == UiBackendKind.Ultralight)
        {
            ImGui.TableNextRow();
            _ = ImGui.TableSetColumnIndex(1);
            ImGui.TextWrapped(UltralightOptionalProfileGate.InactiveReason);
        }

        int release = IndexOf(ReleaseOptions, DraftSettings.ReleaseChannel);
        NextProperty("发行通道");
        if (ImGui.Combo("##player-release-channel", ref release, ReleaseLabels, ReleaseLabels.Length) && release >= 0)
        {
            UpdateDraft(DraftSettings with { ReleaseChannel = ReleaseOptions[release] });
        }

        ImGui.EndTable();
    }

    private void DrawActions(float scale)
    {
        float buttonWidth = EditorUiScale.Scale(82f, scale);
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float startX = ImGui.GetCursorPosX();
        float available = ImGui.GetContentRegionAvail().X;
        string status = RequiresRepair
            ? "配置文件需要修复"
            : HasDraftChanges
                ? "有尚未应用的修改"
                : "设置已应用";
        ImGui.TextDisabled(status);
        float actionX = startX + MathF.Max(0f, available - ((buttonWidth * 2f) + spacing));
        ImGui.SameLine(actionX);
        ImGui.BeginDisabled(!HasDraftChanges);
        if (ImGui.Button("Revert", new Vector2(buttonWidth, 0f)))
        {
            RevertDraft();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!HasPendingChanges || !_draftIsValid);
        if (ImGui.Button("Apply", new Vector2(buttonWidth, 0f)))
        {
            _ = TryApplyDraft(out _);
        }

        ImGui.EndDisabled();
    }

    private static int IndexOf(UiBackendKind[] values, UiBackendKind value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == value)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(PlayerReleaseChannel[] values, PlayerReleaseChannel value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == value)
            {
                return i;
            }
        }

        return 0;
    }

    private void UpdateDraft(PlayerSettingsDto next)
    {
        DraftSettings = next;
        RefreshDraftState();
    }

    private void RefreshDraftState()
    {
        _draftIsValid = DraftSettings.TryNormalize(out string diagnostic);
        ValidationMessage = _draftIsValid ? _persistentDiagnostic : diagnostic;
        HasDraftChanges = !AreEquivalent(_settings, DraftSettings);
        HasPendingChanges = RequiresRepair || HasDraftChanges;
    }

    private static bool AreEquivalent(PlayerSettingsDto left, PlayerSettingsDto right)
    {
        return left.FormatVersion == right.FormatVersion &&
            string.Equals(left.WindowTitle, right.WindowTitle, StringComparison.Ordinal) &&
            left.WindowWidth == right.WindowWidth &&
            left.WindowHeight == right.WindowHeight &&
            left.VSync == right.VSync &&
            string.Equals(left.IconPath, right.IconPath, StringComparison.Ordinal) &&
            string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
            string.Equals(left.StartupScene, right.StartupScene, StringComparison.Ordinal) &&
            left.InputDefaults.EnableKeyboardMouse == right.InputDefaults.EnableKeyboardMouse &&
            left.InputDefaults.EnableGamepad == right.InputDefaults.EnableGamepad &&
            left.RuntimeUiBackend == right.RuntimeUiBackend &&
            left.ReleaseChannel == right.ReleaseChannel;
    }

    private static void NextProperty(string label)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1f);
    }
}

/// <summary>
/// 脚本化验收探针：ScriptedPlayerSettingsProbeSnapshot。
/// </summary>
internal sealed record ScriptedPlayerSettingsProbeSnapshot
{
    public string WindowTitle { get; init; } = string.Empty;

    public int WindowWidth { get; init; }

    public int WindowHeight { get; init; }

    public bool VSync { get; init; }

    public string IconPath { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string StartupScene { get; init; } = string.Empty;

    public bool EnableKeyboardMouse { get; init; }

    public bool EnableGamepad { get; init; }

    public UiBackendKind RuntimeUiBackend { get; init; }

    public PlayerReleaseChannel ReleaseChannel { get; init; }

    public string Diagnostic { get; init; } = string.Empty;
}
