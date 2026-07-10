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
    Action resetLayout) : IEditorPanel
{
    private static readonly (EditorPreferencesCategory Category, string Label)[] Categories =
    [
        (EditorPreferencesCategory.Appearance, "Appearance"),
        (EditorPreferencesCategory.General, "General"),
        (EditorPreferencesCategory.ExternalTools, "External Tools"),
        (EditorPreferencesCategory.Shortcuts, "Shortcuts"),
    ];

    private readonly EditorPreferencesStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Action _resetLayout = resetLayout ?? throw new ArgumentNullException(nameof(resetLayout));
    private string _diagnostic = store.LastDiagnostic;
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
        Vector2 tableSize = ImGui.GetContentRegionAvail();
        LastNavigationVisible = ImGui.BeginTable(
            "preferences_layout",
            2,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
            tableSize);
        if (LastNavigationVisible)
        {
            ImGui.TableSetupColumn("Categories", ImGuiTableColumnFlags.WidthFixed, navigationWidth);
            ImGui.TableSetupColumn("Settings", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            _ = ImGui.TableNextColumn();
            for (int i = 0; i < Categories.Length; i++)
            {
                (EditorPreferencesCategory category, string label) = Categories[i];
                if (ImGui.Selectable(label, SelectedCategory == category))
                {
                    SelectedCategory = category;
                }
            }

            _ = ImGui.TableNextColumn();
            DrawSelectedCategory();
            string diagnostic = string.IsNullOrWhiteSpace(_diagnostic) ? _store.LastDiagnostic : _diagnostic;
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                ImGui.SeparatorText("Diagnostic");
                ImGui.TextWrapped(diagnostic);
            }

            ImGui.EndTable();
        }
        ImGui.End();
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
        ImGui.SeparatorText("Appearance");
        float percent = EditorUiScale.ToPercent(_store.Current.UiScale);
        if (ImGui.SliderFloat(
            "UI Scale",
            ref percent,
            EditorUiScale.Minimum * 100f,
            EditorUiScale.Maximum * 100f,
            "%.0f%%"))
        {
            float nextScale = EditorUiScale.Normalize(percent / 100f);
            Update(_store.Current with { UiScale = nextScale });
        }

        ImGui.TextWrapped("4K 显示器推荐 150%。字体、菜单、间距、滚动条和工具栏尺寸会一起缩放。");
        ImGui.TextWrapped("缩放会立即应用；重启后字体 atlas 会按目标像素大小重建，以获得最清晰的文字。");
        ImGui.Spacing();
        ImGui.TextUnformatted("Theme");
        ImGui.SameLine();
        ImGui.TextDisabled("Unity 6 Dark");
    }

    private void DrawGeneral()
    {
        ImGui.SeparatorText("General");
        bool saveLayout = _store.Current.SaveLayoutOnExit;
        if (ImGui.Checkbox("Save layout on exit", ref saveLayout))
        {
            Update(_store.Current with { SaveLayoutOnExit = saveLayout });
        }

        ImGui.TextWrapped("关闭时保存窗口停靠和尺寸；关闭此选项会保留上一次已保存的布局。");
        bool reopenLastProject = _store.Current.ReopenLastProject;
        if (ImGui.Checkbox("Reopen last project on startup", ref reopenLastProject))
        {
            Update(_store.Current with { ReopenLastProject = reopenLastProject });
        }

        ImGui.TextWrapped("无显式 --project 时恢复最后一次成功打开的工程；自动化和上次异常退出不会盲目重试。");
        bool restoreLastScene = _store.Current.RestoreLastScene;
        if (ImGui.Checkbox("Restore last open scene", ref restoreLastScene))
        {
            Update(_store.Current with { RestoreLastScene = restoreLastScene });
        }

        ImGui.TextWrapped("当前编辑场景保存在用户 workspace，不会改写工程的 Start Scene。");
        if (ImGui.Button("Reset to Default Layout"))
        {
            _resetLayout();
        }
    }

    private void DrawExternalTools()
    {
        ImGui.SeparatorText("External Tools");
        string command = _store.Current.ExternalScriptEditor;
        if (ImGui.InputText("Script Editor", ref command, 1024))
        {
            Update(_store.Current with { ExternalScriptEditor = command });
        }

        ImGui.TextWrapped("留空或填写 system-default 使用系统默认程序。自定义命令可使用 {file} 占位符；未写占位符时会自动追加脚本路径。");
        if (ImGui.Button("Use System Default"))
        {
            Update(_store.Current with { ExternalScriptEditor = string.Empty });
        }
    }

    private static void DrawShortcuts()
    {
        ImGui.SeparatorText("Shortcuts");
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

    private void Update(EditorPreferencesDocument next)
    {
        _diagnostic = _store.TryUpdate(next, out string diagnostic)
            ? string.Empty
            : diagnostic;
    }
}
