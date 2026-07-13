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
            ImGui.SeparatorText("Diagnostic");
            ImGui.TextWrapped(diagnostic);
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
        DrawLanguageSelector();
        float percent = EditorUiScale.ToPercent(_store.Current.UiScale);
        if (ImGui.SliderFloat(
            EditorLocalization.Get("prefs.uiScale", "UI Scale"),
            ref percent,
            EditorUiScale.Minimum * 100f,
            EditorUiScale.Maximum * 100f,
            "%.0f%%"))
        {
            float nextScale = EditorUiScale.Normalize(percent / 100f);
            _ = Update(_store.Current with { UiScale = nextScale });
        }

        ImGui.TextWrapped("4K 显示器推荐 150%。字体、菜单、间距、滚动条和工具栏尺寸会一起缩放。");
        ImGui.TextWrapped("缩放会立即应用；重启后字体 atlas 会按目标像素大小重建，以获得最清晰的文字。");
        ImGui.Spacing();
        ImGui.TextUnformatted(EditorLocalization.Get("prefs.theme", "Theme"));
        ImGui.SameLine();
        ImGui.TextDisabled("Unity 6 Dark");
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

        if (!ImGui.BeginCombo(EditorLocalization.Get("prefs.language", "Language"), currentDisplay))
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
        bool saveLayout = _store.Current.SaveLayoutOnExit;
        if (ImGui.Checkbox(EditorLocalization.Get("prefs.saveLayout", "Save layout continuously"), ref saveLayout))
        {
            _ = Update(_store.Current with { SaveLayoutOnExit = saveLayout });
        }

        ImGui.TextWrapped("关闭时保存窗口停靠和尺寸；关闭此选项会保留上一次已保存的布局。");
        bool reopenLastProject = _store.Current.ReopenLastProject;
        if (ImGui.Checkbox(EditorLocalization.Get("prefs.reopenProject", "Reopen last project on startup"), ref reopenLastProject))
        {
            _ = Update(_store.Current with { ReopenLastProject = reopenLastProject });
        }

        ImGui.TextWrapped("无显式 --project 时恢复最后一次成功打开的工程；自动化和上次异常退出不会盲目重试。");
        bool restoreLastScene = _store.Current.RestoreLastScene;
        if (ImGui.Checkbox(EditorLocalization.Get("prefs.restoreScene", "Restore last open scene"), ref restoreLastScene))
        {
            _ = Update(_store.Current with { RestoreLastScene = restoreLastScene });
        }

        ImGui.TextWrapped("当前编辑场景保存在用户 workspace，不会改写工程的 Start Scene。");
        if (ImGui.Button(EditorLocalization.Get("prefs.resetLayout", "Reset to Default Layout")))
        {
            _resetLayout();
        }
    }

    private void DrawExternalTools()
    {
        ImGui.SeparatorText(EditorLocalization.Get("prefs.externalTools", "External Tools"));
        string command = _store.Current.ExternalScriptEditor;
        ExternalCodeEditorKind currentKind = ExternalCodeEditorPreference.Classify(command);
        string preview = EditorDisplayName(currentKind);
        if (ImGui.BeginCombo(EditorLocalization.Get("prefs.scriptEditor", "Script Editor"), preview))
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
            if (ImGui.InputText(
                EditorLocalization.Get("prefs.customEditorCommand", "Custom Command"),
                ref _customEditorDraft,
                1024))
            {
                _customEditorDraftDirty = !string.Equals(
                    _customEditorDraft,
                    _customEditorSource,
                    StringComparison.Ordinal);
            }

            ImGui.TextWrapped(EditorLocalization.Get(
                "prefs.customEditorHelp",
                "Placeholders: {file}, {line}, {column}, {project}. Without {file}, the script path is appended."));
            bool valid = TryValidateCustomEditorCommand(_customEditorDraft, out string validationDiagnostic);
            if (!valid)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f), validationDiagnostic);
            }

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

        ImGui.TextWrapped(EditorLocalization.Get(
            "prefs.scriptEditorHelp",
            "VS Code is the default. Script assets reuse the project workspace and open at the requested line."));
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
        ImGui.TextWrapped("以下快捷键由菜单和全局命令调度共用；文本框正在编辑时由 ImGui 焦点路由优先处理。");
        if (!ImGui.BeginTable("editor_shortcuts", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
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

    private bool Update(EditorPreferencesDocument next)
    {
        bool updated = _store.TryUpdate(next, out string diagnostic);
        _diagnostic = updated ? string.Empty : diagnostic;
        return updated;
    }
}
