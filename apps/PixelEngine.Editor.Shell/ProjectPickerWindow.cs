using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 启动时全工作区项目浏览、创建与打开界面。
/// </summary>
internal sealed class ProjectPickerWindow
{
    private const float SidebarWidth = 188f;
    private const float ContentPadding = 28f;
    private const float HeaderSearchWidth = 220f;
    private const float HeaderAddWidth = 78f;
    private const float HeaderNewWidth = 116f;
    private const float BrowseButtonWidth = 88f;
    private static readonly Vector4 SelectedNavigationColor = new(0.18f, 0.34f, 0.49f, 1f);
    private static readonly Vector4 PrimaryButtonColor = new(0.12f, 0.43f, 0.72f, 1f);
    private static readonly Vector4 PrimaryButtonHoveredColor = new(0.16f, 0.50f, 0.82f, 1f);
    private static readonly Vector4 PrimaryButtonActiveColor = new(0.09f, 0.36f, 0.62f, 1f);
    private static readonly Vector4 WarningColor = new(1.0f, 0.55f, 0.25f, 1.0f);
    private static readonly Vector4 ErrorColor = new(1.0f, 0.35f, 0.25f, 1.0f);

    private readonly IProjectFolderPicker _folderPicker;
    private string _newProjectLocation;
    private string _newProjectName = "NewPixelProject";
    private string _openProjectPath;
    private string _recentSearch = string.Empty;
    private bool _selectModeOnNextDraw = true;

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
        _newProjectLocation = defaultProjectsRoot;
        _openProjectPath = options.ProjectPath ?? defaultProjectsRoot;
        Mode = string.IsNullOrWhiteSpace(options.ProjectPath)
            ? ProjectPickerMode.RecentProjects
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

