using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.UI;
using L = PixelEngine.Editor.EditorLocalization;

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
    private static readonly PlayerWindowMode[] WindowModeOptions =
    [
        PlayerWindowMode.Windowed,
        PlayerWindowMode.MaximizedWindow,
        PlayerWindowMode.BorderlessFullscreen,
    ];
    private string[] _releaseLabels = ["Development", "Production"];
    private string[] _windowModeLabels =
    [
        "Windowed",
        "Maximized Window",
        "Borderless Fullscreen",
    ];
    private readonly PlayerSettingsStore _store;
    private readonly Func<float> _uiScaleProvider;
    private string _localizedOptionsLocale = string.Empty;
    private string _persistentDiagnostic = string.Empty;
    private bool _draftIsValid = true;
    private float _lastWindowScale = float.NaN;

    public PlayerSettingsPanel(EditorProject project, Func<float>? uiScaleProvider = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _store = new PlayerSettingsStore(project);
        _uiScaleProvider = uiScaleProvider ?? (static () => EditorUiScale.Default);
        AppliedSettings = _store.LoadRecoverable(out _persistentDiagnostic);
        RequiresRepair = !string.IsNullOrWhiteSpace(_persistentDiagnostic);
        DraftSettings = AppliedSettings;
        RefreshDraftState();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    internal event Action? SettingsApplied;

    public string ValidationMessage { get; private set; } = string.Empty;

    internal bool HasPendingChanges { get; private set; }

    internal bool HasDraftChanges { get; private set; }

    internal bool RequiresRepair { get; private set; }

    internal PlayerSettingsDto DraftSettings { get; private set; }

    internal PlayerSettingsDto AppliedSettings { get; private set; }

    internal Vector2 LastWindowPosition { get; private set; }

    internal Vector2 LastWindowSize { get; private set; }

    public ScriptedPlayerSettingsProbeSnapshot ApplyScriptedPlayerSettingsProbe()
    {
        PlayerSettingsDto next = AppliedSettings with
        {
            WindowTitle = "PixelEngine Player Settings Probe",
            WindowWidth = 1600,
            WindowHeight = 900,
            WindowMode = PlayerWindowMode.MaximizedWindow,
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
            WindowTitle = AppliedSettings.WindowTitle,
            WindowWidth = AppliedSettings.WindowWidth,
            WindowHeight = AppliedSettings.WindowHeight,
            WindowMode = AppliedSettings.WindowMode,
            VSync = AppliedSettings.VSync,
            IconPath = AppliedSettings.IconPath ?? string.Empty,
            Version = AppliedSettings.Version,
            StartupScene = AppliedSettings.StartupScene,
            EnableKeyboardMouse = AppliedSettings.InputDefaults.EnableKeyboardMouse,
            EnableGamepad = AppliedSettings.InputDefaults.EnableGamepad,
            RuntimeUiBackend = AppliedSettings.RuntimeUiBackend,
            ReleaseChannel = AppliedSettings.ReleaseChannel,
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
        if (!RequiresRepair && AreEquivalent(AppliedSettings, normalized))
        {
            diagnostic = string.Empty;
            return true;
        }

        try
        {
            _store.Save(normalized);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostic = L.Format("settings.saveFailed", "Failed to save {0}: {1}", "Player Settings", exception.Message);
            _persistentDiagnostic = diagnostic;
            RequiresRepair = true;
            ValidationMessage = diagnostic;
            return false;
        }

        AppliedSettings = normalized;
        DraftSettings = normalized;
        _persistentDiagnostic = string.Empty;
        RequiresRepair = false;
        HasPendingChanges = false;
        HasDraftChanges = false;
        _draftIsValid = true;
        ValidationMessage = string.Empty;
        diagnostic = string.Empty;
        SettingsApplied?.Invoke();
        return true;
    }

    internal PlayerSettingsPanelAutomationSnapshot CaptureAutomationState()
    {
        return new PlayerSettingsPanelAutomationSnapshot(
            CloneSettings(AppliedSettings),
            CloneSettings(DraftSettings),
            _persistentDiagnostic,
            _draftIsValid,
            ValidationMessage,
            HasPendingChanges,
            HasDraftChanges,
            RequiresRepair);
    }

    internal PlayerSettingsPanelAutomationSnapshot CreateAutomationAppliedState(
        PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        PlayerSettingsDto normalized = settings.Normalize();
        return new PlayerSettingsPanelAutomationSnapshot(
            CloneSettings(normalized),
            CloneSettings(normalized),
            string.Empty,
            DraftIsValid: true,
            ValidationMessage: string.Empty,
            HasPendingChanges: false,
            HasDraftChanges: false,
            RequiresRepair: false);
    }

    internal void RestoreAutomationState(PlayerSettingsPanelAutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        AppliedSettings = CloneSettings(snapshot.AppliedSettings);
        DraftSettings = CloneSettings(snapshot.DraftSettings);
        _persistentDiagnostic = snapshot.PersistentDiagnostic;
        _draftIsValid = snapshot.DraftIsValid;
        ValidationMessage = snapshot.ValidationMessage;
        HasPendingChanges = snapshot.HasPendingChanges;
        HasDraftChanges = snapshot.HasDraftChanges;
        RequiresRepair = snapshot.RequiresRepair;
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
        DraftSettings = AppliedSettings;
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
        ImGui.SeparatorText(L.Get("playerSettings.section", "Player"));
        TextWrappedUnformatted(L.Get(
            "playerSettings.help",
            "Player runtime and release settings. Changes remain in a draft until you select Apply."));
        DrawSettings(scale);
        if (!string.IsNullOrWhiteSpace(ValidationMessage))
        {
            ImGui.SeparatorText(L.Get("settings.diagnostic", "Diagnostic"));
            TextWrappedUnformatted(ValidationMessage);
        }

        ImGui.EndChild();
        ImGui.Separator();
        DrawActions(scale);
        ImGui.End();
    }

    private void DrawSettings(float scale)
    {
        RefreshLocalizedOptionLabels();
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!ImGui.BeginTable(
            "player_settings_fields",
            2,
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TableSetupColumn(
            L.Get("settings.property", "Property"),
            ImGuiTableColumnFlags.WidthFixed,
            EditorSettingsWindowLayout.ResolveLabelWidth(availableWidth, scale));
        ImGui.TableSetupColumn(L.Get("settings.value", "Value"), ImGuiTableColumnFlags.WidthStretch);

        string title = DraftSettings.WindowTitle;
        NextProperty(L.Get("playerSettings.windowTitle", "Window Title / Product Name"));
        if (ImGui.InputText("##player-window-title", ref title, 128))
        {
            UpdateDraft(DraftSettings with { WindowTitle = title });
        }

        int width = DraftSettings.WindowWidth;
        NextProperty(L.Get("playerSettings.windowWidth", "Window Width"));
        if (ImGui.InputInt("##player-window-width", ref width))
        {
            UpdateDraft(DraftSettings with { WindowWidth = width });
        }

        int height = DraftSettings.WindowHeight;
        NextProperty(L.Get("playerSettings.windowHeight", "Window Height"));
        if (ImGui.InputInt("##player-window-height", ref height))
        {
            UpdateDraft(DraftSettings with { WindowHeight = height });
        }

        int windowMode = IndexOf(WindowModeOptions, DraftSettings.WindowMode);
        NextProperty(L.Get("playerSettings.windowMode", "Window Mode"));
        if (ImGui.Combo("##player-window-mode", ref windowMode, _windowModeLabels, _windowModeLabels.Length) && windowMode >= 0)
        {
            UpdateDraft(DraftSettings with { WindowMode = WindowModeOptions[windowMode] });
        }

        bool vSync = DraftSettings.VSync;
        NextProperty(L.Get("playerSettings.vsync", "VSync"));
        if (ImGui.Checkbox("##player-vsync", ref vSync))
        {
            UpdateDraft(DraftSettings with { VSync = vSync });
        }

        string iconPath = DraftSettings.IconPath ?? string.Empty;
        NextProperty(L.Get("playerSettings.iconPath", "Icon Path"));
        if (ImGui.InputText("##player-icon-path", ref iconPath, 512))
        {
            UpdateDraft(DraftSettings with { IconPath = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath });
        }

        string version = DraftSettings.Version;
        NextProperty(L.Get("playerSettings.version", "Version"));
        if (ImGui.InputText("##player-version", ref version, 64))
        {
            UpdateDraft(DraftSettings with { Version = version });
        }

        string startupScene = DraftSettings.StartupScene;
        NextProperty(L.Get("playerSettings.startupScene", "Startup Scene"));
        if (ImGui.InputText("##player-startup-scene", ref startupScene, 512))
        {
            UpdateDraft(DraftSettings with { StartupScene = startupScene });
        }

        bool keyboardMouse = DraftSettings.InputDefaults.EnableKeyboardMouse;
        NextProperty(L.Get("playerSettings.keyboardMouse", "Keyboard and Mouse Input"));
        if (ImGui.Checkbox("##player-keyboard-mouse", ref keyboardMouse))
        {
            UpdateDraft(DraftSettings with
            {
                InputDefaults = DraftSettings.InputDefaults with { EnableKeyboardMouse = keyboardMouse },
            });
        }

        bool gamepad = DraftSettings.InputDefaults.EnableGamepad;
        NextProperty(L.Get("playerSettings.gamepad", "Gamepad Input"));
        if (ImGui.Checkbox("##player-gamepad", ref gamepad))
        {
            UpdateDraft(DraftSettings with
            {
                InputDefaults = DraftSettings.InputDefaults with { EnableGamepad = gamepad },
            });
        }

        int backend = IndexOf(UiBackendOptions, DraftSettings.RuntimeUiBackend);
        NextProperty(L.Get("playerSettings.runtimeUiBackend", "Runtime UI Backend"));
        if (ImGui.Combo("##player-runtime-ui", ref backend, UiBackendLabels, UiBackendLabels.Length) && backend >= 0)
        {
            UpdateDraft(DraftSettings with { RuntimeUiBackend = UiBackendOptions[backend] });
        }

        if (DraftSettings.RuntimeUiBackend == UiBackendKind.Ultralight)
        {
            ImGui.TableNextRow();
            _ = ImGui.TableSetColumnIndex(1);
            TextWrappedUnformatted(UltralightOptionalProfileGate.InactiveReason);
        }

        int release = IndexOf(ReleaseOptions, DraftSettings.ReleaseChannel);
        NextProperty(L.Get("playerSettings.releaseChannel", "Release Channel"));
        if (ImGui.Combo("##player-release-channel", ref release, _releaseLabels, _releaseLabels.Length) && release >= 0)
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
            ? L.Get("settings.status.repair", "Configuration file needs repair")
            : HasDraftChanges
                ? L.Get("settings.status.modified", "Unapplied changes")
                : L.Get("settings.status.applied", "No pending changes");
        ImGui.TextDisabled(status);
        float actionX = startX + MathF.Max(0f, available - ((buttonWidth * 2f) + spacing));
        ImGui.SameLine(actionX);
        ImGui.BeginDisabled(!HasDraftChanges);
        if (ImGui.Button(L.Get("settings.revert", "Revert"), new Vector2(buttonWidth, 0f)))
        {
            RevertDraft();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!HasPendingChanges || !_draftIsValid);
        if (ImGui.Button(L.Get("settings.apply", "Apply"), new Vector2(buttonWidth, 0f)))
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

    private void RefreshLocalizedOptionLabels()
    {
        string locale = L.CurrentLocale;
        if (string.Equals(_localizedOptionsLocale, locale, StringComparison.Ordinal))
        {
            return;
        }

        _localizedOptionsLocale = locale;
        _windowModeLabels =
        [
            L.Get("playerSettings.mode.windowed", "Windowed"),
            L.Get("playerSettings.mode.maximized", "Maximized Window"),
            L.Get("playerSettings.mode.borderless", "Borderless Fullscreen"),
        ];
        _releaseLabels =
        [
            L.Get("playerSettings.release.development", "Development"),
            L.Get("playerSettings.release.production", "Production"),
        ];
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

    private static int IndexOf(PlayerWindowMode[] values, PlayerWindowMode value)
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
        HasDraftChanges = !AreEquivalent(AppliedSettings, DraftSettings);
        HasPendingChanges = RequiresRepair || HasDraftChanges;
    }

    private static PlayerSettingsDto CloneSettings(PlayerSettingsDto settings)
    {
        return settings with
        {
            InputDefaults = settings.InputDefaults with { },
        };
    }

    private static bool AreEquivalent(PlayerSettingsDto left, PlayerSettingsDto right)
    {
        return left.FormatVersion == right.FormatVersion &&
            string.Equals(left.WindowTitle, right.WindowTitle, StringComparison.Ordinal) &&
            left.WindowWidth == right.WindowWidth &&
            left.WindowHeight == right.WindowHeight &&
            left.WindowMode == right.WindowMode &&
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
        TextWrappedUnformatted(label);
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1f);
    }

    private static void TextWrappedUnformatted(string text)
    {
        float contentWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}

internal sealed record PlayerSettingsPanelAutomationSnapshot(
    PlayerSettingsDto AppliedSettings,
    PlayerSettingsDto DraftSettings,
    string PersistentDiagnostic,
    bool DraftIsValid,
    string ValidationMessage,
    bool HasPendingChanges,
    bool HasDraftChanges,
    bool RequiresRepair);

/// <summary>
/// 脚本化验收探针：ScriptedPlayerSettingsProbeSnapshot。
/// </summary>
internal sealed record ScriptedPlayerSettingsProbeSnapshot
{
    public string WindowTitle { get; init; } = string.Empty;

    public int WindowWidth { get; init; }

    public int WindowHeight { get; init; }

    public PlayerWindowMode WindowMode { get; init; }

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
