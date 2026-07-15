using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal sealed record EditorAssetAutomationMutationRequest(
    string Method,
    JsonElement? Payload,
    EditorAssetAutomationBrowserSnapshot? FrozenBrowser = null);

internal sealed class EditorAssetAutomationPreparedMutation
{
    private readonly EditorAssetAutomationPreparationWorkspace _workspace;
    private readonly EditorAssetAutomationFileJournal? _fileJournal;
    private int _stagingOwned = 1;

    internal EditorAssetAutomationPreparedMutation(
        EditorAssetAutomationPreparationWorkspace workspace,
        int sequence,
        string method,
        string name,
        bool stateChanged,
        bool succeeded,
        bool requiresConfirmation,
        string diagnostic,
        object? semanticResult,
        AssetBrowserItem? asset,
        string[] affectedAssetIds,
        string[] affectedFolderPaths,
        string beforePayloadPath,
        string afterPayloadPath,
        string? cleanupPath,
        bool directory,
        bool movesPayload,
        EditorAssetAutomationFileJournal? fileJournal,
        EditorAssetAutomationBrowserSnapshot beforeBrowser,
        EditorAssetAutomationBrowserSnapshot afterBrowser,
        EngineSceneDocument beforeScene,
        EngineSceneDocument afterScene,
        bool sceneChanged,
        bool afterSceneDirty,
        EditorProjectAutomationSnapshot beforeProject,
        EditorProjectAutomationSnapshot afterProject,
        bool projectChanged)
    {
        _workspace = workspace;
        Sequence = sequence;
        Method = method;
        Name = name;
        StateChanged = stateChanged;
        Succeeded = succeeded;
        RequiresConfirmation = requiresConfirmation;
        Diagnostic = diagnostic;
        SemanticResult = semanticResult;
        Asset = asset;
        AffectedAssetIds = affectedAssetIds;
        AffectedFolderPaths = affectedFolderPaths;
        BeforePayloadPath = beforePayloadPath;
        AfterPayloadPath = afterPayloadPath;
        CleanupPath = cleanupPath;
        IsDirectory = directory;
        MovesPayload = movesPayload;
        _fileJournal = fileJournal;
        BeforeBrowser = beforeBrowser;
        AfterBrowser = afterBrowser;
        BeforeScene = beforeScene;
        AfterScene = afterScene;
        SceneChanged = sceneChanged;
        AfterSceneDirty = afterSceneDirty;
        BeforeProject = beforeProject;
        AfterProject = afterProject;
        ProjectChanged = projectChanged;
    }

    internal int Sequence { get; }

    internal string Method { get; }

    internal string Name { get; }

    internal bool StateChanged { get; }

    internal bool Succeeded { get; }

    internal bool RequiresConfirmation { get; }

    internal string Diagnostic { get; }

    internal object? SemanticResult { get; }

    internal AssetBrowserItem? Asset { get; }

    internal string[] AffectedAssetIds { get; }

    internal string[] AffectedFolderPaths { get; }

    internal string BeforePayloadPath { get; }

    internal string AfterPayloadPath { get; }

    internal string? CleanupPath { get; }

    internal bool IsDirectory { get; }

    internal bool MovesPayload { get; }

    internal EditorAssetAutomationBrowserSnapshot BeforeBrowser { get; }

    internal EditorAssetAutomationBrowserSnapshot AfterBrowser { get; }

    internal EngineSceneDocument BeforeScene { get; }

    internal EngineSceneDocument AfterScene { get; }

    internal bool SceneChanged { get; }

    internal bool AfterSceneDirty { get; }

    internal EditorProjectAutomationSnapshot BeforeProject { get; }

    internal EditorProjectAutomationSnapshot AfterProject { get; }

    internal bool ProjectChanged { get; }

    internal void CommitNoChange(EditorAssetBrowserDataSource assets)
    {
        if (StateChanged)
        {
            throw new InvalidOperationException("State-changing asset preparation 不能按 no-change 提交。");
        }

        ArgumentNullException.ThrowIfNull(assets);
        _workspace.ValidateCommitSequence(
            Sequence,
            assets.CaptureAutomationBrowserSnapshot(),
            BeforeBrowser);
        _workspace.CommitSequence(Sequence, BeforeBrowser);
        _ = Interlocked.Exchange(ref _stagingOwned, 0);
    }

    internal EditorAutomationAssetUndoAction Apply(
        EditorProjectSession session,
        EditorAssetBrowserDataSource assets)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(assets);
        if (!string.Equals(
            Path.GetFullPath(session.Project.ProjectRoot),
            _workspace.ProjectRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Prepared asset mutation 的 project session 已被替换，拒绝提交旧 workspace。");
        }

        if (!StateChanged)
        {
            throw new InvalidOperationException("No-change asset preparation 不得发布文件变化。");
        }

        _workspace.ValidateCommitSequence(
            Sequence,
            assets.CaptureAutomationBrowserSnapshot(),
            BeforeBrowser);
        EditorAutomationTransactionState beforeState = session.CaptureAutomationTransactionState();
        EditorAssetAutomationFileJournal fileJournal = _fileJournal ??
            throw new InvalidOperationException("State-changing asset preparation 缺少 staged file journal。");
        if (MovesPayload)
        {
            MovePayload(BeforePayloadPath, AfterPayloadPath, IsDirectory);
        }

        try
        {
            fileJournal.ApplyAfter();
            assets.RestoreAutomationBrowserSnapshot(AfterBrowser, BeforeBrowser);
            session.ReloadAutomationAssetBrowserSnapshot();
            if (ProjectChanged)
            {
                session.Project.RestoreAutomationSnapshot(AfterProject);
            }

            if (SceneChanged)
            {
                session.SceneModel.ReplaceWith(
                    EditorSceneModel.FromDocument(AfterScene),
                    AfterSceneDirty);
            }

            EditorAutomationTransactionState afterState = session.CaptureAutomationTransactionState();
            EditorAutomationAssetUndoAction action = new(
                Name,
                session,
                assets,
                BeforePayloadPath,
                AfterPayloadPath,
                CleanupPath,
                IsDirectory,
                MovesPayload,
                fileJournal,
                beforeState,
                afterState,
                BeforeBrowser,
                AfterBrowser,
                SceneChanged ? BeforeScene : null,
                SceneChanged ? AfterScene : null,
                ProjectChanged ? BeforeProject : null,
                ProjectChanged ? AfterProject : null);
            _workspace.CommitSequence(Sequence, AfterBrowser);
            _ = Interlocked.Exchange(ref _stagingOwned, 0);
            return action;
        }
        catch (Exception operationException)
        {
            List<Exception> failures = [operationException];
            TryRollback(fileJournal.ApplyBefore, failures);
            if (MovesPayload)
            {
                TryRollback(
                    () => MovePayload(AfterPayloadPath, BeforePayloadPath, IsDirectory),
                    failures);
            }
            TryRollback(
                () => assets.RestoreAutomationBrowserSnapshot(BeforeBrowser, AfterBrowser),
                failures);
            TryRollback(session.ReloadAutomationAssetBrowserSnapshot, failures);
            if (ProjectChanged)
            {
                TryRollback(() => session.Project.RestoreAutomationSnapshot(BeforeProject), failures);
            }

            if (SceneChanged)
            {
                TryRollback(
                    () => session.SceneModel.ReplaceWith(
                        EditorSceneModel.FromDocument(BeforeScene),
                        beforeState.SceneWasDirty),
                    failures);
            }

            TryRollback(() => session.RestoreAutomationTransactionState(beforeState), failures);
            throw new AggregateException("Prepared asset mutation 发布失败；已尝试恢复 before-image。", failures);
        }
    }

