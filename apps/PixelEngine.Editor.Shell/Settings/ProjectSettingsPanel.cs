using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.UI;
using L = PixelEngine.Editor.EditorLocalization;

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
    private readonly Func<float> _uiScaleProvider;
    private ProjectSettingsDto _settings;
    private string _contentGlobsText;
    private string _persistentDiagnostic = string.Empty;
    private bool _draftIsValid = true;
    private float _lastWindowScale = float.NaN;

    public ProjectSettingsPanel(EditorProject project, Func<float>? uiScaleProvider = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _store = new ProjectSettingsStore(project);
        _uiScaleProvider = uiScaleProvider ?? (static () => EditorUiScale.Default);
        _settings = _store.LoadRecoverable(out _persistentDiagnostic);
        RequiresRepair = !string.IsNullOrWhiteSpace(_persistentDiagnostic);
        DraftSettings = _settings;
        _contentGlobsText = FormatContentGlobs(DraftSettings);
        RefreshDraftState();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    public string ValidationMessage { get; private set; } = string.Empty;

    internal bool HasPendingChanges { get; private set; }

    internal bool HasDraftChanges { get; private set; }

    internal bool RequiresRepair { get; private set; }

    internal ProjectSettingsDto DraftSettings { get; private set; }

    internal Vector2 LastWindowPosition { get; private set; }

    internal Vector2 LastWindowSize { get; private set; }

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
            Diagnostic = ValidationMessage,
        };
    }

    public bool TryApplyProjectSettings(ProjectSettingsDto settings, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryNormalize(out diagnostic))
        {
            ValidationMessage = diagnostic;
            return false;
        }

        ProjectSettingsDto normalized = settings.Normalize();
        try
        {
            _store.Save(normalized);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostic = L.Format("settings.saveFailed", "Failed to save {0}: {1}", "Project Settings", exception.Message);
            _persistentDiagnostic = diagnostic;
            RequiresRepair = true;
            ValidationMessage = diagnostic;
            return false;
        }

        _settings = normalized;
        DraftSettings = normalized;
        _contentGlobsText = FormatContentGlobs(DraftSettings);
        _persistentDiagnostic = string.Empty;
        RequiresRepair = false;
        HasPendingChanges = false;
        HasDraftChanges = false;
        _draftIsValid = true;
        ValidationMessage = string.Empty;
        diagnostic = string.Empty;
        return true;
    }

    internal void StageProjectSettings(ProjectSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DraftSettings = settings;
        _contentGlobsText = FormatContentGlobs(DraftSettings);
        RefreshDraftState();
    }

    internal bool TryApplyDraft(out string diagnostic)
    {
        return TryApplyProjectSettings(DraftSettings, out diagnostic);
    }

    internal void RevertDraft()
    {
        DraftSettings = _settings;
        _contentGlobsText = FormatContentGlobs(DraftSettings);
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
        _ = ImGui.BeginChild("project_settings_body", new Vector2(0f, bodyHeight));
        ImGui.SeparatorText(L.Get("projectSettings.section", "Project"));
        TextWrappedUnformatted(L.Get(
            "projectSettings.help",
            "Project authoring settings. Changes remain in a draft until you select Apply."));
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
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!ImGui.BeginTable(
            "project_settings_fields",
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

        string name = DraftSettings.Name;
        NextProperty(L.Get("projectSettings.name", "Project Name"));
        if (ImGui.InputText("##project-name", ref name, 128))
        {
            UpdateDraft(DraftSettings with { Name = name });
        }

        string contentRoot = DraftSettings.ContentRoot;
        NextProperty(L.Get("projectSettings.contentRoot", "Content Root"));
        if (ImGui.InputText("##project-content-root", ref contentRoot, 512))
        {
            UpdateDraft(DraftSettings with { ContentRoot = contentRoot });
        }

        string scriptSourceDir = DraftSettings.ScriptSourceDir;
        NextProperty(L.Get("projectSettings.scriptSourceDir", "Script Source Directory"));
        if (ImGui.InputText("##project-script-source", ref scriptSourceDir, 512))
        {
            UpdateDraft(DraftSettings with { ScriptSourceDir = scriptSourceDir });
        }

        string startScene = DraftSettings.StartScene;
        NextProperty(L.Get("projectSettings.startScene", "Default Scene"));
        if (ImGui.InputText("##project-start-scene", ref startScene, 512))
        {
            UpdateDraft(DraftSettings with { StartScene = startScene });
        }

        int backend = IndexOf(UiBackendOptions, DraftSettings.DefaultUiBackend);
        NextProperty(L.Get("projectSettings.defaultUiBackend", "Default UI Backend"));
        if (ImGui.Combo("##project-default-ui-backend", ref backend, UiBackendLabels, UiBackendLabels.Length) && backend >= 0)
        {
            UpdateDraft(DraftSettings with { DefaultUiBackend = UiBackendOptions[backend] });
        }

        if (DraftSettings.DefaultUiBackend == UiBackendKind.Ultralight)
        {
            ImGui.TableNextRow();
            _ = ImGui.TableSetColumnIndex(1);
            TextWrappedUnformatted(UltralightOptionalProfileGate.InactiveReason);
        }

        bool stableNames = DraftSettings.ResourceRules.RequireStableMaterialNames;
        NextProperty(L.Get("projectSettings.stableMaterialNames", "Stable Material Names"));
        if (ImGui.Checkbox("##project-stable-material-names", ref stableNames))
        {
            UpdateDraft(DraftSettings with
            {
                ResourceRules = DraftSettings.ResourceRules with { RequireStableMaterialNames = stableNames },
            });
        }

        NextProperty(L.Get("projectSettings.contentGlobs", "Content Globs (; separated)"));
        if (ImGui.InputText("##project-content-globs", ref _contentGlobsText, 1024))
        {
            UpdateDraft(DraftSettings with
            {
                ResourceRules = DraftSettings.ResourceRules with
                {
                    ContentFileGlobs =
                    [
                        .. _contentGlobsText.Split(
                            ';',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    ],
                },
            }, refreshGlobsText: false);
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
                : L.Get("settings.status.applied", "Settings applied");
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

    private void UpdateDraft(ProjectSettingsDto next, bool refreshGlobsText = true)
    {
        DraftSettings = next;
        if (refreshGlobsText)
        {
            _contentGlobsText = FormatContentGlobs(DraftSettings);
        }

        RefreshDraftState();
    }

    private void RefreshDraftState()
    {
        _draftIsValid = DraftSettings.TryNormalize(out string diagnostic);
        ValidationMessage = _draftIsValid ? _persistentDiagnostic : diagnostic;
        HasDraftChanges = !AreEquivalent(_settings, DraftSettings);
        HasPendingChanges = RequiresRepair || HasDraftChanges;
    }

    private static bool AreEquivalent(ProjectSettingsDto left, ProjectSettingsDto right)
    {
        return left.FormatVersion == right.FormatVersion &&
            string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            string.Equals(left.ContentRoot, right.ContentRoot, StringComparison.Ordinal) &&
            string.Equals(left.ScriptSourceDir, right.ScriptSourceDir, StringComparison.Ordinal) &&
            string.Equals(left.StartScene, right.StartScene, StringComparison.Ordinal) &&
            left.DefaultUiBackend == right.DefaultUiBackend &&
            left.ResourceRules.RequireStableMaterialNames == right.ResourceRules.RequireStableMaterialNames &&
            (left.ResourceRules.ContentFileGlobs ?? []).SequenceEqual(
                right.ResourceRules.ContentFileGlobs ?? [],
                StringComparer.Ordinal);
    }

    private static string FormatContentGlobs(ProjectSettingsDto settings)
    {
        return string.Join(";", settings.ResourceRules.ContentFileGlobs ?? []);
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
