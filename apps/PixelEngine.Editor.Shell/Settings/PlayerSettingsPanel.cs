using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Settings;

internal sealed class PlayerSettingsPanel : IEditorPanel
{
    public const string PanelTitle = EditorDockSpace.PlayerSettingsWindowTitle;
    private static readonly UiBackendKind[] UiBackendOptions = [UiBackendKind.ManagedFallback, UiBackendKind.RmlUi, UiBackendKind.Ultralight];
    private static readonly string[] UiBackendLabels = [.. UiBackendOptions.Select(UltralightOptionalProfileGate.GetDisplayLabel)];
    private static readonly PlayerReleaseChannel[] ReleaseOptions = [PlayerReleaseChannel.Development, PlayerReleaseChannel.Production];
    private static readonly string[] ReleaseLabels = ["Development", "Production"];
    private readonly PlayerSettingsStore _store;
    private PlayerSettingsDto _settings;
    private string _validationMessage = string.Empty;

    public PlayerSettingsPanel(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _store = new PlayerSettingsStore(project);
        _settings = _store.Load();
        Validate();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    public string ValidationMessage => _validationMessage;

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
        if (!TryApplyPlayerSettings(next, out string diagnostic))
        {
            throw new InvalidOperationException(diagnostic);
        }

        return CaptureScriptedPlayerSettingsProbe();
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
            Diagnostic = _validationMessage,
        };
    }

    public bool TryApplyPlayerSettings(PlayerSettingsDto settings, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryNormalize(out diagnostic))
        {
            _validationMessage = diagnostic;
            return false;
        }

        _settings = settings.Normalize();
        _store.Save(_settings);
        _validationMessage = string.Empty;
        diagnostic = string.Empty;
        return true;
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawSettings();
        if (!string.IsNullOrWhiteSpace(_validationMessage))
        {
            ImGui.SeparatorText("诊断");
            ImGui.TextWrapped(_validationMessage);
        }

        ImGui.End();
    }

    private void DrawSettings()
    {
        PlayerSettingsDto next = _settings;
        bool changed = false;
        changed |= InputText("窗口标题 / Product Name", _settings.WindowTitle, value => next = next with { WindowTitle = value }, 128);
        int width = _settings.WindowWidth;
        if (ImGui.InputInt("窗口宽度", ref width))
        {
            next = next with { WindowWidth = width };
            changed = true;
        }

        int height = _settings.WindowHeight;
        if (ImGui.InputInt("窗口高度", ref height))
        {
            next = next with { WindowHeight = height };
            changed = true;
        }

        bool vSync = _settings.VSync;
        if (ImGui.Checkbox("VSync", ref vSync))
        {
            next = next with { VSync = vSync };
            changed = true;
        }

        string iconPath = _settings.IconPath ?? string.Empty;
        if (InputText("图标路径", iconPath, value => next = next with { IconPath = string.IsNullOrWhiteSpace(value) ? null : value }, 512))
        {
            changed = true;
        }

        changed |= InputText("版本", _settings.Version, value => next = next with { Version = value }, 64);
        changed |= InputText("启动场景", _settings.StartupScene, value => next = next with { StartupScene = value }, 512);

        bool keyboardMouse = _settings.InputDefaults.EnableKeyboardMouse;
        if (ImGui.Checkbox("键盘鼠标输入", ref keyboardMouse))
        {
            next = next with { InputDefaults = next.InputDefaults with { EnableKeyboardMouse = keyboardMouse } };
            changed = true;
        }

        bool gamepad = _settings.InputDefaults.EnableGamepad;
        if (ImGui.Checkbox("手柄输入", ref gamepad))
        {
            next = next with { InputDefaults = next.InputDefaults with { EnableGamepad = gamepad } };
            changed = true;
        }

        int backend = IndexOf(UiBackendOptions, _settings.RuntimeUiBackend);
        if (ImGui.Combo("运行时 UI 后端", ref backend, UiBackendLabels, UiBackendLabels.Length) && backend >= 0)
        {
            next = next with { RuntimeUiBackend = UiBackendOptions[backend] };
            changed = true;
        }

        if (next.RuntimeUiBackend == UiBackendKind.Ultralight)
        {
            ImGui.TextWrapped(UltralightOptionalProfileGate.InactiveReason);
        }

        int release = IndexOf(ReleaseOptions, _settings.ReleaseChannel);
        if (ImGui.Combo("发行通道", ref release, ReleaseLabels, ReleaseLabels.Length) && release >= 0)
        {
            next = next with { ReleaseChannel = ReleaseOptions[release] };
            changed = true;
        }

        if (changed)
        {
            _ = TryApplyPlayerSettings(next, out _);
        }
    }

    private void Validate()
    {
        _ = _settings.TryNormalize(out _validationMessage);
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

    private static bool InputText(string label, string value, Action<string> assign, uint maxLength)
    {
        string editable = value;
        bool changed = ImGui.InputText(label, ref editable, maxLength);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }
}

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
