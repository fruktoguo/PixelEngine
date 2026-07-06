using PixelEngine.Scripting;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorConsoleScriptHotReloadDiagnosticSink : IScriptHotReloadDiagnosticSink
{
    private const string Source = "script-hot-reload";
    private readonly IEditorConsoleSink _console;

    public EditorConsoleScriptHotReloadDiagnosticSink(IEditorConsoleSink console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void Report(ScriptHotReloadDiagnostic diagnostic)
    {
        string message = string.IsNullOrWhiteSpace(diagnostic.Message) ? "脚本热重载诊断" : diagnostic.Message;
        switch (diagnostic.Kind)
        {
            case ScriptHotReloadDiagnosticKind.WatcherStarted:
                _console.Add(new EditorConsoleEntry(
                    NormalizeTimestamp(diagnostic.Timestamp),
                    EditorConsoleCategory.Script,
                    EditorConsoleSeverity.Info,
                    Source,
                    message));
                break;
            case ScriptHotReloadDiagnosticKind.WatcherStartFailed:
            case ScriptHotReloadDiagnosticKind.WatcherException:
                _console.Add(new EditorConsoleEntry(
                    NormalizeTimestamp(diagnostic.Timestamp),
                    EditorConsoleCategory.Script,
                    EditorConsoleSeverity.Error,
                    Source,
                    message));
                AddDetails(diagnostic.Diagnostics);
                break;
            case ScriptHotReloadDiagnosticKind.ReloadResult:
                if (diagnostic.Status == ScriptHotReloadStatus.NoPendingReload)
                {
                    return;
                }

                _console.AddScriptDiagnostics(
                    Source,
                    message,
                    diagnostic.Diagnostics,
                    success: diagnostic.Status == ScriptHotReloadStatus.Reloaded);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(diagnostic), diagnostic.Kind, "未知脚本热重载诊断类别。");
        }
    }

    private void AddDetails(IReadOnlyList<string> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            _console.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Script,
                EditorConsoleSeverity.Error,
                Source,
                diagnostics[i]));
        }
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp)
    {
        return timestamp == default ? DateTimeOffset.UtcNow : timestamp;
    }
}