    internal void DisposeUncommittedStaging()
    {
        if (Interlocked.Exchange(ref _stagingOwned, 0) == 0)
        {
            return;
        }

        List<Exception>? failures = null;
        try
        {
            _fileJournal?.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (CleanupPath is not null)
        {
            try
            {
                DeletePathIfExists(CleanupPath);
                DeleteEmptyParent(CleanupPath);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Prepared asset staging 清理失败。", failures);
        }
    }

    private static void TryRollback(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void MovePayload(string sourcePath, string targetPath, bool directory)
    {
        bool sourceExists = directory ? Directory.Exists(sourcePath) : File.Exists(sourcePath);
        bool targetExists = directory ? Directory.Exists(targetPath) : File.Exists(targetPath);
        if (!sourceExists || targetExists)
        {
            throw new IOException(
                $"Prepared asset payload 状态无效：sourceExists={sourceExists}, targetExists={targetExists}, " +
                $"source={sourcePath}, target={targetPath}。");
        }

        string? parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parent))
        {
            _ = Directory.CreateDirectory(parent);
        }

        if (directory)
        {
            Directory.Move(sourcePath, targetPath);
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }
    }

    private static void DeletePathIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteEmptyParent(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
            !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }
}

internal sealed class EditorAssetAutomationPreparationWorkspace : IDisposable
{
    private const int MaximumFiles = 8192;
    private const int MaximumDirectories = 8192;
    private const long MaximumBytes = 128L * 1024 * 1024;
    private readonly string _contentRoot;
    private readonly string _scriptRoot;
    private readonly string _currentScenePath;
    private readonly EngineSceneDocument _initialScene;
    private readonly string[] _importRoots;
    private readonly string _preparationRoot;
    private readonly string _sandboxRoot;
    private readonly HashSet<string> _copiedSources = new(StringComparer.OrdinalIgnoreCase);
    private EditorProject? _sandboxProject;
    private EditorSceneModel? _sandboxScene;
    private EditorAssetBrowserDataSource? _sandboxAssets;
    private EditorAssetAutomationBrowserSnapshot? _lastCommittedBrowser;
    private EditorAssetAutomationFileSnapshot? _initialRefreshFiles;
    private EditorProjectAutomationSnapshot? _initialRefreshProject;
    private int _initialRefreshSceneVersion;
    private long _copiedBytes;
    private long _preparedBytes;
    private int _nextPreparationSequence;
    private int _nextCommitSequence;
    private int _initialized;
    private int _disposed;

    internal string ProjectRoot { get; }

    private EditorAssetAutomationPreparationWorkspace(
        string projectRoot,
        string contentRoot,
        string scriptRoot,
        string currentScenePath,
        EngineSceneDocument initialScene,
        string[] importRoots)
    {
        ProjectRoot = projectRoot;
        _contentRoot = contentRoot;
        _scriptRoot = scriptRoot;
        _currentScenePath = currentScenePath;
        _initialScene = initialScene;
        _importRoots = importRoots;
        _preparationRoot = Path.Combine(projectRoot, ".pixelengine", "automation-preparation");
        _sandboxRoot = Path.Combine(_preparationRoot, Guid.NewGuid().ToString("N"));
    }

    internal static EditorAssetAutomationPreparationWorkspace Freeze(
        EditorProjectSession session,
        string[] importRoots)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Freeze(
            session.Project,
            session.AutomationActiveContentRoot,
            session.AutomationActiveScriptSourceDir,
            session.CurrentSceneRelativePath,
            session.SceneModel.ToDocument(),
            importRoots);
    }

    internal static EditorAssetAutomationPreparationWorkspace Freeze(
        EditorProject project,
        string activeContent,
        string activeScripts,
        string currentScenePath,
        EngineSceneDocument initialScene,
        string[] importRoots)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(initialScene);
        ArgumentNullException.ThrowIfNull(importRoots);
        string projectRoot = Path.GetFullPath(project.ProjectRoot);
        if (!string.Equals(activeContent, project.ContentRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(activeScripts, project.ScriptSourceDir, StringComparison.OrdinalIgnoreCase))
        {
            throw StateUnavailable(
                "Project Settings 已改变 Content/ScriptSource，必须 reload project 后再执行 asset mutation。");
        }

        string contentRoot = ResolveProjectChild(projectRoot, activeContent, "ContentRoot");
        string scriptRoot = ResolveProjectChild(projectRoot, activeScripts, "ScriptSourceDir");
        return new EditorAssetAutomationPreparationWorkspace(
            projectRoot,
            contentRoot,
            scriptRoot,
            currentScenePath,
            initialScene,
            [.. importRoots]);
    }

    internal async ValueTask<object?> PrepareAsync(
        EditorAssetAutomationMutationRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized(cancellationToken);
        await Task.Yield();
        return PrepareCore(request, cancellationToken);
    }

