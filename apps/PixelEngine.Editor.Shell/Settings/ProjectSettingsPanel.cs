using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Settings;

/// <summary>
/// Project Settings ImGui 面板。
/// </summary>
internal sealed class ProjectSettingsPanel : IEditorPanel
{
    public const string PanelTitle = EditorDockSpace.ProjectSettingsWindowTitle;
    private static readonly UiBackendKind[] UiBackendOptions = [UiBackendKind.ManagedFallback, UiBackendKind.RmlUi, UiBackendKind.Ultralight];
    private static readonly string[] UiBackendLabels = [.. UiBackendOptions.Select(UltralightOptionalProfileGate.GetDisplayLabel)];
    private readonly ProjectSettingsStore _store;
    private ProjectSettingsDto _settings;
    private string _validationMessage = string.Empty;

    public ProjectSettingsPanel(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _store = new ProjectSettingsStore(project);
        _settings = _store.Load();
        Validate();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    public string ValidationMessage => _validationMessage;

    public ScriptedProjectSettingsProbeSnapshot ApplyScriptedProjectSettingsProbe()
    {
        ProjectSettingsDto next = _settings with
        {
            Name = "PixelEngine Project Settings Probe",
            ContentRoot = "content",
            ScriptSourceDir = "scripts/probe",
            StartScene = "scenes/settings-probe.scene",
            DefaultUiBackend = UiBackendKind.ManagedFallback,
            ResourceRules = _settings.ResourceRules with
            {
                RequireStableMaterialNames = true,
                ContentFileGlobs = ["materials.json", "reactions.json", "scenes/**/*.scene", "ui/**/*", "scripts/**/*.cs"],
            },
        };
        return !TryApplyProjectSettings(next, out string diagnostic)
            ? throw new InvalidOperationException(diagnostic)
            : CaptureScriptedProjectSettingsProbe();
    }

    public ScriptedProjectSettingsProbeSnapshot CaptureScriptedProjectSettingsProbe()
    {
        return new ScriptedProjectSettingsProbeSnapshot
        {
            Name = _settings.Name,
            ContentRoot = _settings.ContentRoot,
            ScriptSourceDir = _settings.ScriptSourceDir,
            StartScene = _settings.StartScene,
            DefaultUiBackend = _settings.DefaultUiBackend,
            DefaultUiBackendDiagnostic = UltralightOptionalProfileGate.GetInactiveReason(_settings.DefaultUiBackend),
            RequireStableMaterialNames = _settings.ResourceRules.RequireStableMaterialNames,
            ContentFileGlobCount = _settings.ResourceRules.ContentFileGlobs?.Length ?? 0,
            Diagnostic = _validationMessage,
        };
    }

    public bool TryApplyProjectSettings(ProjectSettingsDto settings, out string diagnostic)
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
        // 项目级设置：内容根、脚本目录、启动场景与 UI 后端
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
        ProjectSettingsDto next = _settings;
        bool changed = false;
        changed |= InputText("工程名", _settings.Name, value => next = next with { Name = value }, 128);
        changed |= InputText("Content Root", _settings.ContentRoot, value => next = next with { ContentRoot = value }, 512);
        changed |= InputText("Script Source Dir", _settings.ScriptSourceDir, value => next = next with { ScriptSourceDir = value }, 512);
        changed |= InputText("默认场景", _settings.StartScene, value => next = next with { StartScene = value }, 512);

        int backend = IndexOf(UiBackendOptions, _settings.DefaultUiBackend);
        if (ImGui.Combo("默认 UI 后端", ref backend, UiBackendLabels, UiBackendLabels.Length) && backend >= 0)
        {
            next = next with { DefaultUiBackend = UiBackendOptions[backend] };
            changed = true;
        }

        if (next.DefaultUiBackend == UiBackendKind.Ultralight)
        {
            ImGui.TextWrapped(UltralightOptionalProfileGate.InactiveReason);
        }

        bool stableNames = _settings.ResourceRules.RequireStableMaterialNames;
        if (ImGui.Checkbox("材质入盘使用稳定 Name", ref stableNames))
        {
            next = next with { ResourceRules = next.ResourceRules with { RequireStableMaterialNames = stableNames } };
            changed = true;
        }

        string globs = string.Join(";", _settings.ResourceRules.ContentFileGlobs ?? []);
        if (InputText("Content globs (; 分隔)", globs, value => next = next with
        {
            ResourceRules = next.ResourceRules with
            {
                ContentFileGlobs = [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            },
        }, 1024))
        {
            changed = true;
        }

        if (changed)
        {
            _ = TryApplyProjectSettings(next, out _);
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

/// <summary>
/// 脚本化验收探针：ScriptedProjectSettingsProbeSnapshot。
/// </summary>
internal sealed record ScriptedProjectSettingsProbeSnapshot
{
    public string Name { get; init; } = string.Empty;

    public string ContentRoot { get; init; } = string.Empty;

    public string ScriptSourceDir { get; init; } = string.Empty;

    public string StartScene { get; init; } = string.Empty;

    public UiBackendKind DefaultUiBackend { get; init; }

    public string DefaultUiBackendDiagnostic { get; init; } = string.Empty;

    public bool RequireStableMaterialNames { get; init; }

    public int ContentFileGlobCount { get; init; }

    public string Diagnostic { get; init; } = string.Empty;
}
