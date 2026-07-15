using System.Numerics;
using System.Globalization;
using Hexa.NET.ImGui;
using L = PixelEngine.Editor.EditorLocalization;
using HostingEditorMode = PixelEngine.Hosting.EditorMode;

namespace PixelEngine.Editor.Shell;

internal readonly record struct EditorConsoleOptionsSnapshot(
    string Search,
    bool Collapse,
    bool ClearOnPlay,
    bool ErrorPause,
    bool ShowLogs,
    bool ShowWarnings,
    bool ShowErrors,
    bool AutoScroll)
{
    internal static EditorConsoleOptionsSnapshot Default { get; } = new(
        string.Empty,
        Collapse: false,
        ClearOnPlay: true,
        ErrorPause: false,
        ShowLogs: true,
        ShowWarnings: true,
        ShowErrors: true,
        AutoScroll: true);
}

internal sealed class EditorConsoleOptionsState
{
    private EditorConsoleOptionsSnapshot _current = EditorConsoleOptionsSnapshot.Default;

    internal event Action<EditorConsoleOptionsSnapshot>? Changed;

    internal EditorConsoleOptionsSnapshot Capture()
    {
        return _current;
    }

    internal bool Apply(EditorConsoleOptionsSnapshot value, bool notifyChanged = true)
    {
        if (_current == value)
        {
            return false;
        }

        _current = value;
        if (notifyChanged)
        {
            Changed?.Invoke(value);
        }

        return true;
    }
}

internal readonly record struct EditorConsoleSelectionSnapshot(
    long? Sequence,
    string? CollapseKey);

internal sealed class EditorConsoleSelectionState
{
    private EditorConsoleSelectionSnapshot _current;

    internal EditorConsoleSelectionSnapshot Capture()
    {
        return _current;
    }

    internal bool Select(in EditorConsoleRow row)
    {
        EditorConsoleSelectionSnapshot target = new(row.Sequence, row.Entry.CollapseKey);
        if (_current == target)
        {
            return false;
        }

        _current = target;
        return true;
    }

    internal bool Restore(EditorConsoleSelectionSnapshot state)
    {
        if (_current == state)
        {
            return false;
        }

        _current = state;
        return true;
    }

    internal bool Clear()
    {
        return Restore(default);
    }
}

internal readonly record struct EditorConsoleClearResult(
    bool EntriesChanged,
    bool SelectionChanged)
{
    internal bool StateChanged => EntriesChanged || SelectionChanged;
}