    internal async ValueTask<object?> PrepareRefreshAsync(
        EditorAssetAutomationMutationRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(
                request.Method,
                AutomationProtocolConstants.ProjectAssetRefreshMethod,
                StringComparison.Ordinal) ||
            request.FrozenBrowser is null)
        {
            throw new InvalidOperationException("Asset refresh preparation 缺少 method 或 frozen browser。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized(cancellationToken);
        await Task.Yield();
        return PrepareRefreshCore(request, cancellationToken);
    }

    internal void ValidateCommitSequence(
        int sequence,
        EditorAssetAutomationBrowserSnapshot currentBrowser,
        EditorAssetAutomationBrowserSnapshot expectedBefore)
    {
        if (sequence != _nextCommitSequence)
        {
            throw new InvalidOperationException(
                $"Prepared asset mutation 提交顺序无效：expected={_nextCommitSequence}, actual={sequence}。");
        }

        EditorAssetAutomationBrowserSnapshot expected = _lastCommittedBrowser ?? expectedBefore;
        if (_lastCommittedBrowser is not null &&
            (!expectedBefore.Assets.SequenceEqual(_lastCommittedBrowser.Assets) ||
             !expectedBefore.Folders.SequenceEqual(_lastCommittedBrowser.Folders)))
        {
            throw new InvalidOperationException(
                "Prepared asset mutation 的 sandbox operation 链与已提交 catalog 不一致。");
        }

        if (!expected.Assets.SequenceEqual(currentBrowser.Assets) ||
            !expected.Folders.SequenceEqual(currentBrowser.Folders))
        {
            throw new InvalidOperationException(
                "Prepared asset mutation 的 browser before-image 已失效。" +
                DescribeBrowserDifference(expected, currentBrowser));
        }
    }

    private static string DescribeBrowserDifference(
        EditorAssetAutomationBrowserSnapshot expected,
        EditorAssetAutomationBrowserSnapshot actual)
    {
        int assetCount = Math.Min(expected.Assets.Length, actual.Assets.Length);
        for (int i = 0; i < assetCount; i++)
        {
            if (expected.Assets[i] != actual.Assets[i])
            {
                return $" asset[{i}] expected={expected.Assets[i]}, actual={actual.Assets[i]}。";
            }
        }

        if (expected.Assets.Length != actual.Assets.Length)
        {
            return $" assetCount expected={expected.Assets.Length}, actual={actual.Assets.Length}。";
        }

        int folderCount = Math.Min(expected.Folders.Length, actual.Folders.Length);
        for (int i = 0; i < folderCount; i++)
        {
            if (expected.Folders[i] != actual.Folders[i])
            {
                return $" folder[{i}] expected={expected.Folders[i]}, actual={actual.Folders[i]}。";
            }
        }

        return $" folderCount expected={expected.Folders.Length}, actual={actual.Folders.Length}。";
    }

    internal void CommitSequence(int sequence, EditorAssetAutomationBrowserSnapshot browser)
    {
        if (sequence != _nextCommitSequence)
        {
            throw new InvalidOperationException("Prepared asset mutation commit sequence 发生竞态。");
        }

        _lastCommittedBrowser = browser;
        _nextCommitSequence++;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _sandboxAssets?.Dispose();
        if (Directory.Exists(_sandboxRoot))
        {
            Directory.Delete(_sandboxRoot, recursive: true);
        }

        if (Directory.Exists(_preparationRoot) &&
            !Directory.EnumerateFileSystemEntries(_preparationRoot).Any())
        {
            Directory.Delete(_preparationRoot);
        }
    }

    private EditorAssetAutomationPreparedMutation PrepareRefreshCore(
        EditorAssetAutomationMutationRequest request,
        CancellationToken cancellationToken)
    {
        EditorAssetBrowserDataSource assets = _sandboxAssets ??
            throw new InvalidOperationException("Asset refresh sandbox 尚未初始化。");
        EditorSceneModel scene = _sandboxScene ??
            throw new InvalidOperationException("Asset refresh scene 尚未初始化。");
        EditorProject project = _sandboxProject ??
            throw new InvalidOperationException("Asset refresh project 尚未初始化。");
        EditorAssetAutomationFileSnapshot rawBeforeFiles = _initialRefreshFiles ??
            throw new InvalidOperationException("Asset refresh 缺少初始化前文件快照。");
        EditorProjectAutomationSnapshot beforeProject = _initialRefreshProject ??
            throw new InvalidOperationException("Asset refresh 缺少初始化前 project 快照。");
        EditorAssetAutomationBrowserSnapshot beforeBrowser = request.FrozenBrowser ??
            throw new InvalidOperationException("Asset refresh 缺少 live browser before-image。");
        int sequence = _nextPreparationSequence++;
        cancellationToken.ThrowIfCancellationRequested();

        EditorAssetAutomationFileSnapshot rawAfterFiles = CaptureRefreshFiles(project, cancellationToken);
        PreparedMutationResult marker = PreparedMutationResult.NoChange(
            "Refresh Assets",
            string.Empty,
            asset: null,
            affectedAssetIds: [],
            affectedFolderPaths: []);
        (EditorAssetAutomationFileSnapshot beforeFiles, EditorAssetAutomationFileSnapshot afterFiles) =
            BuildChangedSnapshots(rawBeforeFiles, rawAfterFiles, marker);
        TrackPreparedBytes(beforeFiles, afterFiles);
        EditorAssetAutomationBrowserSnapshot afterBrowser = MapBrowserSnapshot(
            assets.CaptureAutomationBrowserSnapshot());
        EngineSceneDocument beforeScene = _initialScene;
        EngineSceneDocument afterScene = scene.ToDocument();
        EditorProjectAutomationSnapshot afterProject = project.CaptureAutomationSnapshot();
        bool sceneChanged = scene.Version != _initialRefreshSceneVersion;
        bool projectChanged = !ProjectSnapshotsEqual(beforeProject, afterProject);
        bool stateChanged = beforeFiles.Files.Length != 0 ||
            !BrowserSnapshotsEqual(beforeBrowser, afterBrowser) ||
            sceneChanged ||
            projectChanged;
        string diagnostic = string.Join(
            Environment.NewLine,
            new[] { afterBrowser.LastDiagnostic, afterBrowser.RuntimeDiagnostic }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
        AutomationAssetRefreshResult response = new()
        {
            AssetCount = afterBrowser.Assets.Length,
            FolderCount = afterBrowser.Folders.Length,
            StateChanged = stateChanged,
            Diagnostic = diagnostic,
        };
        if (!stateChanged)
        {
            return new EditorAssetAutomationPreparedMutation(
                this,
                sequence,
                request.Method,
                "Refresh Assets",
                stateChanged: false,
                succeeded: true,
                requiresConfirmation: false,
                diagnostic,
                response,
                asset: null,
                affectedAssetIds: [],
                affectedFolderPaths: [],
                beforePayloadPath: string.Empty,
                afterPayloadPath: string.Empty,
                cleanupPath: null,
                directory: false,
                movesPayload: false,
                fileJournal: null,
                beforeBrowser,
                beforeBrowser,
                beforeScene,
                beforeScene,
                sceneChanged: false,
                afterSceneDirty: scene.IsDirty,
                beforeProject,
                beforeProject,
                projectChanged: false);
        }

        EditorAssetAutomationFileJournal fileJournal = EditorAssetAutomationFileJournal.Stage(
            ProjectRoot,
            beforeFiles,
            afterFiles);
        return new EditorAssetAutomationPreparedMutation(
            this,
            sequence,
            request.Method,
            "Refresh Assets",
            stateChanged: true,
            succeeded: true,
            requiresConfirmation: false,
            diagnostic,
            response,
            asset: null,
            affectedAssetIds: [],
            affectedFolderPaths: [],
            beforePayloadPath: ProjectRoot,
            afterPayloadPath: ProjectRoot,
            cleanupPath: null,
            directory: false,
            movesPayload: false,
            fileJournal,
            beforeBrowser,
            afterBrowser,
            beforeScene,
            afterScene,
            sceneChanged,
            afterSceneDirty: scene.IsDirty,
            beforeProject,
            afterProject,
            projectChanged);
    }

    private EditorAssetAutomationPreparedMutation PrepareCore(
        EditorAssetAutomationMutationRequest request,
        CancellationToken cancellationToken)
    {
        EditorAssetBrowserDataSource assets = _sandboxAssets ??
            throw new InvalidOperationException("Asset preparation sandbox 尚未初始化。");
        EditorSceneModel scene = _sandboxScene ??
            throw new InvalidOperationException("Asset preparation scene 尚未初始化。");
        EditorProject project = _sandboxProject ??
            throw new InvalidOperationException("Asset preparation project 尚未初始化。");
        int sequence = _nextPreparationSequence++;
        bool includeReferences = request.Method is
            AutomationProtocolConstants.ProjectAssetMoveMethod or
            AutomationProtocolConstants.ProjectFolderMoveMethod;
        string[] additionalSnapshotPaths = ResolveAdditionalSnapshotPaths(
            request,
            assets,
            project);
        EditorAssetAutomationFileSnapshot rawBeforeFiles = assets.CaptureAutomationFileSnapshot(
            includeReferences,
            additionalSnapshotPaths);
        EditorAssetAutomationBrowserSnapshot beforeBrowser = MapBrowserSnapshot(
            assets.CaptureAutomationBrowserSnapshot());
        EngineSceneDocument beforeScene = scene.ToDocument();
        int beforeSceneVersion = scene.Version;
        EditorProjectAutomationSnapshot beforeProject = project.CaptureAutomationSnapshot();

        PreparedMutationResult mutation = request.Method switch
        {
            AutomationProtocolConstants.ProjectAssetCreateMethod => PrepareCreate(assets, request.Payload),
            AutomationProtocolConstants.ProjectAssetImportMethod => PrepareImport(assets, request.Payload),
            AutomationProtocolConstants.ProjectAssetReplaceMethod => PrepareReplace(
                assets,
                request.Payload,
                cancellationToken),
            AutomationProtocolConstants.ProjectUiManifestSyncMethod => PrepareUiManifestSync(
                assets,
                project),
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod => PrepareUiManifestPreload(
                assets,
                project,
                request.Payload),
            AutomationProtocolConstants.ProjectAssetMoveMethod => PrepareMoveAsset(assets, scene, request.Payload),
            AutomationProtocolConstants.ProjectAssetDeleteMethod => PrepareDeleteAsset(assets, scene, request.Payload),
            AutomationProtocolConstants.ProjectFolderMoveMethod => PrepareMoveFolder(assets, scene, request.Payload),
            AutomationProtocolConstants.ProjectFolderDeleteMethod => PrepareDeleteFolder(assets, scene, request.Payload),
            _ => throw InvalidRequest($"未知 asset preparation method：{request.Method}。"),
        };
        cancellationToken.ThrowIfCancellationRequested();

        if (!mutation.StateChanged)
        {
            return new EditorAssetAutomationPreparedMutation(
                this,
                sequence,
                request.Method,
                mutation.Name,
                stateChanged: false,
                mutation.Succeeded,
                mutation.RequiresConfirmation,
                mutation.Diagnostic,
                mutation.SemanticResult,
                mutation.Asset,
                mutation.AffectedAssetIds,
                mutation.AffectedFolderPaths,
                string.Empty,
                string.Empty,
                cleanupPath: null,
                directory: false,
                movesPayload: false,
                fileJournal: null,
                beforeBrowser,
                beforeBrowser,
                beforeScene,
                beforeScene,
                sceneChanged: false,
                afterSceneDirty: scene.IsDirty,
                beforeProject,
                beforeProject,
                projectChanged: false);
        }

        EditorAssetAutomationFileSnapshot rawAfterFiles = assets.CaptureAutomationFileSnapshot(
            includeReferences,
            additionalSnapshotPaths);
        (EditorAssetAutomationFileSnapshot beforeFiles, EditorAssetAutomationFileSnapshot afterFiles) =
            BuildChangedSnapshots(rawBeforeFiles, rawAfterFiles, mutation);
        TrackPreparedBytes(beforeFiles, afterFiles);
        EditorAssetAutomationBrowserSnapshot afterBrowser = MapBrowserSnapshot(
            assets.CaptureAutomationBrowserSnapshot());
        if (!mutation.MovesPayload && beforeFiles.Files.Length == 0)
        {
            return !beforeBrowser.Assets.SequenceEqual(afterBrowser.Assets) ||
                !beforeBrowser.Folders.SequenceEqual(afterBrowser.Folders)
                ? throw new InvalidOperationException(
                    $"Content-only asset mutation '{request.Method}' 改变了 catalog 却没有 journal。")
                : new EditorAssetAutomationPreparedMutation(
                this,
                sequence,
                request.Method,
                mutation.Name,
                stateChanged: false,
                mutation.Succeeded,
                mutation.RequiresConfirmation,
                mutation.Diagnostic,
                mutation.SemanticResult,
                mutation.Asset,
                mutation.AffectedAssetIds,
                mutation.AffectedFolderPaths,
                string.Empty,
                string.Empty,
                cleanupPath: null,
                directory: false,
                movesPayload: false,
                fileJournal: null,
                beforeBrowser,
                beforeBrowser,
                beforeScene,
                beforeScene,
                sceneChanged: false,
                afterSceneDirty: scene.IsDirty,
                beforeProject,
                beforeProject,
                projectChanged: false);
        }

        EngineSceneDocument afterScene = scene.ToDocument();
        EditorProjectAutomationSnapshot afterProject = project.CaptureAutomationSnapshot();
        string beforePayload = MapSandboxPath(mutation.SandboxBeforePayloadPath);
        string afterPayload = MapSandboxPath(mutation.SandboxAfterPayloadPath);
        EditorAssetAutomationFileJournal fileJournal = EditorAssetAutomationFileJournal.Stage(
            ProjectRoot,
            beforeFiles,
            afterFiles);
        string? cleanupPath = null;
        try
        {
            cleanupPath = mutation.StageCreatedPayload
                ? StageCreatedPayload(mutation.SandboxAfterPayloadPath, afterPayload, mutation.Directory)
                : mutation.RetainArchive
                    ? CreateArchivePath(mutation.SandboxBeforePayloadPath)
                    : null;
            if (mutation.StageCreatedPayload)
            {
                beforePayload = cleanupPath!;
            }
            else if (mutation.RetainArchive)
            {
                afterPayload = cleanupPath!;
            }

            return new EditorAssetAutomationPreparedMutation(
                this,
                sequence,
                request.Method,
                mutation.Name,
                stateChanged: true,
                mutation.Succeeded,
                mutation.RequiresConfirmation,
                mutation.Diagnostic,
                mutation.SemanticResult,
                mutation.Asset,
                mutation.AffectedAssetIds,
                mutation.AffectedFolderPaths,
                beforePayload,
                afterPayload,
                cleanupPath,
                mutation.Directory,
                mutation.MovesPayload,
                fileJournal,
                beforeBrowser,
                afterBrowser,
                beforeScene,
                afterScene,
                scene.Version != beforeSceneVersion,
                scene.IsDirty,
                beforeProject,
                afterProject,
                !ProjectSnapshotsEqual(beforeProject, afterProject));
        }
        catch
        {
            fileJournal.Dispose();
            if (cleanupPath is not null)
            {
                CleanupStagedPayload(cleanupPath);
            }

            throw;
        }
    }

    private PreparedMutationResult PrepareCreate(
        EditorAssetBrowserDataSource assets,
        JsonElement? payload)
    {
        AutomationAssetCreateRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetCreateRequest,
            AutomationProtocolConstants.ProjectAssetCreateMethod);
        AssetBrowserItemKind kind = MapKind(request.Kind);
        AssetBrowserCreateResult result = assets.CreateAsset(new AssetBrowserCreateRequest(request.Path, kind));
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Path))
        {
            throw InvalidRequest(result.Diagnostic);
        }

