using System.Numerics;
using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell.Settings;

internal enum EditorPreferencesCategory
{
    Appearance,
    General,
    ExternalTools,
    Shortcuts,
}

/// <summary>
/// Unity-like 用户级 Preferences 窗口；不依赖当前是否打开工程。
/// </summary>
internal sealed class EditorPreferencesWindow(
    EditorPreferencesStore store,
    Action resetLayout,
    Action<string>? languageChanged = null) : IEditorPanel
{
    private static readonly (EditorPreferencesCategory Category, string Key, string Fallback)[] Categories =
    [
        (EditorPreferencesCategory.Appearance, "prefs.appearance", "Appearance"),
        (EditorPreferencesCategory.General, "prefs.general", "General"),
        (EditorPreferencesCategory.ExternalTools, "prefs.externalTools", "External Tools"),
        (EditorPreferencesCategory.Shortcuts, "prefs.shortcuts", "Shortcuts"),
    ];

    private readonly EditorPreferencesStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Action _resetLayout = resetLayout ?? throw new ArgumentNullException(nameof(resetLayout));
    private readonly Action<string>? _languageChanged = languageChanged;
    private string _diagnostic = store.LastDiagnostic;
    private string _customEditorDraft = string.Empty;
    private string _customEditorSource = string.Empty;
    private bool _customEditorDraftDirty;
    private float _lastWindowScale = float.NaN;

    public const string PanelTitle = "Preferences";

    public string Title => PanelTitle;

    public bool Visible { get; set; }

    public EditorPreferencesCategory SelectedCategory { get; private set; } = EditorPreferencesCategory.Appearance;

    public Vector2 LastWindowPosition { get; private set; }

    public Vector2 LastWindowSize { get; private set; }

    public bool LastNavigationVisible { get; private set; }

    public void Show(EditorPreferencesCategory category = EditorPreferencesCategory.Appearance)
    {
        SelectedCategory = category;
        Visible = true;
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        Draw();
    }

    public void Draw()
    {
        if (!Visible)
        {
            return;
        }

        float scale = _store.Current.UiScale;
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 windowSize = EditorUiScale.FitWindow(new Vector2(820f, 540f), scale, viewport.WorkSize);
        Vector2 windowPosition = viewport.WorkPos + ((viewport.WorkSize - windowSize) * 0.5f);
        ImGuiCond placementCondition = MathF.Abs(scale - _lastWindowScale) > 0.0001f
            ? ImGuiCond.Always
            : ImGuiCond.Appearing;
        ImGui.SetNextWindowPos(windowPosition, placementCondition);
        ImGui.SetNextWindowSize(windowSize, placementCondition);
        _lastWindowScale = scale;
        bool visible = Visible;
        if (!ImGui.Begin(PanelTitle, ref visible, ImGuiWindowFlags.NoDocking))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        LastWindowPosition = ImGui.GetWindowPos();
        LastWindowSize = ImGui.GetWindowSize();
        float navigationWidth = EditorUiScale.Scale(170f, scale);
        Vector2 availableSize = ImGui.GetContentRegionAvail();
        if (UseCompactNavigation(availableSize.X, scale))
        {
            LastNavigationVisible = true;
            DrawCompactCategorySelector();
            ImGui.Separator();
            bool childVisible = ImGui.BeginChild("preferences_compact_settings", Vector2.Zero);
            if (childVisible)
            {
                DrawSettingsContent();
            }

            ImGui.EndChild();
            ImGui.End();
            return;
        }

        LastNavigationVisible = ImGui.BeginTable(
            "preferences_layout",
            2,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
            availableSize);
        if (LastNavigationVisible)
        {
            ImGui.TableSetupColumn("Categories", ImGuiTableColumnFlags.WidthFixed, navigationWidth);
            ImGui.TableSetupColumn("Settings", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            _ = ImGui.TableNextColumn();
            for (int i = 0; i < Categories.Length; i++)
            {
                (EditorPreferencesCategory category, string key, string fallback) = Categories[i];
                if (ImGui.Selectable(EditorLocalization.Get(key, fallback), SelectedCategory == category))
                {
                    SelectedCategory = category;
                }
            }

            _ = ImGui.TableNextColumn();
            DrawSettingsContent();

            ImGui.EndTable();
        }
        ImGui.End();
    }

    internal static bool UseCompactNavigation(float availableWidth, float uiScale)
    {
        float width = float.IsFinite(availableWidth) ? MathF.Max(1f, availableWidth) : 1f;
        float navigationWidth = EditorUiScale.Scale(170f, uiScale);
        float minimumSettingsWidth = EditorUiScale.Scale(300f, uiScale);
        return width < navigationWidth + minimumSettingsWidth;
    }

    private void DrawCompactCategorySelector()
    {
        string preview = GetCategoryLabel(SelectedCategory);
        ImGui.TextUnformatted(EditorLocalization.Get("prefs.category", "Category"));
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo("##preferences-category", preview))
        {
            return;
        }

        for (int i = 0; i < Categories.Length; i++)
        {
            EditorPreferencesCategory category = Categories[i].Category;
            bool selected = SelectedCategory == category;
            if (ImGui.Selectable(GetCategoryLabel(category), selected))
            {
                SelectedCategory = category;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private static string GetCategoryLabel(EditorPreferencesCategory category)
    {
        for (int i = 0; i < Categories.Length; i++)
        {
            if (Categories[i].Category == category)
            {
                return EditorLocalization.Get(Categories[i].Key, Categories[i].Fallback);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(category), category, "未知 Preferences 分类。");
    }

    private void DrawSettingsContent()
    {
        DrawSelectedCategory();
        string diagnostic = string.IsNullOrWhiteSpace(_diagnostic) ? _store.LastDiagnostic : _diagnostic;
        if (!string.IsNullOrWhiteSpace(diagnostic))
        {
            ImGui.SeparatorText(EditorLocalization.Get("prefs.diagnostic", "Diagnostic"));
            TextWrappedUnformatted(diagnostic);
        }
    }

    private void DrawSelectedCategory()
    {
        switch (SelectedCategory)
        {
            case EditorPreferencesCategory.Appearance:
                DrawAppearance();
                break;
            case EditorPreferencesCategory.General:
                DrawGeneral();
                break;
            case EditorPreferencesCategory.ExternalTools:
                DrawExternalTools();
                break;
            case EditorPreferencesCategory.Shortcuts:
                DrawShortcuts();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void DrawAppearance()
    {
        ImGui.SeparatorText(EditorLocalization.Get("prefs.appearance", "Appearance"));
        float scale = EditorUiScale.Normalize(_store.Current.UiScale);
        if (!BeginPreferenceFields("preferences_appearance_fields", scale))
        {
            return;
        }

        NextPreferenceField(EditorLocalization.Get("prefs.language", "Language"));
        DrawLanguageSelector();
        NextPreferenceField(EditorLocalization.Get("prefs.uiScale", "UI Scale"));
        float percent = EditorUiScale.ToPercent(_store.Current.UiScale);
        if (ImGui.SliderFloat(
            "##preferences-ui-scale",
            ref percent,
            EditorUiScale.Minimum * 100f,
            EditorUiScale.Maximum * 100f,
            "%.0f%%"))
        {
            float nextScale = EditorUiScale.Normalize(percent / 100f);
            _ = Update(_store.Current with { UiScale = nextScale });
        }

        NextPreferenceHelp(EditorLocalization.Get(
            "prefs.uiScaleHelp",
            "150% is recommended for 4K displays. Fonts, menus, spacing, scrollbars, and toolbar sizes scale together."));
        NextPreferenceHelp(EditorLocalization.Get(
            "prefs.uiScaleRestartHelp",
            "Scaling applies immediately. After restart, the font atlas is rebuilt at the target pixel size for the sharpest text."));
        NextPreferenceField(EditorLocalization.Get("prefs.theme", "Theme"));
        ImGui.TextDisabled("Unity 6 Dark");
        ImGui.EndTable();
    }

    private void DrawLanguageSelector()
    {
        IReadOnlyList<EditorLanguageInfo> languages = EditorLocalization.AvailableLanguages;
        string currentDisplay = _store.Current.Language;
        for (int i = 0; i < languages.Count; i++)
        {
            if (string.Equals(languages[i].Locale, _store.Current.Language, StringComparison.OrdinalIgnoreCase))
            {
                currentDisplay = languages[i].DisplayName;
                break;
            }
        }

        if (!ImGui.BeginCombo("##preferences-language", currentDisplay))
        {
            return;
        }

        for (int i = 0; i < languages.Count; i++)
        {
            EditorLanguageInfo language = languages[i];
            bool selected = string.Equals(language.Locale, _store.Current.Language, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{language.DisplayName}##language_{language.Locale}", selected) &&
                Update(_store.Current with { Language = language.Locale }))
            {
                _languageChanged?.Invoke(language.Locale);
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawGeneral()
    {
        ImGui.SeparatorText(EditorLocalization.Get("prefs.general", "General"));
        float scale = EditorUiScale.Normalize(_store.Current.UiScale);
        if (!BeginPreferenceFields("preferences_general_fields", scale))
        {
            return;
        }

        NextPreferenceField(EditorLocalization.Get("prefs.saveLayout", "Save layout continuously"));
        bool saveLayout = _store.Current.SaveLayoutOnExit;
        if (ImGui.Checkbox("##preferences-save-layout", ref saveLayout))
        {
            _ = Update(_store.Current with { SaveLayoutOnExit = saveLayout });
        }

        NextPreferenceHelp(EditorLocalization.Get(
            "prefs.saveLayoutHelp",
            "Saves window docking and sizes continuously. When disabled, the last saved layout is preserved."));
        NextPreferenceField(EditorLocalization.Get("prefs.reopenProject", "Reopen last project on startup"));
        bool reopenLastProject = _store.Current.ReopenLastProject;
        if (ImGui.Checkbox("##preferences-reopen-project", ref reopenLastProject))
        {
            _ = Update(_store.Current with { ReopenLastProject = reopenLastProject });
        }

        NextPreferenceHelp(EditorLocalization.Get(
            "prefs.reopenProjectHelp",
            "Without an explicit --project, reopens the last successfully opened project. Automation and the last abnormal shutdown are not retried blindly."));
        NextPreferenceField(EditorLocalization.Get("prefs.restoreScene", "Restore last open scene"));
        bool restoreLastScene = _store.Current.RestoreLastScene;
        if (ImGui.Checkbox("##preferences-restore-scene", ref restoreLastScene))
        {
            _ = Update(_store.Current with { RestoreLastScene = restoreLastScene });
        }

        NextPreferenceHelp(EditorLocalization.Get(
            "prefs.restoreSceneHelp",
            "The current editing scene is stored in the user workspace and does not rewrite the project's Start Scene."));
        NextPreferenceField(EditorLocalization.Get("prefs.layout", "Layout"));
        if (ImGui.Button(EditorLocalization.Get("prefs.resetLayout", "Reset to Default Layout")))
        {
            _resetLayout();
        }

        ImGui.EndTable();
    }

    private void DrawExternalTools()
    {
        ImGui.SeparatorText(EditorLocalization.Get("prefs.externalTools", "External Tools"));
        float scale = EditorUiScale.Normalize(_store.Current.UiScale);
        if (!BeginPreferenceFields("preferences_external_tools_fields", scale))
        {
            return;
        }

        string command = _store.Current.ExternalScriptEditor;
        ExternalCodeEditorKind currentKind = ExternalCodeEditorPreference.Classify(command);
        string preview = EditorDisplayName(currentKind);
        NextPreferenceField(EditorLocalization.Get("prefs.scriptEditor", "Script Editor"));
        if (ImGui.BeginCombo("##preferences-script-editor", preview))
        {
            DrawEditorPreset(ExternalCodeEditorKind.VsCode, ExternalCodeEditorPreference.VsCode, currentKind);
            DrawEditorPreset(ExternalCodeEditorKind.VisualStudio, ExternalCodeEditorPreference.VisualStudio, currentKind);
            DrawEditorPreset(ExternalCodeEditorKind.Rider, ExternalCodeEditorPreference.Rider, currentKind);
            DrawEditorPreset(ExternalCodeEditorKind.SystemDefault, ExternalCodeEditorPreference.SystemDefault, currentKind);
            DrawEditorPreset(ExternalCodeEditorKind.Custom, "code.cmd --reuse-window --goto {file}:{line}:{column}", currentKind);
            ImGui.EndCombo();
        }

        if (currentKind == ExternalCodeEditorKind.Custom)
        {
            SynchronizeCustomEditorDraft(command);
            NextPreferenceField(EditorLocalization.Get("prefs.customEditorCommand", "Custom Command"));
            if (ImGui.InputText(
                "##preferences-custom-editor-command",
                ref _customEditorDraft,
                1024))
            {
                _customEditorDraftDirty = !string.Equals(
                    _customEditorDraft,
                    _customEditorSource,
                    StringComparison.Ordinal);
            }

            NextPreferenceHelp(EditorLocalization.Get(
                "prefs.customEditorHelp",
                "Placeholders: {file}, {line}, {column}, {project}. Without {file}, the script path is appended."));
            bool valid = TryValidateCustomEditorCommand(_customEditorDraft, out string validationDiagnostic);
            if (!valid)
            {
                NextPreferenceValueRow();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.25f, 1f));
                TextWrappedUnformatted(validationDiagnostic);
                ImGui.PopStyleColor();
            }

            NextPreferenceField(EditorLocalization.Get("prefs.actions", "Actions"));
            ImGui.BeginDisabled(!_customEditorDraftDirty || !valid);
            if (ImGui.Button(EditorLocalization.Get("prefs.apply", "Apply")) &&
                Update(_store.Current with { ExternalScriptEditor = _customEditorDraft }))
            {
                _customEditorSource = _store.Current.ExternalScriptEditor;
                _customEditorDraft = _customEditorSource;
                _customEditorDraftDirty = false;
            }

            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!_customEditorDraftDirty);
            if (ImGui.Button(EditorLocalization.Get("prefs.revert", "Revert")))
            {
                _customEditorDraft = _customEditorSource;
                _customEditorDraftDirty = false;
            }

            ImGui.EndDisabled();
        }

        NextPreferenceHelp(EditorLocalization.Get(
            "prefs.scriptEditorHelp",
            "VS Code is the default. Script assets reuse the project workspace and open at the requested line."));
        ImGui.EndTable();
    }

    private void DrawEditorPreset(ExternalCodeEditorKind kind, string value, ExternalCodeEditorKind currentKind)
    {
        bool selected = kind == currentKind;
        if (ImGui.Selectable(EditorDisplayName(kind), selected) &&
            Update(_store.Current with { ExternalScriptEditor = value }))
        {
            _customEditorSource = _store.Current.ExternalScriptEditor;
            _customEditorDraft = _customEditorSource;
            _customEditorDraftDirty = false;
        }

        if (selected)
        {
            ImGui.SetItemDefaultFocus();
        }
    }

    internal static bool TryValidateCustomEditorCommand(string command, out string diagnostic)
    {
        string[] tokens = ExternalCodeEditorCommandLine.Split(command, out diagnostic);
        if (tokens.Length == 0 || string.IsNullOrWhiteSpace(tokens[0]))
        {
            diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? "自定义外部编辑器命令不能为空。"
                : diagnostic;
            return false;
        }

        if (tokens[0].Contains("{file}", StringComparison.Ordinal) ||
            tokens[0].Contains("{line}", StringComparison.Ordinal) ||
            tokens[0].Contains("{column}", StringComparison.Ordinal))
        {
            diagnostic = "自定义外部编辑器 executable 不能使用 {file}/{line}/{column} 占位符。";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private void SynchronizeCustomEditorDraft(string command)
    {
        if (_customEditorDraftDirty || string.Equals(_customEditorSource, command, StringComparison.Ordinal))
        {
            return;
        }

        _customEditorSource = command;
        _customEditorDraft = command;
    }

    private static string EditorDisplayName(ExternalCodeEditorKind kind)
    {
        return kind switch
        {
            ExternalCodeEditorKind.VsCode => EditorLocalization.Get("prefs.editor.vscode", "Visual Studio Code (Recommended)"),
            ExternalCodeEditorKind.VisualStudio => EditorLocalization.Get("prefs.editor.visualStudio", "Visual Studio"),
            ExternalCodeEditorKind.Rider => EditorLocalization.Get("prefs.editor.rider", "JetBrains Rider"),
            ExternalCodeEditorKind.SystemDefault => EditorLocalization.Get("prefs.editor.systemDefault", "System Default"),
            ExternalCodeEditorKind.Custom => EditorLocalization.Get("prefs.editor.custom", "Custom Command"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知外部代码编辑器类型。"),
        };
    }

    private static void DrawShortcuts()
    {
        ImGui.SeparatorText(EditorLocalization.Get("prefs.shortcuts", "Shortcuts"));
        TextWrappedUnformatted(EditorLocalization.Get(
            "prefs.shortcutsHelp",
            "These bindings are shared by menus and global command routing. Active text fields keep keyboard priority through ImGui focus routing."));
        if (!ImGui.BeginTable(
            "editor_shortcuts",
            2,
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TableSetupColumn("Action");
        ImGui.TableSetupColumn("Binding");
        ImGui.TableHeadersRow();
        ReadOnlySpan<EditorShortcutDefinition> shortcuts = EditorShortcutCatalog.All;
        for (int i = 0; i < shortcuts.Length; i++)
        {
            ImGui.TableNextRow();
            _ = ImGui.TableNextColumn();
            ImGui.TextUnformatted(shortcuts[i].Action);
            _ = ImGui.TableNextColumn();
            ImGui.TextUnformatted(shortcuts[i].DisplayText);
        }

        ImGui.EndTable();
    }

    private static bool BeginPreferenceFields(string id, float scale)
    {
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!ImGui.BeginTable(
            id,
            2,
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.NoSavedSettings))
        {
            return false;
        }

        ImGui.TableSetupColumn(
            "Property",
            ImGuiTableColumnFlags.WidthFixed,
            ResolvePreferenceLabelWidth(availableWidth, scale));
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        return true;
    }

    internal static float ResolvePreferenceLabelWidth(float availableWidth, float uiScale)
    {
        float available = float.IsFinite(availableWidth) ? MathF.Max(1f, availableWidth) : 1f;
        float scale = EditorUiScale.Normalize(uiScale);
        float minimum = EditorUiScale.Scale(120f, scale);
        float maximum = EditorUiScale.Scale(220f, scale);
        float minimumValueWidth = EditorUiScale.Scale(160f, scale);
        float preferred = Math.Clamp(available * 0.34f, minimum, maximum);
        return MathF.Min(preferred, MathF.Max(1f, available - minimumValueWidth));
    }

    private static void NextPreferenceField(string label)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        TextWrappedUnformatted(label);
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1f);
    }

    private static void NextPreferenceHelp(string text)
    {
        NextPreferenceValueRow();
        TextWrappedUnformatted(text);
    }

    private static void NextPreferenceValueRow()
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(1);
    }

    private static void TextWrappedUnformatted(string text)
    {
        float contentWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private bool Update(EditorPreferencesDocument next)
    {
        bool updated = _store.TryUpdate(next, out string diagnostic);
        _diagnostic = updated ? string.Empty : diagnostic;
        return updated;
    }
}
