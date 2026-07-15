using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 资产写入的同卷 payload 移动与磁盘/Editor before-image。Undo/Redo 只在 Editor 主线程执行。
/// </summary>
internal sealed class EditorAutomationAssetUndoAction(
    string name,
    EditorProjectSession session,
    EditorAssetBrowserDataSource assets,
    string beforePayloadPath,
    string afterPayloadPath,
    string? cleanupPath,
    bool directory,
    bool movesPayload,
    EditorAssetAutomationFileJournal fileJournal,
    EditorAutomationTransactionState beforeState,
    EditorAutomationTransactionState afterState,
    EditorAssetAutomationBrowserSnapshot? beforeBrowser = null,
    EditorAssetAutomationBrowserSnapshot? afterBrowser = null,
    EngineSceneDocument? beforeScene = null,
    EngineSceneDocument? afterScene = null,
    EditorProjectAutomationSnapshot? beforeProject = null,
    EditorProjectAutomationSnapshot? afterProject = null) :
    Automation.Server.IAutomationUndoAction,
    IDisposable
{
    private readonly EditorProjectSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly EditorAssetBrowserDataSource _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly string _beforePayloadPath = Path.GetFullPath(beforePayloadPath);
    private readonly string _afterPayloadPath = Path.GetFullPath(afterPayloadPath);
    private readonly string? _cleanupPath = cleanupPath is null ? null : Path.GetFullPath(cleanupPath);
    private readonly bool _directory = directory;
    private readonly bool _movesPayload = movesPayload;
    private readonly EditorAssetAutomationFileJournal _fileJournal = fileJournal ??
        throw new ArgumentNullException(nameof(fileJournal));
    private readonly EditorAutomationTransactionState _beforeState = beforeState ??
        throw new ArgumentNullException(nameof(beforeState));
    private readonly EditorAutomationTransactionState _afterState = afterState ??
        throw new ArgumentNullException(nameof(afterState));
    private readonly EditorAssetAutomationBrowserSnapshot? _beforeBrowser = beforeBrowser;
    private readonly EditorAssetAutomationBrowserSnapshot? _afterBrowser = afterBrowser;
    private readonly EngineSceneDocument? _beforeScene = beforeScene;
    private readonly EngineSceneDocument? _afterScene = afterScene;
    private readonly EditorProjectAutomationSnapshot? _beforeProject = beforeProject;
    private readonly EditorProjectAutomationSnapshot? _afterProject = afterProject;
    private bool _isBefore;
    private int _disposed;

    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Asset Undo name 不能为空。", nameof(name))
        : name;

    public void Undo()
    {
        Apply(before: true);
    }

    public void Redo()
    {
        Apply(before: false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? failures = null;
        try
        {
            _fileJournal.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (_cleanupPath is not null)
        {
            try
            {
                if (Directory.Exists(_cleanupPath))
                {
                    Directory.Delete(_cleanupPath, recursive: true);
                }
                else if (File.Exists(_cleanupPath))
                {
                    File.Delete(_cleanupPath);
                }

                string? parent = Path.GetDirectoryName(_cleanupPath);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
                    !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            _session.ReportAutomationCleanupFailure(string.Join(
                Environment.NewLine,
                failures.Select(exception =>
                    $"清理资产 Undo journal 失败，已保留供人工恢复。{exception.Message}")));
        }
    }

    private void Apply(bool before)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_isBefore == before)
        {
            return;
        }

        string sourcePath = before ? _afterPayloadPath : _beforePayloadPath;
        string targetPath = before ? _beforePayloadPath : _afterPayloadPath;
        EditorAutomationTransactionState targetState = before ? _beforeState : _afterState;
        EditorAutomationTransactionState sourceState = before ? _afterState : _beforeState;
        EditorAssetAutomationBrowserSnapshot? targetBrowser = before ? _beforeBrowser : _afterBrowser;
        EditorAssetAutomationBrowserSnapshot? sourceBrowser = before ? _afterBrowser : _beforeBrowser;
        EngineSceneDocument? targetScene = before ? _beforeScene : _afterScene;
        EngineSceneDocument? sourceScene = before ? _afterScene : _beforeScene;
        EditorProjectAutomationSnapshot? targetProject = before ? _beforeProject : _afterProject;
        EditorProjectAutomationSnapshot? sourceProject = before ? _afterProject : _beforeProject;
        if (_movesPayload)
        {
            MovePayload(sourcePath, targetPath);
        }

        try
        {
            if (before)
            {
                _fileJournal.ApplyBefore();
            }
            else
            {
                _fileJournal.ApplyAfter();
            }

            if (targetBrowser is not null)
            {
                _assets.RestoreAutomationBrowserSnapshot(targetBrowser, sourceBrowser);
                _session.ReloadAutomationAssetBrowserSnapshot();
            }

            if (targetProject is not null)
            {
                _session.Project.RestoreAutomationSnapshot(targetProject);
            }

            if (targetScene is not null)
            {
                _session.SceneModel.ReplaceWith(
                    EditorSceneModel.FromDocument(targetScene),
                    targetState.SceneWasDirty);
            }

            _session.RestoreAutomationTransactionState(targetState);
            _isBefore = before;
        }
        catch (Exception operationException)
        {
            List<Exception> failures = [operationException];
            try
            {
                if (before)
                {
                    _fileJournal.ApplyAfter();
                }
                else
                {
                    _fileJournal.ApplyBefore();
                }
            }
            catch (Exception rollbackException)
            {
                failures.Add(rollbackException);
            }

            if (_movesPayload)
            {
                try
                {
                    MovePayload(targetPath, sourcePath);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }
            }

            try
            {
                if (sourceBrowser is not null)
                {
                    _assets.RestoreAutomationBrowserSnapshot(sourceBrowser, targetBrowser);
                    _session.ReloadAutomationAssetBrowserSnapshot();
                }

                if (sourceProject is not null)
                {
                    _session.Project.RestoreAutomationSnapshot(sourceProject);
                }

                if (sourceScene is not null)
                {
                    _session.SceneModel.ReplaceWith(
                        EditorSceneModel.FromDocument(sourceScene),
                        sourceState.SceneWasDirty);
                }

                _session.RestoreAutomationTransactionState(sourceState);
            }
            catch (Exception rollbackException)
            {
                failures.Add(rollbackException);
            }

            AggregateException failure = new(
                $"资产 Undo/Redo '{Name}' 失败；已尝试恢复原方向。",
                failures);
            try
            {
                _session.ReportAutomationCleanupFailure(failure.ToString());
            }
            catch (Exception diagnosticException)
            {
                failures.Add(diagnosticException);
                failure = new AggregateException(
                    $"资产 Undo/Redo '{Name}' 失败，且 Console 诊断写入失败；已尝试恢复原方向。",
                    failures);
            }

            throw failure;
        }
    }

    private void MovePayload(string sourcePath, string targetPath)
    {
        bool sourceExists = _directory ? Directory.Exists(sourcePath) : File.Exists(sourcePath);
        bool targetExists = _directory ? Directory.Exists(targetPath) : File.Exists(targetPath);
        if (!sourceExists || targetExists)
        {
            throw new IOException(
                $"资产 Undo payload 状态无效：sourceExists={sourceExists}, targetExists={targetExists}, " +
                $"source={sourcePath}, target={targetPath}。");
        }

        string? parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parent))
        {
            _ = Directory.CreateDirectory(parent);
        }

        if (_directory)
        {
            Directory.Move(sourcePath, targetPath);
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }
    }
}