        string target = assets.ResolveAutomationPath(result.Path);
        AssetBrowserItem? asset = result.AssetId is null ? null : RequireAsset(assets, result.AssetId);
        return new PreparedMutationResult(
            kind == AssetBrowserItemKind.Folder ? "Create Asset Folder" : "Create Asset",
            true,
            true,
            false,
            result.Diagnostic,
            asset,
            result.AssetId is null ? [] : [result.AssetId],
            kind == AssetBrowserItemKind.Folder ? [result.Path] : [],
            target,
            target,
            kind == AssetBrowserItemKind.Folder,
            StageCreatedPayload: true,
            RetainArchive: false);
    }

    private PreparedMutationResult PrepareImport(
        EditorAssetBrowserDataSource assets,
        JsonElement? payload)
    {
        AutomationAssetImportRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetImportRequest,
            AutomationProtocolConstants.ProjectAssetImportMethod);
        AssetBrowserItemKind kind = MapKind(request.Kind);
        if (kind == AssetBrowserItemKind.Folder)
        {
            throw InvalidRequest("asset.import 不接受 Folder kind。");
        }

        string source = ValidateImportSource(request.SourcePath, _importRoots);
        AssetBrowserImportResult result = assets.ImportAsset(new AssetBrowserImportRequest(
            source,
            request.TargetPath,
            kind));
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.AssetId) ||
            string.IsNullOrWhiteSpace(result.Path))
        {
            throw InvalidRequest(result.Diagnostic);
        }

        string target = assets.ResolveAutomationPath(result.Path);
        return new PreparedMutationResult(
            "Import Asset",
            true,
            true,
            false,
            result.Diagnostic,
            RequireAsset(assets, result.AssetId),
            [result.AssetId],
            [],
            target,
            target,
            Directory: false,
            StageCreatedPayload: true,
            RetainArchive: false);
    }

    private PreparedMutationResult PrepareReplace(
        EditorAssetBrowserDataSource assets,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        AutomationAssetReplaceRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetReplaceRequest,
            AutomationProtocolConstants.ProjectAssetReplaceMethod);
        AssetBrowserItem before = RequireAsset(assets, request.AssetId);
        if (before.Kind == AssetBrowserItemKind.Folder)
        {
            throw InvalidRequest("asset.replace 不接受 Folder。");
        }

        string source = ValidateImportSource(request.SourcePath, _importRoots);
        FileInfo sourceInfo = new(source);
        if (sourceInfo.Length > MaximumBytes)
        {
            throw InvalidRequest($"asset.replace source 超过 {MaximumBytes} 字节 workspace 上限。");
        }

        string target = assets.ResolveAutomationPath(before.Path);
        if (FilesEqual(source, target, cancellationToken))
        {
            return PreparedMutationResult.NoChange(
                "Replace Asset",
                "资产内容与 source 完全一致。",
                before,
                [before.AssetId!],
                []);
        }

        File.Copy(source, target, overwrite: true);
        File.SetLastWriteTimeUtc(target, sourceInfo.LastWriteTimeUtc);
        assets.RefreshAssets();
        AssetBrowserItem after = RequireAsset(assets, before.AssetId!);
        return !string.Equals(after.Path, before.Path, StringComparison.OrdinalIgnoreCase) ||
            after.Kind != before.Kind
            ? throw new InvalidOperationException("asset.replace 改变了 stable asset identity 或类型。")
            : new PreparedMutationResult(
            "Replace Asset",
            true,
            true,
            false,
            $"已替换资产内容：{before.Path}",
            after,
            [before.AssetId!],
            [],
            target,
            target,
            Directory: false,
            StageCreatedPayload: false,
            RetainArchive: false,
            MovesPayload: false);
    }

    private static string ResolveReplaceTarget(
        EditorAssetBrowserDataSource assets,
        JsonElement? payload)
    {
        AutomationAssetReplaceRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetReplaceRequest,
            AutomationProtocolConstants.ProjectAssetReplaceMethod);
        AssetBrowserItem item = RequireAsset(assets, request.AssetId);
        return item.Kind == AssetBrowserItemKind.Folder
            ? throw InvalidRequest("asset.replace 不接受 Folder。")
            : assets.ResolveAutomationPath(item.Path);
    }

    private static string[] ResolveAdditionalSnapshotPaths(
        EditorAssetAutomationMutationRequest request,
        EditorAssetBrowserDataSource assets,
        EditorProject project)
    {
        return request.Method switch
        {
            AutomationProtocolConstants.ProjectAssetReplaceMethod =>
                [ResolveReplaceTarget(assets, request.Payload)],
            AutomationProtocolConstants.ProjectUiManifestSyncMethod or
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod =>
                [Path.Combine(project.ContentRootPath, "ui", "ui-manifest.json")],
            _ => [],
        };
    }

    private static PreparedMutationResult PrepareUiManifestSync(
        EditorAssetBrowserDataSource assets,
        EditorProject project)
    {
        EditorAssetManifestStore store = new(project);
        EditorUiManifestSyncResult result = store.SyncUiManifestScreens();
        assets.RefreshAssets();
        AutomationUiManifestSnapshot snapshot = EditorUiManifestAutomation.Capture(
            project.ContentRootPath,
            assets.ListAssets(),
            result.Diagnostic);
        return UiManifestMutation(
            "Sync UI Manifest",
            result.Diagnostic,
            snapshot,
            assets,
            project);
    }

    private static PreparedMutationResult PrepareUiManifestPreload(
        EditorAssetBrowserDataSource assets,
        EditorProject project,
        JsonElement? payload)
    {
        AutomationUiManifestPreloadSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationUiManifestPreloadSetRequest,
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod);
        EditorAssetManifestStore store = new(project);
        if (!store.TrySetUiManifestScreenPreload(
            request.ScreenId,
            request.Preload,
            out string diagnostic))
        {
            throw NotFound(diagnostic);
        }

        assets.RefreshAssets();
        AutomationUiManifestSnapshot snapshot = EditorUiManifestAutomation.Capture(
            project.ContentRootPath,
            assets.ListAssets(),
            diagnostic);
        return UiManifestMutation(
            "Set UI Manifest Preload",
            diagnostic,
            snapshot,
            assets,
            project);
    }

    private static PreparedMutationResult UiManifestMutation(
        string name,
        string diagnostic,
        AutomationUiManifestSnapshot snapshot,
        EditorAssetBrowserDataSource assets,
        EditorProject project)
    {
        AssetBrowserItem? manifestAsset = assets.ListAssets().FirstOrDefault(static item =>
            string.Equals(
                item.Path.Replace('\\', '/'),
                "Content/ui/ui-manifest.json",
                StringComparison.OrdinalIgnoreCase));
        string[] affectedAssetIds =
        [
            .. snapshot.Screens
                .Select(static screen => screen.AssetId)
                .Append(manifestAsset?.AssetId)
                .Where(static assetId => !string.IsNullOrWhiteSpace(assetId))
                .Select(static assetId => assetId!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string target = Path.Combine(project.ContentRootPath, "ui", "ui-manifest.json");
        return new PreparedMutationResult(
            name,
            true,
            true,
            false,
            diagnostic,
            manifestAsset,
            affectedAssetIds,
            [],
            target,
            target,
            Directory: false,
            StageCreatedPayload: false,
            RetainArchive: false,
            MovesPayload: false,
            SemanticResult: snapshot);
    }

    private static PreparedMutationResult PrepareMoveAsset(
        EditorAssetBrowserDataSource assets,
        EditorSceneModel scene,
        JsonElement? payload)
    {
        AutomationAssetMoveRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetMoveRequest,
            AutomationProtocolConstants.ProjectAssetMoveMethod);
        AssetBrowserItem before = RequireAsset(assets, request.AssetId);
        string targetBrowserPath = request.NewPath.Trim();
        if (string.Equals(before.Path, targetBrowserPath, StringComparison.OrdinalIgnoreCase))
        {
            return PreparedMutationResult.NoChange(
                "Move Asset",
                "资产已位于目标路径。",
                before,
                [before.AssetId!],
                []);
        }

        string beforePath = assets.ResolveAutomationPath(before.Path);
        string afterPath = assets.ResolveAutomationPath(targetBrowserPath);
        AssetBrowserMoveResult result = assets.MoveAsset(
            new AssetBrowserMoveRequest(before.Path, before.AssetId!, before.Kind, targetBrowserPath),
            scene);
        return !result.Succeeded
            ? throw InvalidRequest(result.Diagnostic)
            : new PreparedMutationResult(
            "Move Asset",
            true,
            true,
            false,
            result.Diagnostic,
            RequireAsset(assets, before.AssetId!),
            [before.AssetId!],
            [],
            beforePath,
            afterPath,
            Directory: false,
            StageCreatedPayload: false,
            RetainArchive: false);
    }

    private static PreparedMutationResult PrepareDeleteAsset(
        EditorAssetBrowserDataSource assets,
        EditorSceneModel scene,
        JsonElement? payload)
    {
        AutomationAssetDeleteRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetDeleteRequest,
            AutomationProtocolConstants.ProjectAssetDeleteMethod);
        AssetBrowserItem item = RequireAsset(assets, request.AssetId);
        string originalPath = assets.ResolveAutomationPath(item.Path);
        string sandboxArchive = assets.CreateAutomationArchivePath(originalPath);
        AssetBrowserDeleteResult result = assets.DeleteAsset(
            new AssetBrowserDeleteRequest(item.Path, item.AssetId!, item.Kind, request.Confirmed),
            scene,
            sandboxArchive);
        return !result.Succeeded
            ? result.RequiresConfirmation
                ? PreparedMutationResult.NoChange(
                    "Delete Asset",
                    result.Diagnostic,
                    asset: null,
                    [item.AssetId!],
                    []) with
                { RequiresConfirmation = true }
                : throw InvalidRequest(result.Diagnostic)
            : new PreparedMutationResult(
            "Delete Asset",
            true,
            true,
            false,
            result.Diagnostic,
            Asset: null,
            [item.AssetId!],
            [],
            originalPath,
            sandboxArchive,
            Directory: false,
            StageCreatedPayload: false,
            RetainArchive: true);
    }

    private static PreparedMutationResult PrepareMoveFolder(
        EditorAssetBrowserDataSource assets,
        EditorSceneModel scene,
        JsonElement? payload)
    {
        AutomationFolderMoveRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationFolderMoveRequest,
            AutomationProtocolConstants.ProjectFolderMoveMethod);
        string current = request.Path.Trim();
        string target = request.NewPath.Trim();
        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            return PreparedMutationResult.NoChange(
                "Move Asset Folder",
                "文件夹已位于目标路径。",
                asset: null,
                [],
                [current, target]);
        }

        string[] ids = CaptureFolderAssetIds(assets, current);
        string beforePath = assets.ResolveAutomationPath(current);
        string afterPath = assets.ResolveAutomationPath(target);
        AssetBrowserFolderMoveResult result = assets.MoveFolder(
            new AssetBrowserFolderMoveRequest(current, target),
            scene);
        return !result.Succeeded
            ? throw InvalidRequest(result.Diagnostic)
            : new PreparedMutationResult(
            "Move Asset Folder",
            true,
            true,
            false,
            result.Diagnostic,
            Asset: null,
            ids,
            [current, target],
            beforePath,
            afterPath,
            Directory: true,
            StageCreatedPayload: false,
            RetainArchive: false);
    }

    private static PreparedMutationResult PrepareDeleteFolder(
        EditorAssetBrowserDataSource assets,
        EditorSceneModel scene,
        JsonElement? payload)
    {
        AutomationFolderDeleteRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationFolderDeleteRequest,
            AutomationProtocolConstants.ProjectFolderDeleteMethod);
        string folder = request.Path.Trim();
        string[] ids = CaptureFolderAssetIds(assets, folder);
        string originalPath = assets.ResolveAutomationPath(folder);
        string sandboxArchive = assets.CreateAutomationArchivePath(originalPath);
        AssetBrowserFolderDeleteResult result = assets.DeleteFolder(
            new AssetBrowserFolderDeleteRequest(folder, ids, request.Confirmed),
            scene,
            sandboxArchive);
        return !result.Succeeded
            ? result.RequiresConfirmation
                ? PreparedMutationResult.NoChange(
                    "Delete Asset Folder",
                    result.Diagnostic,
                    asset: null,
                    ids,
                    [folder]) with
                { RequiresConfirmation = true }
                : throw InvalidRequest(result.Diagnostic)
            : new PreparedMutationResult(
            "Delete Asset Folder",
            true,
            true,
            false,
            result.Diagnostic,
            Asset: null,
            ids,
            [folder],
            originalPath,
            sandboxArchive,
            Directory: true,
            StageCreatedPayload: false,
            RetainArchive: true);
    }

    private void EnsureInitialized(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            return;
        }

        try
        {
            EnsureProjectRootSafe();
            _ = Directory.CreateDirectory(_sandboxRoot);
            CopyRoot(_contentRoot, cancellationToken);
            if (!IsInside(_scriptRoot, _contentRoot))
            {
                CopyRoot(_scriptRoot, cancellationToken);
            }

            CopyProjectFile(EditorProject.ProjectFileName, required: true, cancellationToken);
            CopyProjectFile(EngineProjectSettingsStore.ProjectSettingsFileName, required: false, cancellationToken);
            CopyProjectFile(EngineProjectSettingsStore.PlayerSettingsFileName, required: false, cancellationToken);
            CopyProjectFile(EngineProjectSettingsStore.BuildSettingsFileName, required: false, cancellationToken);
            CopyProjectFile(EditorAssetManifestStore.ManifestRelativePath, required: false, cancellationToken);
            CopyProjectFile(".pixelengine/script-assets.json", required: false, cancellationToken);

            _sandboxProject = EditorProject.Load(_sandboxRoot);
            _sandboxScene = EditorSceneModel.FromDocument(_initialScene);
            _initialRefreshProject = _sandboxProject.CaptureAutomationSnapshot();
            _initialRefreshSceneVersion = _sandboxScene.Version;
            _initialRefreshFiles = CaptureRefreshFiles(_sandboxProject, cancellationToken);
            _sandboxAssets = new EditorAssetBrowserDataSource(
                _sandboxProject,
                thumbnailProvider: null,
                _sandboxScene,
                () => _currentScenePath);
        }
        catch
        {
            _ = Interlocked.Exchange(ref _initialized, 0);
            throw;
        }
    }

    private void CopyRoot(string sourceRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        string[] directories = EditorAssetFileTraversal.EnumerateDirectories(
            sourceRoot,
            MaximumDirectories,
            "automation asset sandbox 目录复制");
        for (int i = 0; i < directories.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = MapRealPathToSandbox(directories[i]);
            _ = Directory.CreateDirectory(destination);
        }

        string[] files = EditorAssetFileTraversal.EnumerateFiles(
            sourceRoot,
            EditorAssetFileTraversalSelection.AllFiles,
            MaximumFiles,
            "automation asset sandbox 文件复制");
        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyFile(files[i], MapRealPathToSandbox(files[i]));
        }
    }

    private void CopyProjectFile(
        string relativePath,
        bool required,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string source = Path.GetFullPath(Path.Combine(
            ProjectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(source))
        {
            if (required)
            {
                throw new FileNotFoundException("Automation asset sandbox 缺少工程文件。", source);
            }

            return;
        }

        CopyFile(source, MapRealPathToSandbox(source));
    }

    private void CopyFile(string source, string destination)
    {
        string canonicalSource = Path.GetFullPath(source);
        if (!_copiedSources.Add(canonicalSource))
        {
            return;
        }

        FileInfo info = new(canonicalSource);
        _copiedBytes = checked(_copiedBytes + info.Length);
        if (_copiedSources.Count > MaximumFiles || _copiedBytes > MaximumBytes)
        {
            throw new InvalidOperationException(
                $"Automation asset sandbox 超过 {MaximumFiles} 文件或 {MaximumBytes} 字节上限。");
        }

        string? parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            _ = Directory.CreateDirectory(parent);
        }

        File.Copy(canonicalSource, destination, overwrite: false);
        File.SetLastWriteTimeUtc(destination, info.LastWriteTimeUtc);
    }

    private (EditorAssetAutomationFileSnapshot Before, EditorAssetAutomationFileSnapshot After)
        BuildChangedSnapshots(
            EditorAssetAutomationFileSnapshot before,
            EditorAssetAutomationFileSnapshot after,
            PreparedMutationResult mutation)
    {
        Dictionary<string, EditorAssetAutomationFileState> afterByPath = after.Files.ToDictionary(
            static file => file.FullPath,
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> consumedAfter = new(StringComparer.OrdinalIgnoreCase);
        List<EditorAssetAutomationFileState> changedBefore = [];
        List<EditorAssetAutomationFileState> changedAfter = [];
        for (int i = 0; i < before.Files.Length; i++)
        {
            EditorAssetAutomationFileState beforeFile = before.Files[i];
            string expectedAfterPath = mutation.IsMove
                ? RewritePath(
                    beforeFile.FullPath,
                    mutation.SandboxBeforePayloadPath,
                    mutation.SandboxAfterPayloadPath,
                    mutation.Directory)
                : beforeFile.FullPath;
            if (afterByPath.TryGetValue(expectedAfterPath, out EditorAssetAutomationFileState? afterFile))
            {
                _ = consumedAfter.Add(expectedAfterPath);
                if (ContentsEqual(beforeFile.Contents, afterFile.Contents))
                {
                    continue;
                }

                changedBefore.Add(MapFileState(beforeFile));
                changedAfter.Add(MapFileState(afterFile));
                continue;
            }

            if (!IsPrimaryPath(beforeFile.FullPath, mutation))
            {
                changedBefore.Add(MapFileState(beforeFile));
                changedAfter.Add(new EditorAssetAutomationFileState(
                    MapSandboxPath(expectedAfterPath),
                    Contents: null,
                    LastWriteTimeUtc: null));
            }
        }

        for (int i = 0; i < after.Files.Length; i++)
        {
            EditorAssetAutomationFileState afterFile = after.Files[i];
            if (consumedAfter.Contains(afterFile.FullPath) || IsPrimaryPath(afterFile.FullPath, mutation))
            {
                continue;
            }

            changedBefore.Add(new EditorAssetAutomationFileState(
                MapSandboxPath(afterFile.FullPath),
                Contents: null,
                LastWriteTimeUtc: null));
            changedAfter.Add(MapFileState(afterFile));
        }

        return (
            new EditorAssetAutomationFileSnapshot([.. changedBefore]),
            new EditorAssetAutomationFileSnapshot([.. changedAfter]));
    }

    private string StageCreatedPayload(string sandboxPath, string realTargetPath, bool directory)
    {
        string archive = CreateArchivePath(realTargetPath);
        try
        {
            if (directory)
            {
                CopyDirectoryPayload(sandboxPath, archive);
            }
            else
            {
                FileInfo info = new(sandboxPath);
                TrackPreparedBytes(info.Length);
                File.Copy(sandboxPath, archive, overwrite: false);
                File.SetLastWriteTimeUtc(archive, info.LastWriteTimeUtc);
            }

            return archive;
        }
        catch
        {
            if (Directory.Exists(archive))
            {
                Directory.Delete(archive, recursive: true);
            }
            else if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            throw;
        }
    }

    private void CopyDirectoryPayload(string source, string target)
    {
        _ = Directory.CreateDirectory(target);
        string[] directories = EditorAssetFileTraversal.EnumerateDirectories(
            source,
            MaximumDirectories,
            "automation asset payload staging");
        for (int i = 0; i < directories.Length; i++)
        {
            string relative = Path.GetRelativePath(source, directories[i]);
            _ = Directory.CreateDirectory(Path.Combine(target, relative));
        }

        string[] files = EditorAssetFileTraversal.EnumerateFiles(
            source,
            EditorAssetFileTraversalSelection.AllFiles,
            MaximumFiles,
            "automation asset payload staging");
        for (int i = 0; i < files.Length; i++)
        {
            FileInfo info = new(files[i]);
            TrackPreparedBytes(info.Length);
            string destination = Path.Combine(target, Path.GetRelativePath(source, files[i]));
            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                _ = Directory.CreateDirectory(parent);
            }

            File.Copy(files[i], destination, overwrite: false);
            File.SetLastWriteTimeUtc(destination, info.LastWriteTimeUtc);
        }
    }

    private string CreateArchivePath(string sourcePath)
    {
        string archiveRoot = Path.Combine(ProjectRoot, ".pixelengine", "automation-undo");
        _ = Directory.CreateDirectory(archiveRoot);
        EnsureNoReparsePoint(ProjectRoot, archiveRoot);
        string parent = Path.Combine(archiveRoot, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(parent);
        EnsureNoReparsePoint(archiveRoot, parent);
        return Path.Combine(parent, Path.GetFileName(sourcePath));
    }

    private static void CleanupStagedPayload(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }

        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
            !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }

    private static bool BrowserSnapshotsEqual(
        EditorAssetAutomationBrowserSnapshot left,
        EditorAssetAutomationBrowserSnapshot right)
    {
        return left.Assets.SequenceEqual(right.Assets) &&
            left.Folders.SequenceEqual(right.Folders) &&
            string.Equals(left.LastDiagnostic, right.LastDiagnostic, StringComparison.Ordinal) &&
            string.Equals(left.RuntimeDiagnostic, right.RuntimeDiagnostic, StringComparison.Ordinal);
    }

    private static EditorAssetAutomationFileSnapshot CaptureRefreshFiles(
        EditorProject project,
        CancellationToken cancellationToken)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(project.ProjectRoot, EditorProject.ProjectFileName),
            Path.Combine(project.ProjectRoot, EngineProjectSettingsStore.ProjectSettingsFileName),
            Path.Combine(project.ProjectRoot, EngineProjectSettingsStore.PlayerSettingsFileName),
            Path.Combine(project.ProjectRoot, EngineProjectSettingsStore.BuildSettingsFileName),
            Path.Combine(project.ProjectRoot, EditorAssetManifestStore.ManifestRelativePath),
            Path.Combine(project.ProjectRoot, ".pixelengine", "script-assets.json"),
            Path.Combine(project.ContentRootPath, "ui", "ui-manifest.json"),
        };
        if (Directory.Exists(project.ContentRootPath))
        {
            string[] references = EditorAssetFileTraversal.EnumerateFiles(
                project.ContentRootPath,
                EditorAssetFileTraversalSelection.ReferenceDocuments,
                MaximumFiles,
                "automation asset refresh reference document 扫描");
            for (int i = 0; i < references.Length; i++)
            {
                _ = paths.Add(references[i]);
            }
        }

        if (paths.Count > MaximumFiles)
        {
            throw new InvalidOperationException(
                $"Automation asset refresh before-image 文件数超过 {MaximumFiles} 上限。");
        }

        EditorAssetAutomationFileState[] files = new EditorAssetAutomationFileState[paths.Count];
        long totalBytes = 0;
        int index = 0;
        foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string canonical = Path.GetFullPath(path);
            byte[]? contents = null;
            DateTime? lastWriteTimeUtc = null;
            if (File.Exists(canonical))
            {
                FileInfo before = new(canonical);
                totalBytes = checked(totalBytes + before.Length);
                if (totalBytes > MaximumBytes)
                {
                    throw new InvalidOperationException(
                        $"Automation asset refresh before-image 超过 {MaximumBytes} 字节上限。");
                }

                contents = File.ReadAllBytes(canonical);
                lastWriteTimeUtc = before.LastWriteTimeUtc;
                FileInfo after = new(canonical);
                if (!after.Exists || after.Length != before.Length ||
                    after.LastWriteTimeUtc != before.LastWriteTimeUtc)
                {
                    throw new IOException($"捕获 asset refresh before-image 时文件发生变化：{canonical}");
                }
            }

            files[index++] = new EditorAssetAutomationFileState(
                canonical,
                contents,
                lastWriteTimeUtc);
        }

        return new EditorAssetAutomationFileSnapshot(files);
    }

    private void TrackPreparedBytes(
        EditorAssetAutomationFileSnapshot before,
        EditorAssetAutomationFileSnapshot after)
    {
        long bytes = 0;
        for (int i = 0; i < before.Files.Length; i++)
        {
            bytes = checked(bytes + (before.Files[i].Contents?.LongLength ?? 0));
        }

        for (int i = 0; i < after.Files.Length; i++)
        {
            bytes = checked(bytes + (after.Files[i].Contents?.LongLength ?? 0));
        }

        TrackPreparedBytes(bytes);
    }

    private void TrackPreparedBytes(long bytes)
    {
        _preparedBytes = checked(_preparedBytes + bytes);
        if (_preparedBytes > MaximumBytes)
        {
            throw new InvalidOperationException(
                $"Automation asset prepared delta 超过 {MaximumBytes} 字节上限。");
        }
    }

    private EditorAssetAutomationFileState MapFileState(EditorAssetAutomationFileState file)
    {
        return new EditorAssetAutomationFileState(
            MapSandboxPath(file.FullPath),
            file.Contents,
            file.LastWriteTimeUtc);
    }

    private EditorAssetAutomationBrowserSnapshot MapBrowserSnapshot(
        EditorAssetAutomationBrowserSnapshot snapshot)
    {
        AssetBrowserItem[] assets = new AssetBrowserItem[snapshot.Assets.Length];
        for (int i = 0; i < assets.Length; i++)
        {
            AssetBrowserItem item = snapshot.Assets[i];
            AssetBrowserDescriptor? descriptor = item.Descriptor is { } value
                ? value with
                {
                    TypeLabel = SanitizeSandboxPath(value.TypeLabel),
                    Purpose = SanitizeSandboxPath(value.Purpose),
                }
                : null;
            assets[i] = item with
            {
                PreviewSummary = item.PreviewSummary is null
                    ? null
                    : SanitizeSandboxPath(item.PreviewSummary),
                Descriptor = descriptor,
            };
        }

        return new EditorAssetAutomationBrowserSnapshot(
            assets,
            [.. snapshot.Folders],
            SanitizeSandboxPath(snapshot.LastDiagnostic),
            SanitizeSandboxPath(snapshot.RuntimeDiagnostic));
    }

    private string SanitizeSandboxPath(string value)
    {
        return value.Replace(_sandboxRoot, ProjectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string MapSandboxPath(string sandboxPath)
    {
        string fullPath = Path.GetFullPath(sandboxPath);
        return !IsInside(fullPath, _sandboxRoot)
            ? throw new InvalidOperationException($"Sandbox path 越过隔离工程：{sandboxPath}")
            : Path.GetFullPath(Path.Combine(ProjectRoot, Path.GetRelativePath(_sandboxRoot, fullPath)));
    }

    private string MapRealPathToSandbox(string realPath)
    {
        string fullPath = Path.GetFullPath(realPath);
        return !IsInside(fullPath, ProjectRoot)
            ? throw new InvalidOperationException($"Project file 越过工程根：{realPath}")
            : Path.GetFullPath(Path.Combine(_sandboxRoot, Path.GetRelativePath(ProjectRoot, fullPath)));
    }

    private void EnsureProjectRootSafe()
    {
        if (!Directory.Exists(ProjectRoot) ||
            (File.GetAttributes(ProjectRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Automation asset project root 不存在或是 reparse point。");
        }

        string metadataRoot = Path.Combine(ProjectRoot, ".pixelengine");
        _ = Directory.CreateDirectory(metadataRoot);
        EnsureNoReparsePoint(ProjectRoot, metadataRoot);
    }

    private static void EnsureNoReparsePoint(string root, string path)
    {
        string canonicalRoot = Path.GetFullPath(root);
        string current = Path.GetFullPath(path);
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Automation asset staging 路径包含 reparse point：{current}");
            }

            if (string.Equals(current, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("Automation asset staging 无法回溯到权威根。");
        }
    }

    private static string ResolveProjectChild(string projectRoot, string relativePath, string field)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"{field} 必须是工程内相对路径。");
        }

        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        return IsInside(fullPath, projectRoot)
            ? fullPath
            : throw new InvalidOperationException($"{field} 越过工程根目录。");
    }

    private static bool IsInside(string candidate, string root)
    {
        string fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string prefix = fullRoot + Path.DirectorySeparatorChar;
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
            fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string RewritePath(string path, string source, string target, bool directory)
    {
        return string.Equals(path, source, StringComparison.OrdinalIgnoreCase)
            ? target
            : directory && IsInside(path, source)
            ? Path.Combine(target, Path.GetRelativePath(source, path))
            : path;
    }

    private static bool IsPrimaryPath(string path, PreparedMutationResult mutation)
    {
        return string.Equals(path, mutation.SandboxBeforePayloadPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, mutation.SandboxAfterPayloadPath, StringComparison.OrdinalIgnoreCase) ||
            (mutation.Directory &&
                (IsInside(path, mutation.SandboxBeforePayloadPath) ||
                 IsInside(path, mutation.SandboxAfterPayloadPath)));
    }

    private static bool ContentsEqual(byte[]? left, byte[]? right)
    {
        return ReferenceEquals(left, right) ||
            (left is not null && right is not null && left.AsSpan().SequenceEqual(right));
    }

    private static bool FilesEqual(
        string leftPath,
        string rightPath,
        CancellationToken cancellationToken)
    {
        FileInfo leftInfo = new(leftPath);
        FileInfo rightInfo = new(rightPath);
        if (!rightInfo.Exists || leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        byte[] leftBuffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        byte[] rightBuffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using FileStream left = File.OpenRead(leftPath);
            using FileStream right = File.OpenRead(rightPath);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int leftRead = left.Read(leftBuffer);
                int rightRead = right.Read(rightBuffer);
                if (leftRead != rightRead)
                {
                    return false;
                }

                if (leftRead == 0)
                {
                    return true;
                }

                if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                {
                    return false;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(leftBuffer);
            ArrayPool<byte>.Shared.Return(rightBuffer);
        }
    }

    private static bool ProjectSnapshotsEqual(
        EditorProjectAutomationSnapshot left,
        EditorProjectAutomationSnapshot right)
    {
        EditorProjectDocument a = left.Document;
        EditorProjectDocument b = right.Document;
        return a.FormatVersion == b.FormatVersion &&
            string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
            string.Equals(a.ContentRoot, b.ContentRoot, StringComparison.Ordinal) &&
            string.Equals(a.ScriptSourceDir, b.ScriptSourceDir, StringComparison.Ordinal) &&
            string.Equals(a.StartScene, b.StartScene, StringComparison.Ordinal) &&
            (a.Scenes ?? []).Select(static scene => (scene.Name, scene.Path))
                .SequenceEqual((b.Scenes ?? []).Select(static scene => (scene.Name, scene.Path))) &&
            string.Equals(
                left.ProjectSettingsDiagnostic,
                right.ProjectSettingsDiagnostic,
                StringComparison.Ordinal);
    }

    private static AssetBrowserItem RequireAsset(EditorAssetBrowserDataSource assets, string assetId)
    {
        AssetBrowserItem item = assets.ListAssets().FirstOrDefault(candidate =>
            string.Equals(candidate.AssetId, assetId, StringComparison.OrdinalIgnoreCase));
        return item.AssetId is null
            ? throw NotFound($"未找到 assetId：{assetId}。")
            : item;
    }

    private static string[] CaptureFolderAssetIds(
        EditorAssetBrowserDataSource assets,
        string folderPath)
    {
        string normalized = folderPath.Trim().Replace('\\', '/').TrimEnd('/');
        string prefix = normalized + "/";
        return
        [
            .. assets.ListAssets()
                .Where(item => item.AssetId is not null &&
                    item.Path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(static item => item.AssetId!)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string ValidateImportSource(string sourcePath, string[] roots)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
        {
            throw PathDenied("asset.import sourcePath 必须是显式获准 root 下的 canonical absolute path。");
        }

        string canonical = Path.GetFullPath(sourcePath.Trim());
        if (canonical.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw PathDenied("asset.import 不接受 UNC 或 device path。");
        }

        string? allowedRoot = null;
        for (int i = 0; i < roots.Length; i++)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(roots[i]));
            if (!Directory.Exists(root) || ContainsReparsePoint(root, root))
            {
                continue;
            }

            if (IsInside(canonical, root) && !string.Equals(canonical, root, StringComparison.OrdinalIgnoreCase))
            {
                allowedRoot = root;
                break;
            }
        }

        return allowedRoot is null
            ? throw PathDenied("asset.import sourcePath 不在任何 --automation-import-root 下。")
            : !File.Exists(canonical)
                ? throw NotFound("asset.import sourcePath 在获准 root 下不存在或不是普通文件。")
                : ContainsReparsePoint(canonical, allowedRoot)
                    ? throw PathDenied("asset.import sourcePath 或其父目录包含 reparse point。")
                    : canonical;
    }

    private static bool ContainsReparsePoint(string path, string root)
    {
        string current = Path.GetFullPath(path);
        string canonicalRoot = Path.GetFullPath(root);
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (string.Equals(current, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = Path.GetDirectoryName(current)
                ?? throw PathDenied("asset.import sourcePath 无法回溯到获准 root。");
        }
    }

    private static T Deserialize<T>(
        JsonElement? payload,
        JsonTypeInfo<T> typeInfo,
        string method)
        where T : class
    {
        try
        {
            T value = payload?.Deserialize(typeInfo) ?? throw InvalidRequest($"{method} payload 不能为空。");
            int schemaVersion = value switch
            {
                AutomationAssetCreateRequest request => request.SchemaVersion,
                AutomationAssetImportRequest request => request.SchemaVersion,
                AutomationAssetReplaceRequest request => request.SchemaVersion,
                AutomationUiManifestPreloadSetRequest request => request.SchemaVersion,
                AutomationAssetMoveRequest request => request.SchemaVersion,
                AutomationAssetDeleteRequest request => request.SchemaVersion,
                AutomationFolderMoveRequest request => request.SchemaVersion,
                AutomationFolderDeleteRequest request => request.SchemaVersion,
                _ => throw new InvalidOperationException($"未知 asset request DTO：{typeof(T).FullName}。"),
            };
            return schemaVersion == AutomationProtocolConstants.WireSchemaVersion
                ? value
                : throw InvalidRequest($"{method} schemaVersion 必须是 {AutomationProtocolConstants.WireSchemaVersion}。");
        }
        catch (JsonException exception)
        {
            throw InvalidRequest($"{method} payload schema 无效：{exception.Message}");
        }
    }

    private static AssetBrowserItemKind MapKind(AutomationAssetKind kind)
    {
        return kind switch
        {
            AutomationAssetKind.Folder => AssetBrowserItemKind.Folder,
            AutomationAssetKind.Material => AssetBrowserItemKind.Material,
            AutomationAssetKind.Texture => AssetBrowserItemKind.Texture,
            AutomationAssetKind.Audio => AssetBrowserItemKind.Audio,
            AutomationAssetKind.Scene => AssetBrowserItemKind.Scene,
            AutomationAssetKind.Prefab => AssetBrowserItemKind.Prefab,
            AutomationAssetKind.Script => AssetBrowserItemKind.Script,
            AutomationAssetKind.UiScreen => AssetBrowserItemKind.UiScreen,
            AutomationAssetKind.Json => AssetBrowserItemKind.Json,
            AutomationAssetKind.Other => AssetBrowserItemKind.Other,
            _ => throw InvalidRequest($"未知 automation asset kind：{kind}。"),
        };
    }

    private static AutomationRequestException InvalidRequest(string message)
    {
        return Error(AutomationErrorCodes.InvalidRequest, AutomationErrorCategory.Validation, message);
    }

    private static AutomationRequestException NotFound(string message)
    {
        return Error(AutomationErrorCodes.ResourceNotFound, AutomationErrorCategory.Validation, message);
    }

    private static AutomationRequestException PathDenied(string message)
    {
        return Error(AutomationErrorCodes.PathNotAllowed, AutomationErrorCategory.Authorization, message);
    }

    private static AutomationRequestException StateUnavailable(string message)
    {
        return Error(AutomationErrorCodes.StateUnavailable, AutomationErrorCategory.Availability, message);
    }

    private static AutomationRequestException Error(
        string code,
        AutomationErrorCategory category,
        string message)
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = code,
            Category = category,
            Message = message,
            Transient = false,
        });
    }

    private sealed record PreparedMutationResult(
        string Name,
        bool StateChanged,
        bool Succeeded,
        bool RequiresConfirmation,
        string Diagnostic,
        AssetBrowserItem? Asset,
        string[] AffectedAssetIds,
        string[] AffectedFolderPaths,
        string SandboxBeforePayloadPath,
        string SandboxAfterPayloadPath,
        bool Directory,
        bool StageCreatedPayload,
        bool RetainArchive,
        bool MovesPayload = true,
        object? SemanticResult = null)
    {
        public bool IsMove => StateChanged && MovesPayload && !StageCreatedPayload && !RetainArchive;

        public static PreparedMutationResult NoChange(
            string name,
            string diagnostic,
            AssetBrowserItem? asset,
            string[] affectedAssetIds,
            string[] affectedFolderPaths,
            object? semanticResult = null)
        {
            return new PreparedMutationResult(
                name,
                false,
                true,
                false,
                diagnostic,
                asset,
                affectedAssetIds,
                affectedFolderPaths,
                string.Empty,
                string.Empty,
                false,
                false,
                false,
                SemanticResult: semanticResult);
        }
    }
}
