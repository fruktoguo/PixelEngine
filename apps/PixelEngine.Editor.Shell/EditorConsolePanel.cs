using System.Numerics;
using System.Globalization;
using Hexa.NET.ImGui;
using L = PixelEngine.Editor.EditorLocalization;
using HostingEditorMode = PixelEngine.Hosting.EditorMode;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Unity-like Console：严重度计数过滤、Collapse、Play 联动、可选日志行与完整详情。
/// </summary>
internal sealed class EditorConsolePanel : IEditorPanel
{
    private static readonly Vector4 LogColor = new(0.78f, 0.80f, 0.84f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.72f, 0.25f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.34f, 0.34f, 1f);
    private readonly EditorShellApp _app;
    private string _search = string.Empty;
    private bool _collapse;
    private bool _clearOnPlay = true;
    private bool _errorPause;
    private bool _showLogs = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private bool _autoScroll = true;
    private readonly EditorConsolePlayState _playState;
    private long _lastRenderedSequence = -1;
    private long? _selectedSequence;
    private string? _selectedCollapseKey;

    public EditorConsolePanel(EditorShellApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _playState = new EditorConsolePlayState(_app.ConsoleStore.LastRuntimeErrorSequence);
        _ = _playState.ObserveMode(HostingEditorMode.Edit, clearOnPlay: false);
    }

    public string Title => EditorDockSpace.ConsoleDiagnosticsWindowTitle;

    public bool Visible { get; set; } = true;

    /// <summary>
    /// 推进与面板可见性无关的 Console Play 联动状态。
    /// </summary>
    /// <remarks>
    /// EditorApp 不会 Draw 已关闭面板；Clear on Play 与 Error Pause 不能因此停摆。
    /// </remarks>
    internal void PrepareFrame()
    {
        ObservePlayTransitions();
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        ObservePlayTransitions();
        string windowTitle = $"{L.Get("window.console", "Console")}###{Title}";
        if (!ImGui.Begin(windowTitle))
        {
            ImGui.End();
            return;
        }

        DrawToolbar();
        ImGui.Separator();
        DrawEntries();
        ImGui.End();
    }

    private void ObservePlayTransitions()
    {
        HostingEditorMode mode = _app.CurrentSession?.CaptureEditorPlaySession().Mode ?? HostingEditorMode.Edit;
        if (_playState.ObserveMode(mode, _clearOnPlay))
        {
            _app.ConsoleStore.Clear();
            _selectedSequence = null;
            _selectedCollapseKey = null;
        }

        long runtimeErrorSequence = _app.ConsoleStore.LastRuntimeErrorSequence;
        if (_playState.ObserveRuntimeError(runtimeErrorSequence, _errorPause, mode))
        {
            _app.TogglePauseMode();
        }
    }

    private void DrawToolbar()
    {
        bool expandedControls = ImGui.GetContentRegionAvail().X >= 620f;
        if (ImGui.Button(L.Get("console.clear", "Clear")))
        {
            _app.ConsoleStore.Clear();
            _selectedSequence = null;
            _selectedCollapseKey = null;
        }

        ImGui.SameLine();
        DrawToolbarToggle(
            expandedControls ? $"{L.Get("console.collapse", "Collapse")}##console-collapse" : "C##console-collapse",
            L.Get("console.collapse", "Collapse"),
            ref _collapse);
        if (expandedControls)
        {
            ImGui.SameLine();
            DrawToolbarToggle(
                $"{L.Get("console.clearOnPlay", "Clear on Play")}##console-clear-on-play",
                L.Get("console.clearOnPlay", "Clear on Play"),
                ref _clearOnPlay);
            ImGui.SameLine();
            DrawToolbarToggle(
                $"{L.Get("console.errorPause", "Error Pause")}##console-error-pause",
                L.Get("console.errorPause", "Error Pause"),
                ref _errorPause);
        }

        ImGui.SameLine();
        if (ImGui.Button("...##console-options"))
        {
            ImGui.OpenPopup("console-options-menu");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Console options");
        }

        if (ImGui.BeginPopup("console-options-menu"))
        {
            if (!expandedControls)
            {
                _ = ImGui.MenuItem(L.Get("console.clearOnPlay", "Clear on Play"), string.Empty, ref _clearOnPlay);
                _ = ImGui.MenuItem(L.Get("console.errorPause", "Error Pause"), string.Empty, ref _errorPause);
                ImGui.Separator();
            }

            _ = ImGui.MenuItem(L.Get("console.autoScroll", "Auto scroll"), string.Empty, ref _autoScroll);
            ImGui.EndPopup();
        }

        EditorConsoleCounts counts = _app.ConsoleStore.CaptureCounts();
        const float SeverityControlsWidth = 114f;
        ImGui.SameLine();
        float remaining = ImGui.GetContentRegionAvail().X;
        float searchWidth = Math.Clamp(remaining - SeverityControlsWidth, 56f, 320f);
        float leadingSpace = MathF.Max(0f, remaining - SeverityControlsWidth - searchWidth);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + leadingSpace);
        ImGui.SetNextItemWidth(searchWidth);
        _ = ImGui.InputTextWithHint(
            "##console-search",
            L.Get("console.searchHint", "Search"),
            ref _search,
            256);

