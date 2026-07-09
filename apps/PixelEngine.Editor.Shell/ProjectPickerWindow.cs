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
    private ProjectPickerMode _mode;

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
        _mode = string.IsNullOrWhiteSpace(options.ProjectPath)
            ? ProjectPickerMode.NewProject
            : ProjectPickerMode.OpenProject;
    }

    internal string FolderPickerDiagnostic { get; private set; } = string.Empty;

    public void Focus(ProjectPickerMode mode)
    {
        _mode = mode;
    }

    public void Draw(EditorShellApp app)
    {
        ImGui.SetNextWindowPos(new Vector2(24, 48), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(680, 500), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Project Picker"))
        {
            ImGui.End();
            return;
        }

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

        ImGui.TextUnformatted(_mode == ProjectPickerMode.NewProject
            ? "Mode: New Project"
            : "Mode: Open Project");
        ImGui.Spacing();
        if (ImGui.BeginTabBar("ProjectPickerTabs"))
        {
            DrawNewProjectTab(app);
            DrawOpenProjectTab(app);
            DrawRecentProjectsTab(app);
            ImGui.EndTabBar();
        }

        if (!string.IsNullOrWhiteSpace(app.LastProjectError))
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.25f, 1.0f), app.LastProjectError);
        }

        ImGui.End();
    }

    private void DrawNewProjectTab(EditorShellApp app)
    {
        if (!ImGui.BeginTabItem("New Project"))
        {
            return;
        }

        _mode = ProjectPickerMode.NewProject;
        _ = ImGui.InputText("Project Name", ref _newProjectName, 128);
        DrawPathInputWithBrowse("Project Directory", ref _newProjectRoot, "new_project_root");
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

    private void DrawOpenProjectTab(EditorShellApp app)
    {
        if (!ImGui.BeginTabItem("Open Project"))
        {
            return;
        }

        _mode = ProjectPickerMode.OpenProject;
        DrawPathInputWithBrowse("Project Root or project.pixelproj", ref _openProjectPath, "open_project_root");
        if (ImGui.Button("Open Project"))
        {
            app.OpenProjectPath(_openProjectPath);
        }

        ImGui.EndTabItem();
    }

    private void DrawPathInputWithBrowse(string label, ref string path, string id)
    {
        const float BrowseButtonWidth = 88f;
        ImGui.TextUnformatted(label);
        float inputWidth = Math.Min(520f, Math.Max(240f, ImGui.GetContentRegionAvail().X - BrowseButtonWidth - ImGui.GetStyle().ItemSpacing.X));
        ImGui.PushItemWidth(inputWidth);
        _ = ImGui.InputText($"##{id}_path", ref path, 512);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button($"Browse...##{id}", new Vector2(BrowseButtonWidth, 0f)))
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

    private static void DrawRecentProjectsTab(EditorShellApp app)
    {
        if (!ImGui.BeginTabItem("Recent"))
        {
            return;
        }

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
