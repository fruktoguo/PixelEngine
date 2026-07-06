using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Settings;

internal sealed class ProjectSettingsPanel : IEditorPanel
{
    public const string PanelTitle = EditorDockSpace.ProjectSettingsWindowTitle;
    private static readonly UiBackendKind[] UiBackendOptions = [UiBackendKind.ManagedFallback, UiBackendKind.RmlUi, UiBackendKind.Ultralight];
    private static readonly string[] UiBackendLabels = ["ManagedFallback", "RmlUi", "Ultralight"];
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
            EditorPreferences = _settings.EditorPreferences with
            {
                SaveLayoutOnExit = false,
                ExternalScriptEditor = "system-default",
            },
        };
        if (!TryApplyProjectSettings(next, out string diagnostic))
        {
            throw new InvalidOperationException(diagnostic);
        }

        return CaptureScriptedProjectSettingsProbe();
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
            RequireStableMaterialNames = _settings.ResourceRules.RequireStableMaterialNames,
            ContentFileGlobCount = _settings.ResourceRules.ContentFileGlobs?.Length ?? 0,
            SaveLayoutOnExit = _settings.EditorPreferences.SaveLayoutOnExit,
            ExternalScriptEditor = _settings.EditorPreferences.ExternalScriptEditor,
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

        bool saveLayout = _settings.EditorPreferences.SaveLayoutOnExit;
        if (ImGui.Checkbox("退出时保存布局", ref saveLayout))
        {
            next = next with { EditorPreferences = next.EditorPreferences with { SaveLayoutOnExit = saveLayout } };
            changed = true;
        }

        changed |= InputText(
            "外部脚本编辑器",
            _settings.EditorPreferences.ExternalScriptEditor,
            value => next = next with { EditorPreferences = next.EditorPreferences with { ExternalScriptEditor = value } },
            512);

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

internal sealed record ScriptedProjectSettingsProbeSnapshot
{
    public string Name { get; init; } = string.Empty;

    public string ContentRoot { get; init; } = string.Empty;

    public string ScriptSourceDir { get; init; } = string.Empty;

    public string StartScene { get; init; } = string.Empty;

    public UiBackendKind DefaultUiBackend { get; init; }

    public bool RequireStableMaterialNames { get; init; }

    public int ContentFileGlobCount { get; init; }

    public bool SaveLayoutOnExit { get; init; }

    public string ExternalScriptEditor { get; init; } = string.Empty;

    public string Diagnostic { get; init; } = string.Empty;
}