        ImGui.SameLine();
        DrawSeverityToggle(
            L.Get("console.log", "Log"),
            counts.Logs,
            ref _showLogs,
            LogColor);
        ImGui.SameLine();
        DrawSeverityToggle(
            L.Get("console.warning", "Warning"),
            counts.Warnings,
            ref _showWarnings,
            WarningColor);
        ImGui.SameLine();
        DrawSeverityToggle(
            L.Get("console.error", "Error"),
            counts.Errors,
            ref _showErrors,
            ErrorColor);
    }

    private static void DrawToolbarToggle(string label, string tooltip, ref bool value)
    {
        if (value)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.24f, 0.42f, 0.60f, 1f));
        }

        if (ImGui.Button(label))
        {
            value = !value;
        }

        if (value)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static void DrawSeverityToggle(string tooltip, int count, ref bool value, Vector4 color)
    {
        if (value)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X * 0.28f, color.Y * 0.28f, color.Z * 0.28f, 1f));
        }

        ImGui.PushStyleColor(ImGuiCol.Text, value ? color : new Vector4(color.X, color.Y, color.Z, 0.38f));
        if (ImGui.Button($"{count.ToString(CultureInfo.InvariantCulture)}##console-{tooltip}"))
        {
            value = !value;
        }

        ImGui.PopStyleColor();
        if (value)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{tooltip}: {count.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private void DrawEntries()
    {
        EditorConsoleRow[] rows = _app.ConsoleStore.SnapshotRows(
            new EditorConsoleFilter
            {
                SearchContains = string.IsNullOrWhiteSpace(_search) ? null : _search,
            },
            newestFirst: false,
            collapse: _collapse);
        float availableHeight = ImGui.GetContentRegionAvail().Y;
        float listHeight = Math.Max(80f, availableHeight * 0.68f);
        bool listVisible = ImGui.BeginChild(
            "editor_console_rows",
            new Vector2(0f, listHeight),
            ImGuiChildFlags.Borders);
        bool wasAtBottom = ImGui.GetScrollMaxY() - ImGui.GetScrollY() <= 4f;
        EditorConsoleRow? selectedRow = null;
        if (listVisible)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                EditorConsoleRow row = rows[i];
                if (!IsSeverityVisible(row.Entry.Severity))
                {
                    continue;
                }

                DrawEntryRow(row);
                if (RowMatchesSelection(row, _collapse, _selectedSequence, _selectedCollapseKey))
                {
                    selectedRow = row;
                }
            }

            long latestSequence = _app.ConsoleStore.LastSequence;
            if (_autoScroll && wasAtBottom && latestSequence > _lastRenderedSequence)
            {
                ImGui.SetScrollHereY(1f);
            }

            _lastRenderedSequence = latestSequence;
        }

        if (rows.Length == 0)
        {
            ImGui.TextDisabled(L.Get("console.empty", "No console messages"));
        }

        ImGui.EndChild();
        _ = ImGui.BeginChild("editor_console_details", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
        DrawSelectedDetails(selectedRow);
        ImGui.EndChild();
    }

    private void DrawEntryRow(EditorConsoleRow row)
    {
        EditorConsoleEntry entry = row.Entry;
        Vector4 color = GetSeverityColor(entry.Severity);
        Vector2 iconCenter = ImGui.GetCursorScreenPos() + new Vector2(7f, ImGui.GetTextLineHeight() * 0.5f);
        ImGui.GetWindowDrawList().AddCircleFilled(iconCenter, 4f, ToAbgr(color));
        ImGui.Dummy(new Vector2(14f, ImGui.GetTextLineHeight()));
        ImGui.SameLine();
        string repeat = row.RepeatCount > 1 ? $"  ×{row.RepeatCount.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        string label = $"{entry.Text}{repeat}##console-{row.Sequence}";
        bool selected = RowMatchesSelection(row, _collapse, _selectedSequence, _selectedCollapseKey);
        if (ImGui.Selectable(label, selected, ImGuiSelectableFlags.SpanAllColumns))
        {
            _selectedSequence = row.Sequence;
            _selectedCollapseKey = row.Entry.CollapseKey;
        }

        bool hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _ = TryOpenSource(entry);
        }

        if (ImGui.BeginPopupContextItem($"console-context-{row.Sequence}"))
        {
            if (ImGui.MenuItem(L.Get("console.copy", "Copy")))
            {
                ImGui.SetClipboardText(BuildClipboardText(entry));
            }

            if (ImGui.MenuItem(
                L.Get("console.openSource", "Open Source"),
                string.Empty,
                selected: false,
                enabled: !string.IsNullOrWhiteSpace(entry.FilePath)))
            {
                _ = TryOpenSource(entry);
            }

            ImGui.EndPopup();
        }
    }

    internal static bool RowMatchesSelection(
        in EditorConsoleRow row,
        bool collapse,
        long? selectedSequence,
        string? selectedCollapseKey)
    {
        return collapse
            ? !string.IsNullOrEmpty(selectedCollapseKey) &&
              string.Equals(row.Entry.CollapseKey, selectedCollapseKey, StringComparison.Ordinal)
            : selectedSequence == row.Sequence;
    }

    private void DrawSelectedDetails(EditorConsoleRow? selectedRow)
    {
        if (selectedRow is not { } selected)
        {
            ImGui.TextDisabled(L.Get("console.selectMessage", "Select a message to inspect its details."));
            return;
        }

        EditorConsoleEntry entry = selected.Entry;
        ImGui.PushStyleColor(ImGuiCol.Text, GetSeverityColor(entry.Severity));
        ImGui.TextWrapped(entry.Text);
        ImGui.PopStyleColor();
        ImGui.TextDisabled($"{entry.Category} · {entry.Source} · {entry.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}");
        if (!string.IsNullOrWhiteSpace(entry.FilePath))
        {
            string location = FormatSourceLocation(entry);
            if (ImGui.Selectable(location, selected: false))
            {
                _ = TryOpenSource(entry);
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            ImGui.Separator();
            ImGui.TextWrapped(entry.Details);
        }
    }

    private bool TryOpenSource(in EditorConsoleEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FilePath) || _app.CurrentProject is not { } project)
        {
            return false;
        }

        string value = entry.FilePath.Replace('\\', '/');
        string browserPath;
        if (value.StartsWith("ScriptSource/", StringComparison.OrdinalIgnoreCase))
        {
            browserPath = value;
        }
        else if (Path.IsPathRooted(entry.FilePath))
        {
            string fullPath = Path.GetFullPath(entry.FilePath);
            string root = Path.GetFullPath(project.ScriptSourcePath);
            string rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            browserPath = "ScriptSource/" + Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        }
        else
        {
            browserPath = "ScriptSource/" + value.TrimStart('/');
        }

        return _app.OpenScriptAsset(
            browserPath,
            Math.Max(1, entry.Line),
            Math.Max(1, entry.Column),
            out _);
    }

    private bool IsSeverityVisible(EditorConsoleSeverity severity)
    {
        return severity switch
        {
            EditorConsoleSeverity.Trace or EditorConsoleSeverity.Info => _showLogs,
            EditorConsoleSeverity.Warning => _showWarnings,
            EditorConsoleSeverity.Error => _showErrors,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "未知 Console 严重度。"),
        };
    }

    private static Vector4 GetSeverityColor(EditorConsoleSeverity severity)
    {
        return severity switch
        {
            EditorConsoleSeverity.Trace or EditorConsoleSeverity.Info => LogColor,
            EditorConsoleSeverity.Warning => WarningColor,
            EditorConsoleSeverity.Error => ErrorColor,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "未知 Console 严重度。"),
        };
    }

    private static string BuildClipboardText(in EditorConsoleEntry entry)
    {
        string location = string.IsNullOrWhiteSpace(entry.FilePath)
            ? string.Empty
            : FormatSourceLocation(entry);
        return string.Join(
            Environment.NewLine,
            new[] { entry.Text, entry.Details, location }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatSourceLocation(in EditorConsoleEntry entry)
    {
        return entry.Line <= 0
            ? entry.FilePath ?? string.Empty
            : entry.Column > 0
                ? $"{entry.FilePath}:{entry.Line}:{entry.Column}"
                : $"{entry.FilePath}:{entry.Line}";
    }

    private static uint ToAbgr(Vector4 color)
    {
        uint r = (uint)Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
        uint g = (uint)Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
        uint b = (uint)Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
        uint a = (uint)Math.Clamp((int)MathF.Round(color.W * 255f), 0, 255);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
}
