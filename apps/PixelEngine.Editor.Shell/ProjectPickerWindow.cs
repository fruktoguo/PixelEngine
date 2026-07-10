using Hexa.NET.ImGui;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 启动时项目选择/创建窗口。
/// </summary>
internal sealed class ProjectPickerWindow
{
    private readonly IProjectFolderPicker _folderPicker;
    private string _newProjectRoot;
    private string _newProjectName = "NewPixelProject";
    private string _openProjectPath;
    private bool _selectModeOnNextDraw = true;
    private float _lastUiScale = float.NaN;

    public ProjectPickerWindow(EditorShellOptions options)
        : this(options, NativeProjectFolderPicker.Instance)
    {
    }

    internal ProjectPickerWindow(EditorShellOptions options, IProjectFolderPicker folderPicker)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        string defaultProjectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PixelEngine Projects");
        _newProjectRoot = Path.Combine(defaultProjectsRoot, _newProjectName);
        _openProjectPath = options.ProjectPath ?? defaultProjectsRoot;
        Mode = string.IsNullOrWhiteSpace(options.ProjectPath)
            ? ProjectPickerMode.NewProject
            : ProjectPickerMode.OpenProject;
    }

    internal string FolderPickerDiagnostic { get; private set; } = string.Empty;

    public bool Visible { get; private set; } = true;

    internal ProjectPickerMode Mode { get; private set; }

    internal ProjectPickerMode? PendingTabSelection => _selectModeOnNextDraw ? Mode : null;

    public void Focus(ProjectPickerMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知 Project Picker 模式。");
        }

        Mode = mode;
        _selectModeOnNextDraw = true;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
    }

    public void Draw(EditorShellApp app)
    {
        if (!Visible)
        {
            return;
        }

        float scale = app.UiScale;
        ImGuiCond placementCondition = MathF.Abs(scale - _lastUiScale) > 0.0001f
            ? ImGuiCond.Always
            : ImGuiCond.FirstUseEver;
        ImGui.SetNextWindowPos(
            new Vector2(EditorUiScale.Scale(24f, scale), EditorUiScale.Scale(48f, scale)),
            placementCondition);
        ImGui.SetNextWindowSize(
            EditorUiScale.FitWindow(new Vector2(680f, 500f), scale, ImGui.GetMainViewport().WorkSize),
            placementCondition);
        _lastUiScale = scale;
        bool visible = Visible;
        if (!ImGui.Begin("Project Picker", ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        ImGui.TextUnformatted("PixelEngine Editor");
        ImGui.Separator();
        if (app.HasOpenProject)
        {
            ImGui.TextUnformatted($"Open: {app.CurrentProject!.Name}");
            ImGui.TextUnformatted(app.CurrentProject.ProjectRoot);
        }
        else
        {
            ImGui.TextUnformatted("No project selected");
        }

        ImGui.TextUnformatted(Mode switch
        {
            ProjectPickerMode.NewProject => "Mode: New Project",
            ProjectPickerMode.OpenProject => "Mode: Open Project",
            ProjectPickerMode.RecentProjects => "Mode: Recent Projects",
            _ => "Mode: Project Picker",
        });
        ImGui.Spacing();
        if (ImGui.BeginTabBar("ProjectPickerTabs"))
        {
            DrawNewProjectTab(app, GetTabItemFlags(ProjectPickerMode.NewProject));
            DrawOpenProjectTab(app, GetTabItemFlags(ProjectPickerMode.OpenProject));
            DrawRecentProjectsTab(app, GetTabItemFlags(ProjectPickerMode.RecentProjects));
            ImGui.EndTabBar();
            _selectModeOnNextDraw = false;
        }

        if (!string.IsNullOrWhiteSpace(app.LastProjectError))
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.25f, 1.0f), app.LastProjectError);
        }

        ImGui.End();
    }

    private void DrawNewProjectTab(EditorShellApp app, ImGuiTabItemFlags flags)
    {
        if (!ImGui.BeginTabItem("New Project", flags))
        {
            return;
        }

        Mode = ProjectPickerMode.NewProject;
        _ = ImGui.InputText("Project Name", ref _newProjectName, 128);
        DrawPathInputWithBrowse("Project Directory", ref _newProjectRoot, "new_project_root", app.UiScale);
        if (ImGui.Button("Create Project"))
        {
            app.CreateProject(_newProjectRoot, _newProjectName);
            if (app.HasOpenProject)
            {
                _openProjectPath = app.CurrentProject!.ProjectRoot;
            }
        }

        ImGui.EndTabItem();
    }

    private void DrawOpenProjectTab(EditorShellApp app, ImGuiTabItemFlags flags)
    {
        if (!ImGui.BeginTabItem("Open Project", flags))
        {
            return;
        }

        Mode = ProjectPickerMode.OpenProject;
        DrawPathInputWithBrowse("Project Root or project.pixelproj", ref _openProjectPath, "open_project_root", app.UiScale);
        if (ImGui.Button("Open Project"))
        {
            app.OpenProjectPath(_openProjectPath);
        }

        ImGui.EndTabItem();
    }

    private ImGuiTabItemFlags GetTabItemFlags(ProjectPickerMode mode)
    {
        return _selectModeOnNextDraw && Mode == mode
            ? ImGuiTabItemFlags.SetSelected
            : ImGuiTabItemFlags.None;
    }

    private void DrawPathInputWithBrowse(string label, ref string path, string id, float uiScale)
    {
        const float BrowseButtonWidth = 88f;
        float scaledButtonWidth = EditorUiScale.Scale(BrowseButtonWidth, uiScale);
        ImGui.TextUnformatted(label);
        float inputWidth = Math.Min(
            EditorUiScale.Scale(520f, uiScale),
            Math.Max(
                EditorUiScale.Scale(240f, uiScale),
                ImGui.GetContentRegionAvail().X - scaledButtonWidth - ImGui.GetStyle().ItemSpacing.X));
        ImGui.PushItemWidth(inputWidth);
        _ = ImGui.InputText($"##{id}_path", ref path, 512);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button($"Browse...##{id}", new Vector2(scaledButtonWidth, 0f)))
        {
            _ = ApplyFolderPicker(ref path);
        }

        if (!string.IsNullOrWhiteSpace(FolderPickerDiagnostic))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.55f, 0.25f, 1.0f), FolderPickerDiagnostic);
        }
    }

    internal bool ApplyFolderPicker(ref string path)
    {
        if (_folderPicker.TryPickFolder(path, out string selectedPath, out string diagnostic))
        {
            path = selectedPath;
            FolderPickerDiagnostic = string.Empty;
            return true;
        }

        FolderPickerDiagnostic = diagnostic;
        return false;
    }

    private void DrawRecentProjectsTab(EditorShellApp app, ImGuiTabItemFlags flags)
    {
        if (!ImGui.BeginTabItem("Recent", flags))
        {
            return;
        }

        Mode = ProjectPickerMode.RecentProjects;

        IReadOnlyList<RecentProjectEntry> entries = app.RecentProjects.Entries;
        if (entries.Count == 0)
        {
            ImGui.TextUnformatted("No recent projects");
            ImGui.EndTabItem();
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            RecentProjectEntry entry = entries[i];
            bool exists = File.Exists(Path.Combine(entry.ProjectPath, EditorProject.ProjectFileName));
            if (!exists)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Selectable($"{entry.Name}##recent_{i}", selected: false))
            {
                app.OpenProjectPath(entry.ProjectPath);
            }

            ImGui.TextUnformatted(entry.ProjectPath);
            ImGui.TextUnformatted($"Last opened UTC: {entry.LastOpenedUtc:yyyy-MM-dd HH:mm:ss}");
            ImGui.Separator();
            if (!exists)
            {
                ImGui.EndDisabled();
            }
        }

        ImGui.EndTabItem();
    }
}

/// <summary>
/// 项目选择器模式：打开或创建。
/// </summary>
internal enum ProjectPickerMode
{
    NewProject,
    OpenProject,
    RecentProjects,
}

internal interface IProjectFolderPicker
{
    bool TryPickFolder(string initialPath, out string selectedPath, out string diagnostic);
}

internal sealed class NativeProjectFolderPicker : IProjectFolderPicker
{
    public static NativeProjectFolderPicker Instance { get; } = new();

    private NativeProjectFolderPicker()
    {
    }

    public bool TryPickFolder(string initialPath, out string selectedPath, out string diagnostic)
    {
        return NativeFolderPicker.TryPickFolder(initialPath, out selectedPath, out diagnostic);
    }
}
