using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 把 Silk 平台 file-drop 路由到上一完整帧命中的 Project folder，并把批量结果写入 Console。
/// </summary>
internal sealed class EditorExternalAssetDropConnector : IDisposable
{
    private readonly RenderWindow _window;
    private readonly AssetBrowserPanel _project;
    private readonly EditorConsoleStore _console;
    private bool _disposed;

    public EditorExternalAssetDropConnector(
        RenderWindow window,
        AssetBrowserPanel project,
        EditorConsoleStore console)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _window.FilesDropped += OnFilesDropped;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _window.FilesDropped -= OnFilesDropped;
        _disposed = true;
    }

    internal static Vector2 ToFramebufferPoint(Vector2 logicalPoint, float scaleX, float scaleY)
    {
        return new Vector2(logicalPoint.X * scaleX, logicalPoint.Y * scaleY);
    }

    private void OnFilesDropped(string[] paths)
    {
        if (_disposed)
        {
            return;
        }

        if (_window.Input.Mice.Count == 0)
        {
            Reject("外部拖入未执行：窗口没有可用鼠标，无法确定 Project 目标。");
            return;
        }

        Vector2 point = ToFramebufferPoint(
            _window.Input.Mice[0].Position,
            _window.FramebufferScaleX,
            _window.FramebufferScaleY);
        if (!_project.TryResolveExternalDropTarget(point, out string folderPath))
        {
            Reject("外部拖入未执行：请把文件或目录放到当前可见的 Project 窗口或目标文件夹。");
            return;
        }

        AssetBrowserExternalImportResult result = _project.ImportExternalPaths(paths, folderPath);
        EditorConsoleSeverity severity = result.RejectedFileCount == 0 && result.ImportedFileCount > 0
            ? EditorConsoleSeverity.Info
            : EditorConsoleSeverity.Warning;
        _console.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            severity,
            "project-file-drop",
            result.Diagnostic));
    }

    private void Reject(string diagnostic)
    {
        _project.ReportExternalDropDiagnostic(diagnostic);
        _console.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            EditorConsoleSeverity.Warning,
            "project-file-drop",
            diagnostic));
    }
}