internal static class EditorConsoleActions
{
    internal static bool IsSeverityVisible(
        EditorConsoleSeverity severity,
        in EditorConsoleOptionsSnapshot options)
    {
        return severity switch
        {
            EditorConsoleSeverity.Trace or EditorConsoleSeverity.Info => options.ShowLogs,
            EditorConsoleSeverity.Warning => options.ShowWarnings,
            EditorConsoleSeverity.Error => options.ShowErrors,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "未知 Console 严重度。"),
        };
    }

    internal static string BuildClipboardText(in EditorConsoleEntry entry)
    {
        string location = string.IsNullOrWhiteSpace(entry.FilePath)
            ? string.Empty
            : FormatSourceLocation(entry);
        return string.Join(
            Environment.NewLine,
            new[] { entry.Text, entry.Details, location }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    internal static bool TryResolveScriptBrowserPath(
        EditorProject project,
        in EditorConsoleEntry entry,
        out string browserPath,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(project);
        browserPath = string.Empty;
        if (string.IsNullOrWhiteSpace(entry.FilePath))
        {
            diagnostic = "Console entry 不含源码位置。";
            return false;
        }

        try
        {
            string value = entry.FilePath.Replace('\\', '/');
            if (value.StartsWith("ScriptSource/", StringComparison.OrdinalIgnoreCase))
            {
                browserPath = value;
            }
            else if (Path.IsPathRooted(entry.FilePath))
            {
                string fullPath = Path.GetFullPath(entry.FilePath);
                string root = Path.GetFullPath(project.ScriptSourcePath);
                string rootPrefix = Path.EndsInDirectorySeparator(root)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic = "Console 源码路径不在当前工程 ScriptSource 根内。";
                    return false;
                }

                browserPath = "ScriptSource/" + Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            }
            else
            {
                browserPath = "ScriptSource/" + value.TrimStart('/');
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostic = $"Console 源码路径无效：{exception.Message}";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private static string FormatSourceLocation(in EditorConsoleEntry entry)
    {
        return entry.Line <= 0
            ? entry.FilePath ?? string.Empty
            : entry.Column > 0
                ? $"{entry.FilePath}:{entry.Line}:{entry.Column}"
                : $"{entry.FilePath}:{entry.Line}";
    }
}

/// <summary>
/// Unity-like Console：严重度计数过滤、Collapse、Play 联动、可选日志行与完整详情。
/// </summary>
internal sealed class EditorConsolePanel : IEditorPanel
{
    private static readonly Vector4 LogColor = new(0.78f, 0.80f, 0.84f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.72f, 0.25f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.34f, 0.34f, 1f);
    private readonly EditorShellApp _app;
    private readonly EditorConsolePlayState _playState;
    private long _lastRenderedSequence = -1;

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
        string windowTitle = L.GetWindowTitle("window.console", "Console", Title);
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
        EditorConsoleOptionsSnapshot options = _app.ConsoleOptions.Capture();
        HostingEditorMode mode = _app.CurrentSession?.CaptureEditorPlaySession().Mode ?? HostingEditorMode.Edit;
        if (_playState.ObserveMode(mode, options.ClearOnPlay))
        {
            _ = _app.ClearConsole(notifyAutomation: true);
        }

        long runtimeErrorSequence = _app.ConsoleStore.LastRuntimeErrorSequence;
        if (_playState.ObserveRuntimeError(runtimeErrorSequence, options.ErrorPause, mode))
        {
            _app.TogglePauseMode();
        }
    }

    private void DrawToolbar()
    {
        EditorConsoleOptionsSnapshot before = _app.ConsoleOptions.Capture();
        string search = before.Search;
        bool collapse = before.Collapse;
        bool clearOnPlay = before.ClearOnPlay;
        bool errorPause = before.ErrorPause;
        bool showLogs = before.ShowLogs;
        bool showWarnings = before.ShowWarnings;
        bool showErrors = before.ShowErrors;
        bool autoScroll = before.AutoScroll;
        bool expandedControls = ImGui.GetContentRegionAvail().X >= 620f;
        if (ImGui.Button(L.Get("console.clear", "Clear")))
        {
            _ = _app.ClearConsole(notifyAutomation: true);
        }

        ImGui.SameLine();
        DrawToolbarToggle(
            expandedControls ? $"{L.Get("console.collapse", "Collapse")}##console-collapse" : "C##console-collapse",
            L.Get("console.collapse", "Collapse"),
            ref collapse);
        if (expandedControls)
        {
            ImGui.SameLine();
            DrawToolbarToggle(
                $"{L.Get("console.clearOnPlay", "Clear on Play")}##console-clear-on-play",
                L.Get("console.clearOnPlay", "Clear on Play"),
                ref clearOnPlay);
            ImGui.SameLine();
            DrawToolbarToggle(
                $"{L.Get("console.errorPause", "Error Pause")}##console-error-pause",
                L.Get("console.errorPause", "Error Pause"),
                ref errorPause);
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
                _ = ImGui.MenuItem(L.Get("console.clearOnPlay", "Clear on Play"), string.Empty, ref clearOnPlay);
                _ = ImGui.MenuItem(L.Get("console.errorPause", "Error Pause"), string.Empty, ref errorPause);
                ImGui.Separator();
            }

            _ = ImGui.MenuItem(L.Get("console.autoScroll", "Auto scroll"), string.Empty, ref autoScroll);
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
            ref search,
            256);

        ImGui.SameLine();
        DrawSeverityToggle(
            L.Get("console.log", "Log"),
            counts.Logs,
            ref showLogs,
            LogColor);
        ImGui.SameLine();
        DrawSeverityToggle(
            L.Get("console.warning", "Warning"),
            counts.Warnings,
            ref showWarnings,
            WarningColor);
        ImGui.SameLine();
        DrawSeverityToggle(
            L.Get("console.error", "Error"),
            counts.Errors,
            ref showErrors,
            ErrorColor);

        _ = _app.ConsoleOptions.Apply(new EditorConsoleOptionsSnapshot(
            search,
            collapse,
            clearOnPlay,
            errorPause,
            showLogs,
            showWarnings,
            showErrors,
            autoScroll));
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
        EditorConsoleOptionsSnapshot options = _app.ConsoleOptions.Capture();
        EditorConsoleRow[] rows = _app.ConsoleStore.SnapshotRows(
            new EditorConsoleFilter
            {
                SearchContains = string.IsNullOrWhiteSpace(options.Search) ? null : options.Search,
            },
            newestFirst: false,
            collapse: options.Collapse);
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
                if (!EditorConsoleActions.IsSeverityVisible(row.Entry.Severity, options))
                {
                    continue;
                }

                DrawEntryRow(row);
                EditorConsoleSelectionSnapshot selection = _app.ConsoleSelection.Capture();
                if (RowMatchesSelection(row, options.Collapse, selection.Sequence, selection.CollapseKey))
                {
                    selectedRow = row;
                }
            }

            long latestSequence = _app.ConsoleStore.LastSequence;
            if (options.AutoScroll && wasAtBottom && latestSequence > _lastRenderedSequence)
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
        EditorConsoleOptionsSnapshot options = _app.ConsoleOptions.Capture();
        EditorConsoleEntry entry = row.Entry;
        Vector4 color = GetSeverityColor(entry.Severity);
        Vector2 iconCenter = ImGui.GetCursorScreenPos() + new Vector2(7f, ImGui.GetTextLineHeight() * 0.5f);
        ImGui.GetWindowDrawList().AddCircleFilled(iconCenter, 4f, ToAbgr(color));
        ImGui.Dummy(new Vector2(14f, ImGui.GetTextLineHeight()));
        ImGui.SameLine();
        string repeat = row.RepeatCount > 1 ? $"  ×{row.RepeatCount.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        string label = $"{entry.Text}{repeat}##console-{row.Sequence}";
        EditorConsoleSelectionSnapshot selection = _app.ConsoleSelection.Capture();
        bool selected = RowMatchesSelection(row, options.Collapse, selection.Sequence, selection.CollapseKey);
        if (ImGui.Selectable(label, selected, ImGuiSelectableFlags.SpanAllColumns))
        {
            if (_app.ConsoleSelection.Select(row))
            {
                _app.NotifyAutomationConsoleSelectionChanged();
            }
        }

        bool hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _ = _app.TryOpenConsoleSource(entry, out _);
        }

        if (ImGui.BeginPopupContextItem($"console-context-{row.Sequence}"))
        {
            if (ImGui.MenuItem(L.Get("console.copy", "Copy")))
            {
                ImGui.SetClipboardText(EditorConsoleActions.BuildClipboardText(entry));
            }

            if (ImGui.MenuItem(
                L.Get("console.openSource", "Open Source"),
                string.Empty,
                selected: false,
                enabled: !string.IsNullOrWhiteSpace(entry.FilePath)))
            {
                _ = _app.TryOpenConsoleSource(entry, out _);
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
                _ = _app.TryOpenConsoleSource(entry, out _);
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            ImGui.Separator();
            ImGui.TextWrapped(entry.Details);
        }
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