        Navigate(mode);
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

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.WorkSize, ImGuiCond.Always);
        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoDocking;
        if (!ImGui.Begin("Project Picker##project-picker-surface", flags))
        {
            ImGui.End();
            return;
        }

        Vector2 windowSize = ImGui.GetWindowSize();
        float scale = app.UiScale;
        float sidebarWidth = Math.Clamp(
            EditorUiScale.Scale(SidebarWidth, scale),
            EditorUiScale.Scale(148f, scale),
            MathF.Max(EditorUiScale.Scale(148f, scale), windowSize.X * 0.34f));

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.075f, 0.08f, 0.09f, 1f));
        _ = ImGui.BeginChild(
            "project-picker-navigation",
            new Vector2(sidebarWidth, windowSize.Y),
            ImGuiChildFlags.Borders);
        DrawNavigation(app, scale);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 0f);
        _ = ImGui.BeginChild(
            "project-picker-content",
            new Vector2(MathF.Max(1f, windowSize.X - sidebarWidth), windowSize.Y));
        switch (Mode)
        {
            case ProjectPickerMode.NewProject:
                DrawNewProjectPage(app, scale);
                break;
            case ProjectPickerMode.OpenProject:
                DrawOpenProjectPage(app, scale);
                break;
            case ProjectPickerMode.RecentProjects:
                DrawProjectsPage(app, scale);
                break;
            default:
                throw new InvalidOperationException($"未知 Project Picker 模式：{Mode}。");
        }

        ImGui.EndChild();
        ImGui.End();
        _selectModeOnNextDraw = false;
    }

    private void DrawNavigation(EditorShellApp app, float scale)
    {
        float padding = EditorUiScale.Scale(18f, scale);
        float buttonWidth = MathF.Max(1f, ImGui.GetWindowSize().X - (padding * 2f));
        ImGui.SetCursorPos(new Vector2(padding, EditorUiScale.Scale(22f, scale)));
        ImGui.TextUnformatted("PixelEngine");
        ImGui.TextDisabled("Project Browser");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, SelectedNavigationColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.40f, 0.58f, 1f));
        if (ImGui.Button("Projects", new Vector2(buttonWidth, 0f)))
        {
            Navigate(ProjectPickerMode.RecentProjects);
        }

        ImGui.PopStyleColor(2);

        float frameHeight = ImGui.GetFrameHeight();
        float spacing = ImGui.GetStyle().ItemSpacing.Y;
        float bottomControlsHeight = app.HasOpenProject
            ? (frameHeight * 2f) + spacing
            : frameHeight;
        float bottomY = MathF.Max(
            ImGui.GetCursorPosY() + spacing,
            ImGui.GetWindowSize().Y - padding - bottomControlsHeight);
        ImGui.SetCursorPos(new Vector2(padding, bottomY));
        if (app.HasOpenProject && ImGui.Button("Back to Editor", new Vector2(buttonWidth, 0f)))
        {
            Close();
        }

        if (app.HasOpenProject)
        {
            ImGui.SetCursorPosX(padding);
        }

        if (ImGui.Button("Settings", new Vector2(buttonWidth, 0f)))
        {
            app.ShowPreferences();
        }
    }

    private void DrawProjectsPage(EditorShellApp app, float scale)
    {
        float padding = EditorUiScale.Scale(ContentPadding, scale);
        float contentWidth = MathF.Max(1f, ImGui.GetWindowSize().X - (padding * 2f));
        float searchWidth = EditorUiScale.Scale(HeaderSearchWidth, scale);
        float addWidth = EditorUiScale.Scale(HeaderAddWidth, scale);
        float newWidth = EditorUiScale.Scale(HeaderNewWidth, scale);
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float controlsWidth = searchWidth + addWidth + newWidth + (spacing * 2f);

        ImGui.SetCursorPos(new Vector2(padding, padding));
        ImGui.TextUnformatted("Projects");
        bool compactHeader = contentWidth < EditorUiScale.Scale(650f, scale);
        if (compactHeader)
        {
            ImGui.SetCursorPos(new Vector2(padding, ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y));
            searchWidth = MathF.Max(
                EditorUiScale.Scale(112f, scale),
                contentWidth - addWidth - newWidth - (spacing * 2f));
        }
        else
        {
            ImGui.SameLine(MathF.Max(padding, padding + contentWidth - controlsWidth));
        }

        ImGui.SetNextItemWidth(searchWidth);
        _ = ImGui.InputTextWithHint("##project-picker-search", "Search", ref _recentSearch, 256);
        ImGui.SameLine();
        if (ImGui.Button("Add##project-picker-add", new Vector2(addWidth, 0f)))
        {
            ImGui.OpenPopup("project-picker-add-menu");
        }

        if (ImGui.BeginPopup("project-picker-add-menu"))
        {
            if (ImGui.MenuItem("Add project from disk..."))
            {
                Navigate(ProjectPickerMode.OpenProject);
                TryOpenFromFolderPicker(app);
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (DrawPrimaryButton("+ New project", new Vector2(newWidth, 0f)))
        {
            Navigate(ProjectPickerMode.NewProject);
        }

        ImGui.SetCursorPosX(padding);
        ImGui.Separator();
        DrawDiagnostic(app, contentWidth);
        DrawRecentProjects(app, contentWidth, padding);
    }

    private void DrawRecentProjects(EditorShellApp app, float contentWidth, float padding)
    {
        IReadOnlyList<RecentProjectEntry> entries = app.RecentProjects.Entries;
        int matchingCount = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (RecentProjectMatchesSearch(entries[i], _recentSearch))
            {
                matchingCount++;
            }
        }

        if (matchingCount == 0)
        {
            ImGui.SetCursorPosX(padding);
            ImGui.Spacing();
            ImGui.TextDisabled(entries.Count == 0 ? "No projects yet" : "No projects match your search");
            ImGui.TextWrapped(entries.Count == 0
                ? "Create a PixelEngine project or add an existing project from disk."
                : "Try a different project name or path.");
            if (entries.Count == 0 && DrawPrimaryButton("+ New project", Vector2.Zero))
            {
                Navigate(ProjectPickerMode.NewProject);
            }

            if (entries.Count == 0)
            {
                ImGui.SameLine();
                if (ImGui.Button("Add from disk..."))
                {
                    Navigate(ProjectPickerMode.OpenProject);
                    TryOpenFromFolderPicker(app);
                }
            }

            return;
        }

        ImGui.SetCursorPosX(padding);
        float tableHeight = MathF.Max(96f, ImGui.GetContentRegionAvail().Y - padding);
        ImGuiTableFlags flags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("project-picker-recent", 5, flags, new Vector2(contentWidth, tableHeight)))
        {
            return;
        }

        ImGui.TableSetupColumn("★", ImGuiTableColumnFlags.WidthFixed, 34f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("Last opened", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 74f);
        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 34f);
        ImGui.TableHeadersRow();

        DateTimeOffset now = DateTimeOffset.Now;
        for (int i = 0; i < entries.Count; i++)
        {
            RecentProjectEntry entry = entries[i];
            if (!RecentProjectMatchesSearch(entry, _recentSearch))
            {
                continue;
            }

            bool exists = File.Exists(Path.Combine(entry.ProjectPath, EditorProject.ProjectFileName));
            bool removed = false;
            ImGui.TableNextRow();
            _ = ImGui.TableNextColumn();
            if (ImGui.Button($"{(entry.Favorite ? "★" : "☆")}##recent-favorite-{i}"))
            {
                app.SetRecentProjectFavorite(entry.ProjectPath, !entry.Favorite);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(entry.Favorite ? "Remove from favorites" : "Add to favorites");
            }

            _ = ImGui.TableNextColumn();
            if (!exists)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Selectable($"{entry.Name}##recent-project-{i}", selected: false))
            {
                app.OpenProjectPath(entry.ProjectPath);
            }

            if (!exists)
            {
                ImGui.EndDisabled();
            }

            ImGui.TextDisabled(entry.ProjectPath);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(entry.ProjectPath);
            }

            _ = ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatLastOpened(entry.LastOpenedUtc, now));

            _ = ImGui.TableNextColumn();
            if (exists)
            {
                ImGui.TextDisabled("Ready");
            }
            else
            {
                ImGui.TextColored(WarningColor, "Missing");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Missing {EditorProject.ProjectFileName}");
                }
            }

            _ = ImGui.TableNextColumn();
            if (ImGui.Button($"...##recent-options-{i}"))
            {
                ImGui.OpenPopup($"recent-options-menu-{i}");
            }

            if (ImGui.BeginPopup($"recent-options-menu-{i}"))
            {
                if (!exists)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.MenuItem("Open project"))
                {
                    app.OpenProjectPath(entry.ProjectPath);
                }

                if (!exists)
                {
                    ImGui.EndDisabled();
                }

                if (ImGui.MenuItem(entry.Favorite ? "Remove from favorites" : "Add to favorites"))
                {
                    app.SetRecentProjectFavorite(entry.ProjectPath, !entry.Favorite);
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Remove from list"))
                {
                    app.RemoveRecentProject(entry.ProjectPath);
                    removed = true;
                }

                ImGui.EndPopup();
            }

            if (removed)
            {
                break;
            }
        }

        ImGui.EndTable();
    }

    private void DrawNewProjectPage(EditorShellApp app, float scale)
    {
        float padding = EditorUiScale.Scale(ContentPadding, scale);
        float contentWidth = MathF.Max(1f, ImGui.GetWindowSize().X - (padding * 2f));
        DrawPageHeader("New project", padding);
        DrawDiagnostic(app, contentWidth);

        ImGui.SetCursorPosX(padding);
        float tableHeight = MathF.Max(180f, ImGui.GetContentRegionAvail().Y - padding);
        if (!ImGui.BeginTable(
            "project-picker-new-layout",
            2,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp,
            new Vector2(contentWidth, tableHeight)))
        {
            return;
        }

        ImGui.TableSetupColumn("Templates", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Project settings", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableNextRow();
        _ = ImGui.TableNextColumn();
        ImGui.TextUnformatted("Templates");
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Header, SelectedNavigationColor);
        _ = ImGui.Selectable("PixelEngine 2D\nFalling-sand world, scripting, UI and editor-ready scene", selected: true);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.TextWrapped("The built-in template creates a complete PixelEngine project with content, scripts and a playable main scene.");

        _ = ImGui.TableNextColumn();
        ImGui.TextUnformatted("Project settings");
        ImGui.Separator();
        ImGui.TextUnformatted("Project name");
        ImGui.SetNextItemWidth(-1f);
        _ = ImGui.InputText("##new-project-name", ref _newProjectName, 128);
        DrawPathInputWithBrowse("Location", ref _newProjectLocation, "new_project_location", app.UiScale);

        bool valid = TryResolveNewProjectPath(
            _newProjectLocation,
            _newProjectName,
            out string projectPath,
            out string validationDiagnostic);
        ImGui.Spacing();
        ImGui.TextDisabled("Project path");
        ImGui.TextWrapped(valid ? projectPath : validationDiagnostic);
        if (!valid)
        {
            ImGui.TextColored(WarningColor, validationDiagnostic);
        }

        ImGui.Spacing();
        float createWidth = EditorUiScale.Scale(132f, scale);
        float createX = ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - createWidth);
        ImGui.SetCursorPosX(createX);
        if (!valid)
        {
            ImGui.BeginDisabled();
        }

        if (DrawPrimaryButton("+ Create project", new Vector2(createWidth, 0f)))
        {
            _openProjectPath = projectPath;
            app.CreateProject(projectPath, _newProjectName.Trim());
        }

        if (!valid)
        {
            ImGui.EndDisabled();
        }

        ImGui.EndTable();
    }

    private void DrawOpenProjectPage(EditorShellApp app, float scale)
    {
        float padding = EditorUiScale.Scale(ContentPadding, scale);
        float contentWidth = MathF.Max(1f, ImGui.GetWindowSize().X - (padding * 2f));
        DrawPageHeader("Add project from disk", padding);
        DrawDiagnostic(app, contentWidth);

        ImGui.SetCursorPosX(padding);
        ImGui.TextWrapped($"Select a folder containing {EditorProject.ProjectFileName}, or enter the folder/project file path directly.");
        ImGui.Spacing();
        DrawPathInputWithBrowse("Project root or project.pixelproj", ref _openProjectPath, "open_project_root", app.UiScale);
        bool hasPath = !string.IsNullOrWhiteSpace(_openProjectPath);
        if (!hasPath)
        {
            ImGui.TextColored(WarningColor, "Choose a project folder before opening.");
            ImGui.BeginDisabled();
        }

        if (DrawPrimaryButton(
            "Open project",
            new Vector2(EditorUiScale.Scale(112f, scale), 0f)))
        {
            app.OpenProjectPath(_openProjectPath);
        }

        if (!hasPath)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawPageHeader(string title, float padding)
    {
        ImGui.SetCursorPos(new Vector2(padding, padding));
        if (ImGui.Button("<##project-picker-back"))
        {
            Navigate(ProjectPickerMode.RecentProjects);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(title);
        ImGui.SetCursorPosX(padding);
        ImGui.Separator();
    }

    private static bool DrawPrimaryButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, PrimaryButtonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PrimaryButtonHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, PrimaryButtonActiveColor);
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private void DrawDiagnostic(EditorShellApp app, float contentWidth)
    {
        string diagnostic = !string.IsNullOrWhiteSpace(FolderPickerDiagnostic)
            ? FolderPickerDiagnostic
            : app.LastProjectError ?? string.Empty;
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return;
        }

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
        ImGui.TextColored(
            string.IsNullOrWhiteSpace(FolderPickerDiagnostic) ? ErrorColor : WarningColor,
            diagnostic);
        ImGui.PopTextWrapPos();
        ImGui.Separator();
    }

    private void DrawPathInputWithBrowse(string label, ref string path, string id, float uiScale)
    {
        float scaledButtonWidth = EditorUiScale.Scale(BrowseButtonWidth, uiScale);
        ImGui.TextUnformatted(label);
        float inputWidth = MathF.Max(
            EditorUiScale.Scale(160f, uiScale),
            ImGui.GetContentRegionAvail().X - scaledButtonWidth - ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(inputWidth);
        _ = ImGui.InputText($"##{id}_path", ref path, 512);
        ImGui.SameLine();
        if (ImGui.Button($"Browse...##{id}", new Vector2(scaledButtonWidth, 0f)))
        {
            _ = ApplyFolderPicker(ref path);
        }
    }

    private void TryOpenFromFolderPicker(EditorShellApp app)
    {
        string path = _openProjectPath;
        if (!ApplyFolderPicker(ref path))
        {
            return;
        }

        _openProjectPath = path;
        app.OpenProjectPath(path);
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

    internal static bool RecentProjectMatchesSearch(RecentProjectEntry entry, string? search)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        string query = search.Trim();
        return entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.ProjectPath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatLastOpened(DateTimeOffset lastOpened, DateTimeOffset now)
    {
        TimeSpan elapsed = now - lastOpened;
        if (elapsed <= TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            int minutes = Math.Max(1, (int)elapsed.TotalMinutes);
            return $"{minutes.ToString(CultureInfo.InvariantCulture)} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            int hours = Math.Max(1, (int)elapsed.TotalHours);
            return $"{hours.ToString(CultureInfo.InvariantCulture)} hr ago";
        }

        if (elapsed < TimeSpan.FromDays(30))
        {
            int days = Math.Max(1, (int)elapsed.TotalDays);
            return $"{days.ToString(CultureInfo.InvariantCulture)} days ago";
        }

        return lastOpened.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static bool TryResolveNewProjectPath(
        string? location,
        string? projectName,
        out string projectPath,
        out string diagnostic)
    {
        projectPath = string.Empty;
        diagnostic = string.Empty;
        string name = projectName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            diagnostic = "Project name is required.";
            return false;
        }

        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            diagnostic = "Project name contains characters that cannot be used in a folder name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            diagnostic = "Project location is required.";
            return false;
        }

        try
        {
            string root = Path.GetFullPath(location.Trim());
            projectPath = Path.GetFullPath(Path.Combine(root, name));
            string relative = Path.GetRelativePath(root, projectPath);
            if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            {
                diagnostic = "Project path must stay inside the selected location.";
                projectPath = string.Empty;
                return false;
            }

            if (File.Exists(Path.Combine(projectPath, EditorProject.ProjectFileName)))
            {
                diagnostic = "A PixelEngine project already exists at this path.";
                return false;
            }

            if (Directory.Exists(projectPath))
            {
                using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(projectPath).GetEnumerator();
                if (entries.MoveNext())
                {
                    diagnostic = "The destination folder is not empty.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or PathTooLongException)
        {
            diagnostic = $"Project path is invalid: {exception.Message}";
            projectPath = string.Empty;
            return false;
        }
    }

    private void Navigate(ProjectPickerMode mode)
    {
        Mode = mode;
        _selectModeOnNextDraw = true;
        FolderPickerDiagnostic = string.Empty;
    }
}

/// <summary>
/// 项目选择器模式：最近工程、新建或从磁盘打开。
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
