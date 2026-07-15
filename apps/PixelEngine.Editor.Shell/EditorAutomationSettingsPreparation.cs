using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private const int MaximumSettingsFileCount = 4;
    private const long MaximumSettingsFileBytes = 2L * 1024 * 1024;

    private delegate IReadOnlyDictionary<string, byte[]> SettingsFileFactory(
        string stagingRoot,
        CancellationToken cancellationToken);

    private AutomationBackgroundPreparation PreparePreferences(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationEditorPreferences request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationEditorPreferences,
            AutomationProtocolConstants.PreferencesSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PreferencesSetMethod);
        EditorPreferencesDocument settings = new()
        {
            FormatVersion = EditorPreferencesDocument.CurrentFormatVersion,
            UiScale = request.UiScale,
            SaveLayoutOnExit = request.SaveLayoutOnExit,
            ReopenLastProject = request.ReopenLastProject,
            RestoreLastScene = request.RestoreLastScene,
            ExternalScriptEditor = request.ExternalScriptEditor,
            Language = request.Language,
        };
        if (!settings.TryNormalize(out EditorPreferencesDocument normalized, out string diagnostic))
        {
            throw Invalid(diagnostic);
        }

        EditorPreferencesStore store = _app.Preferences;
        AutomationPreparationScope scope = RequirePreparationScope(
            context,
            AutomationProtocolConstants.PreferencesSetMethod);
        string? storagePath = store.StoragePath is null
            ? null
            : Path.GetFullPath(store.StoragePath);
        string? authorityRoot = storagePath is null ? null : Path.GetDirectoryName(storagePath);
        string workspaceKey = $"pixelengine.settings.preferences:{storagePath ?? "memory"}";
        SettingsPreparationWorkspace<EditorPreferencesAutomationSnapshot> workspace = scope.GetOrAdd(
            workspaceKey,
            () => new SettingsPreparationWorkspace<EditorPreferencesAutomationSnapshot>(
                authorityRoot,
                store.CaptureAutomationSnapshot(),
                static (left, right) => left == right));
        EditorPreferencesAutomationSnapshot after = store.CreateAutomationAppliedSnapshot(normalized);
        JsonElement response = JsonSerializer.SerializeToElement(
            MapPreferences(normalized),
            AutomationJsonContext.Default.AutomationEditorPreferences);
        return PrepareSettingsMutation(
            workspace,
            AutomationProtocolConstants.PreferencesSetMethod,
            "Set Editor Preferences",
            after,
            response,
            storagePath is null
                ? static (_, _) => EmptySettingsFiles
                : (_, cancellationToken) => SingleSettingsFile(
                    storagePath,
                    EditorPreferencesStore.SerializeCanonical(normalized),
                    cancellationToken),
            store.CaptureAutomationSnapshot,
            store.RestoreAutomationSnapshot);
    }

    private AutomationBackgroundPreparation PrepareProjectSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationProjectSettingsSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationProjectSettingsSetRequest,
            AutomationProtocolConstants.ProjectSettingsSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectSettingsSetMethod);
        EditorProjectSession session = RequireEditSession();
        AutomationPreparationScope scope = RequirePreparationScope(
            context,
            AutomationProtocolConstants.ProjectSettingsSetMethod);
        string projectRoot = Path.GetFullPath(session.Project.ProjectRoot);
        string workspaceKey = $"pixelengine.settings.project:{projectRoot}";
        SettingsPreparationWorkspace<EditorProjectSettingsAutomationState> workspace = scope.GetOrAdd(
            workspaceKey,
            () => new SettingsPreparationWorkspace<EditorProjectSettingsAutomationState>(
                projectRoot,
                session.CaptureAutomationProjectSettingsState(),
                ProjectSettingsStatesEqual));
        ProjectSettingsDto beforeSettings = workspace.PlannedState.Panel.AppliedSettings;
        ProjectSettingsDto settings = ToProjectSettings(request, beforeSettings);
        EditorProjectSettingsAutomationState after =
            session.CreateAutomationProjectSettingsState(workspace.PlannedState, settings);
        JsonElement response = JsonSerializer.SerializeToElement(
            MapProjectSettings(session, settings),
            AutomationJsonContext.Default.AutomationProjectSettings);
        string settingsPath = Path.Combine(
            projectRoot,
            EngineProjectSettingsStore.ProjectSettingsFileName);
        string projectPath = Path.Combine(projectRoot, EditorProject.ProjectFileName);
        return PrepareSettingsMutation(
            workspace,
            AutomationProtocolConstants.ProjectSettingsSetMethod,
            "Set Project Settings",
            after,
            response,
            (stagingRoot, cancellationToken) =>
            {
                byte[] settingsBytes = SerializeProjectSettings(
                    stagingRoot,
                    settings,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                byte[] projectBytes = JsonSerializer.SerializeToUtf8Bytes(
                    after.Project.Document,
                    EditorShellJsonContext.Default.EditorProjectDocument);
                return SettingsFiles(
                    (settingsPath, settingsBytes),
                    (projectPath, projectBytes));
            },
            session.CaptureAutomationProjectSettingsState,
            session.RestoreAutomationProjectSettingsState);
    }

    private AutomationBackgroundPreparation PreparePlayerSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPlayerSettings request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPlayerSettings,
            AutomationProtocolConstants.PlayerSettingsSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PlayerSettingsSetMethod);
        EditorProjectSession session = RequireEditSession();
        PlayerSettingsDto settings = ToPlayerSettings(request);
        AutomationPreparationScope scope = RequirePreparationScope(
            context,
            AutomationProtocolConstants.PlayerSettingsSetMethod);
        string projectRoot = Path.GetFullPath(session.Project.ProjectRoot);
        string workspaceKey = $"pixelengine.settings.player:{projectRoot}";
        SettingsPreparationWorkspace<PlayerSettingsPanelAutomationSnapshot> workspace = scope.GetOrAdd(
            workspaceKey,
            () => new SettingsPreparationWorkspace<PlayerSettingsPanelAutomationSnapshot>(
                projectRoot,
                session.CaptureAutomationPlayerSettingsState(),
                PlayerSettingsStatesEqual));
        PlayerSettingsPanelAutomationSnapshot after =
            session.CreateAutomationPlayerSettingsState(settings);
        JsonElement response = JsonSerializer.SerializeToElement(
            MapPlayerSettings(settings),
            AutomationJsonContext.Default.AutomationPlayerSettings);
        string settingsPath = Path.Combine(
            projectRoot,
            EngineProjectSettingsStore.PlayerSettingsFileName);
        return PrepareSettingsMutation(
            workspace,
            AutomationProtocolConstants.PlayerSettingsSetMethod,
            "Set Player Settings",
            after,
            response,
            (stagingRoot, cancellationToken) => SingleSettingsFile(
                settingsPath,
                SerializePlayerSettings(stagingRoot, settings, cancellationToken),
                cancellationToken),
            session.CaptureAutomationPlayerSettingsState,
            session.RestoreAutomationPlayerSettingsState);
    }

    private AutomationBackgroundPreparation PrepareBuildSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBuildSettings request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBuildSettings,
            AutomationProtocolConstants.BuildSettingsSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.BuildSettingsSetMethod);
        EditorProjectSession session = RequireEditSession();
        BuildProfileDto settings = ToBuildSettings(request);
        AutomationPreparationScope scope = RequirePreparationScope(
            context,
            AutomationProtocolConstants.BuildSettingsSetMethod);
        string projectRoot = Path.GetFullPath(session.Project.ProjectRoot);
        string workspaceKey = $"pixelengine.settings.build:{projectRoot}";
        SettingsPreparationWorkspace<BuildSettingsPanelAutomationSnapshot> workspace = scope.GetOrAdd(
            workspaceKey,
            () => new SettingsPreparationWorkspace<BuildSettingsPanelAutomationSnapshot>(
                projectRoot,
                session.CaptureAutomationBuildSettingsState(),
                BuildSettingsStatesEqual));
        BuildSettingsPanelAutomationSnapshot after =
            session.CreateAutomationBuildSettingsState(settings);
        JsonElement response = JsonSerializer.SerializeToElement(
            MapBuildSettings(settings),
            AutomationJsonContext.Default.AutomationBuildSettings);
        string settingsPath = Path.Combine(
            projectRoot,
            EngineProjectSettingsStore.BuildSettingsFileName);
        return PrepareSettingsMutation(
            workspace,
            AutomationProtocolConstants.BuildSettingsSetMethod,
            "Set Build Settings",
            after,
            response,
            (stagingRoot, cancellationToken) => SingleSettingsFile(
                settingsPath,
                SerializeBuildSettings(stagingRoot, settings, cancellationToken),
                cancellationToken),
            session.CaptureAutomationBuildSettingsState,
            session.RestoreAutomationBuildSettingsState);
    }

    private static AutomationPreparationScope RequirePreparationScope(
        AutomationScheduledContext context,
        string method)
    {
        return context.PreparationScope ??
            throw new InvalidOperationException($"{method} preparation 缺少共享 workspace scope。");
    }

    private AutomationBackgroundPreparation PrepareSettingsMutation<TState>(
        SettingsPreparationWorkspace<TState> workspace,
        string method,
        string name,
        TState after,
        JsonElement response,
        SettingsFileFactory files,
        Func<TState> capture,
        Action<TState> restore)
        where TState : class
    {
        SettingsPreparationPlan<TState> plan = workspace.Plan(
            method,
            name,
            after,
            response,
            files,
            capture,
            restore,
            diagnostic => _app.ConsoleStore.AddProjectError(
                "automation-settings-cleanup",
                diagnostic));
        PreparedSettingsMutation<TState>? prepared = null;
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken =>
            {
                PreparedSettingsMutation<TState> result = workspace.Prepare(plan, cancellationToken);
                Volatile.Write(ref prepared, result);
                return ValueTask.FromResult<object?>(result);
            },
            AbortAtEditorIngress = () =>
                Volatile.Read(ref prepared)?.DisposeUncommitted(),
        };
    }

    private AutomationOperationResult CommitPreparedPreferences(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        return CommitPreparedSettings<EditorPreferencesAutomationSnapshot>(
            context,
            AutomationProtocolConstants.PreferencesSetMethod,
            [PreferencesResource]);
    }

    private AutomationOperationResult CommitPreparedProjectSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        return CommitPreparedSettings<EditorProjectSettingsAutomationState>(
            context,
            AutomationProtocolConstants.ProjectSettingsSetMethod,
            [ProjectResource, ProjectSettingsResource]);
    }

    private AutomationOperationResult CommitPreparedPlayerSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        return CommitPreparedSettings<PlayerSettingsPanelAutomationSnapshot>(
            context,
            AutomationProtocolConstants.PlayerSettingsSetMethod,
            [ProjectResource, PlayerSettingsResource]);
    }

    private AutomationOperationResult CommitPreparedBuildSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        return CommitPreparedSettings<BuildSettingsPanelAutomationSnapshot>(
            context,
            AutomationProtocolConstants.BuildSettingsSetMethod,
            [ProjectResource, BuildSettingsResource]);
    }

    private AutomationOperationResult CommitPreparedSettings<TState>(
        AutomationScheduledContext context,
        string method,
        string[] resources)
        where TState : class
    {
        PreparedSettingsMutation<TState> prepared =
            context.RequirePreparedState<PreparedSettingsMutation<TState>>();
        if (!string.Equals(prepared.Method, method, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared settings method 不匹配：expected={method}, actual={prepared.Method}。");
        }

        if (!prepared.IsSourceCurrent())
        {
            throw StateUnavailable(
                $"{method} preparation 期间设置状态发生变化；请刷新 revision 后重试。");
        }

        if (!prepared.StateChanged)
        {
            prepared.CommitNoChange();
            return new AutomationOperationResult
            {
                Payload = prepared.Response,
                ResourceIds = resources,
                WriteStateChanged = false,
            };
        }

        IAutomationUndoAction action = prepared.Apply();
        return new AutomationOperationResult
        {
            Payload = prepared.Response,
            ResourceIds = resources,
            UndoAction = action,
        };
    }

    private static IReadOnlyDictionary<string, byte[]> SingleSettingsFile(
        string path,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SettingsFiles((path, contents));
    }

    private static IReadOnlyDictionary<string, byte[]> SettingsFiles(
        params (string Path, byte[] Contents)[] files)
    {
        Dictionary<string, byte[]> result = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
        {
            string path = Path.GetFullPath(files[i].Path);
            if (!result.TryAdd(path, files[i].Contents))
            {
                throw new InvalidOperationException($"Settings preparation target 重复：{path}");
            }
        }

        return result;
    }

    private static byte[] SerializeProjectSettings(
        string stagingRoot,
        ProjectSettingsDto settings,
        CancellationToken cancellationToken)
    {
        string root = CreateSerializationDirectory(stagingRoot);
        cancellationToken.ThrowIfCancellationRequested();
        EngineProjectSettingsStore.SaveProjectSettings(root, settings);
        return ReadSerializationOutput(
            Path.Combine(root, EngineProjectSettingsStore.ProjectSettingsFileName),
            cancellationToken);
    }

    private static byte[] SerializePlayerSettings(
        string stagingRoot,
        PlayerSettingsDto settings,
        CancellationToken cancellationToken)
    {
        string root = CreateSerializationDirectory(stagingRoot);
        cancellationToken.ThrowIfCancellationRequested();
        EngineProjectSettingsStore.SavePlayerSettings(root, settings);
        return ReadSerializationOutput(
            Path.Combine(root, EngineProjectSettingsStore.PlayerSettingsFileName),
            cancellationToken);
    }

    private static byte[] SerializeBuildSettings(
        string stagingRoot,
        BuildProfileDto settings,
        CancellationToken cancellationToken)
    {
        string root = CreateSerializationDirectory(stagingRoot);
        string path = Path.Combine(root, EngineProjectSettingsStore.BuildSettingsFileName);
        cancellationToken.ThrowIfCancellationRequested();
        EngineProjectSettingsStore.SaveBuildProfileToFile(path, settings);
        return ReadSerializationOutput(path, cancellationToken);
    }

    private static string CreateSerializationDirectory(string stagingRoot)
    {
        string path = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] ReadSerializationOutput(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo before = new(path);
        if (!before.Exists || before.Length > MaximumSettingsFileBytes)
        {
            throw new IOException($"Settings serialization output 大小无效：{path}");
        }

        byte[] contents = File.ReadAllBytes(path);
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo after = new(path);
        return after.Exists && after.Length == before.Length &&
            after.LastWriteTimeUtc == before.LastWriteTimeUtc
                ? contents
                : throw new IOException($"读取 settings serialization output 时文件发生变化：{path}");
    }

    private static bool ProjectSettingsStatesEqual(
        EditorProjectSettingsAutomationState left,
        EditorProjectSettingsAutomationState right)
    {
        return ProjectSettingsPanelStatesEqual(left.Panel, right.Panel) &&
            string.Equals(
                left.Project.ProjectSettingsDiagnostic,
                right.Project.ProjectSettingsDiagnostic,
                StringComparison.Ordinal) &&
            JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(
                    left.Project.Document,
                    EditorShellJsonContext.Default.EditorProjectDocument),
                JsonSerializer.SerializeToElement(
                    right.Project.Document,
                    EditorShellJsonContext.Default.EditorProjectDocument));
    }

    private static bool ProjectSettingsPanelStatesEqual(
        ProjectSettingsPanelAutomationSnapshot left,
        ProjectSettingsPanelAutomationSnapshot right)
    {
        return ProjectSettingsEqual(left.AppliedSettings, right.AppliedSettings) &&
            ProjectSettingsEqual(left.DraftSettings, right.DraftSettings) &&
            string.Equals(left.ContentGlobsText, right.ContentGlobsText, StringComparison.Ordinal) &&
            string.Equals(left.PersistentDiagnostic, right.PersistentDiagnostic, StringComparison.Ordinal) &&
            left.DraftIsValid == right.DraftIsValid &&
            string.Equals(left.ValidationMessage, right.ValidationMessage, StringComparison.Ordinal) &&
            left.HasPendingChanges == right.HasPendingChanges &&
            left.HasDraftChanges == right.HasDraftChanges &&
            left.RequiresRepair == right.RequiresRepair;
    }

    private static bool PlayerSettingsStatesEqual(
        PlayerSettingsPanelAutomationSnapshot left,
        PlayerSettingsPanelAutomationSnapshot right)
    {
        return SettingsEqual(
                MapPlayerSettings(left.AppliedSettings),
                MapPlayerSettings(right.AppliedSettings),
                AutomationJsonContext.Default.AutomationPlayerSettings) &&
            SettingsEqual(
                MapPlayerSettings(left.DraftSettings),
                MapPlayerSettings(right.DraftSettings),
                AutomationJsonContext.Default.AutomationPlayerSettings) &&
            string.Equals(left.PersistentDiagnostic, right.PersistentDiagnostic, StringComparison.Ordinal) &&
            left.DraftIsValid == right.DraftIsValid &&
            string.Equals(left.ValidationMessage, right.ValidationMessage, StringComparison.Ordinal) &&
            left.HasPendingChanges == right.HasPendingChanges &&
            left.HasDraftChanges == right.HasDraftChanges &&
            left.RequiresRepair == right.RequiresRepair;
    }

    private static bool BuildSettingsStatesEqual(
        BuildSettingsPanelAutomationSnapshot left,
        BuildSettingsPanelAutomationSnapshot right)
    {
        return SettingsEqual(
                MapBuildSettings(left.Settings),
                MapBuildSettings(right.Settings),
                AutomationJsonContext.Default.AutomationBuildSettings) &&
            SettingsEqual(
                MapBuildSettings(left.PersistedSettings),
                MapBuildSettings(right.PersistedSettings),
                AutomationJsonContext.Default.AutomationBuildSettings) &&
            string.Equals(left.ValidationMessage, right.ValidationMessage, StringComparison.Ordinal) &&
            string.Equals(left.PersistentDiagnostic, right.PersistentDiagnostic, StringComparison.Ordinal) &&
            left.RequiresRepair == right.RequiresRepair &&
            left.BuildRunning == right.BuildRunning;
    }

    private static readonly IReadOnlyDictionary<string, byte[]> EmptySettingsFiles =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    private sealed class SettingsPreparationWorkspace<TState> : IDisposable
        where TState : class
    {
        private readonly string? _authorityRoot;
        private readonly string? _stagingRoot;
        private readonly Func<TState, TState, bool> _statesEqual;
        private readonly Dictionary<string, EditorAssetAutomationFileState> _virtualFiles =
            new(StringComparer.OrdinalIgnoreCase);
        private int _nextPlanSequence;
        private int _nextPrepareSequence;
        private int _disposed;

        internal SettingsPreparationWorkspace(
            string? authorityRoot,
            TState initialState,
            Func<TState, TState, bool> statesEqual)
        {
            _authorityRoot = authorityRoot is null ? null : Path.GetFullPath(authorityRoot);
            _stagingRoot = _authorityRoot is null
                ? null
                : Path.Combine(
                    _authorityRoot,
                    ".pixelengine",
                    "automation-preparation",
                    Guid.NewGuid().ToString("N"));
            PlannedState = initialState ?? throw new ArgumentNullException(nameof(initialState));
            _statesEqual = statesEqual ?? throw new ArgumentNullException(nameof(statesEqual));
        }

        internal TState PlannedState { get; private set; }

        internal SettingsPreparationPlan<TState> Plan(
            string method,
            string name,
            TState after,
            JsonElement response,
            SettingsFileFactory files,
            Func<TState> capture,
            Action<TState> restore,
            Action<string> reportCleanupFailure)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            TState before = PlannedState;
            PlannedState = after;
            return new SettingsPreparationPlan<TState>(
                _nextPlanSequence++,
                method,
                name,
                before,
                after,
                response,
                files,
                capture,
                restore,
                _statesEqual,
                reportCleanupFailure);
        }

        internal PreparedSettingsMutation<TState> Prepare(
            SettingsPreparationPlan<TState> plan,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (plan.Sequence != _nextPrepareSequence)
            {
                throw new InvalidOperationException(
                    $"Settings preparation 顺序错误：expected={_nextPrepareSequence}, actual={plan.Sequence}。");
            }

            _nextPrepareSequence++;
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, byte[]> outputs = plan.Files(
                RequireStagingRoot(outputsRequired: _authorityRoot is not null),
                cancellationToken);
            if (outputs.Count > MaximumSettingsFileCount)
            {
                throw new InvalidOperationException(
                    $"Settings preparation 文件数超过 {MaximumSettingsFileCount} 上限。");
            }

            if (outputs.Count != 0 && _authorityRoot is null)
            {
                throw new InvalidOperationException("In-memory settings preparation 不得生成磁盘文件。");
            }

            List<EditorAssetAutomationFileState> changedBefore = [];
            List<EditorAssetAutomationFileState> changedAfter = [];
            long totalBytes = 0;
            foreach ((string rawPath, byte[] contents) in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(rawPath);
                EnsureTargetSafe(_authorityRoot!, path);
                totalBytes = checked(totalBytes + contents.LongLength);
                if (totalBytes > MaximumSettingsFileBytes)
                {
                    throw new InvalidOperationException(
                        $"Settings preparation 总字节数超过 {MaximumSettingsFileBytes} 上限。");
                }

                if (!_virtualFiles.TryGetValue(path, out EditorAssetAutomationFileState? current))
                {
                    current = ReadStableFile(path, cancellationToken);
                    _virtualFiles.Add(path, current);
                }

                if (current.Contents is not null && current.Contents.AsSpan().SequenceEqual(contents))
                {
                    continue;
                }

                changedBefore.Add(current);
                changedAfter.Add(new EditorAssetAutomationFileState(path, contents, null));
            }

            EditorAssetAutomationFileJournal? journal = null;
            try
            {
                if (changedBefore.Count != 0)
                {
                    journal = EditorAssetAutomationFileJournal.Stage(
                        _authorityRoot!,
                        new EditorAssetAutomationFileSnapshot([.. changedBefore]),
                        new EditorAssetAutomationFileSnapshot([.. changedAfter]));
                    for (int i = 0; i < journal.AfterSnapshot.Files.Length; i++)
                    {
                        EditorAssetAutomationFileState file = journal.AfterSnapshot.Files[i];
                        _virtualFiles[file.FullPath] = file;
                    }
                }

                bool stateChanged = !_statesEqual(plan.Before, plan.After) || journal is not null;
                return new PreparedSettingsMutation<TState>(
                    plan,
                    stateChanged,
                    journal);
            }
            catch
            {
                journal?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || _stagingRoot is null)
            {
                return;
            }

            if (Directory.Exists(_stagingRoot))
            {
                Directory.Delete(_stagingRoot, recursive: true);
            }

            string? parent = Path.GetDirectoryName(_stagingRoot);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
                !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }

        private string RequireStagingRoot(bool outputsRequired)
        {
            if (_stagingRoot is null)
            {
                return outputsRequired
                    ? throw new InvalidOperationException("Settings preparation 缺少 staging root。")
                    : string.Empty;
            }

            _ = Directory.CreateDirectory(_stagingRoot);
            EnsureTargetSafe(_authorityRoot!, _stagingRoot);
            return _stagingRoot;
        }

        private static EditorAssetAutomationFileState ReadStableFile(
            string path,
            CancellationToken cancellationToken)
        {
            FileInfo before = new(path);
            if (!before.Exists)
            {
                return new EditorAssetAutomationFileState(path, null, null);
            }

            if (before.Length > MaximumSettingsFileBytes)
            {
                throw new InvalidOperationException(
                    $"Settings before-image 超过 {MaximumSettingsFileBytes} 字节上限：{path}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] contents = File.ReadAllBytes(path);
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo after = new(path);
            return after.Exists && after.Length == before.Length &&
                after.LastWriteTimeUtc == before.LastWriteTimeUtc
                    ? new EditorAssetAutomationFileState(path, contents, before.LastWriteTimeUtc)
                    : throw new IOException($"捕获 settings before-image 时文件发生变化：{path}");
        }

        private static void EnsureTargetSafe(string authorityRoot, string target)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(authorityRoot));
            string fullTarget = Path.GetFullPath(target);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!fullTarget.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            {
                throw new InvalidOperationException(
                    $"Settings preparation target 越过权威根：{fullTarget}");
            }

            string? current = fullTarget;
            while (current is not null)
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Settings preparation 路径包含 reparse point：{current}");
                }

                if (string.Equals(current, root, comparison))
                {
                    return;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new InvalidOperationException("Settings preparation target 无法回溯到权威根。");
        }
    }

    private sealed record SettingsPreparationPlan<TState>(
        int Sequence,
        string Method,
        string Name,
        TState Before,
        TState After,
        JsonElement Response,
        SettingsFileFactory Files,
        Func<TState> Capture,
        Action<TState> Restore,
        Func<TState, TState, bool> StatesEqual,
        Action<string> ReportCleanupFailure)
        where TState : class;

    private sealed class PreparedSettingsMutation<TState>
        where TState : class
    {
        private readonly SettingsPreparationPlan<TState> _plan;
        private EditorAssetAutomationFileJournal? _journal;
        private int _consumed;

        internal PreparedSettingsMutation(
            SettingsPreparationPlan<TState> plan,
            bool stateChanged,
            EditorAssetAutomationFileJournal? journal)
        {
            _plan = plan;
            StateChanged = stateChanged;
            _journal = journal;
        }

        internal string Method => _plan.Method;

        internal JsonElement Response => _plan.Response;

        internal bool StateChanged { get; }

        internal bool IsSourceCurrent()
        {
            return _plan.StatesEqual(_plan.Capture(), _plan.Before);
        }

        internal void CommitNoChange()
        {
            if (StateChanged || Interlocked.Exchange(ref _consumed, 1) != 0)
            {
                throw new InvalidOperationException("Prepared settings no-change 状态无效。");
            }
        }

        internal IAutomationUndoAction Apply()
        {
            EditorAssetAutomationFileJournal? journal = ApplyPreparedState();
            EditorAutomationSettingsUndoAction<TState> action = new(
                _plan.Name,
                _plan.Before,
                _plan.After,
                _plan.Capture,
                _plan.Restore,
                _plan.StatesEqual,
                journal,
                _plan.ReportCleanupFailure);
            return action;
        }

        internal void ApplyCommand()
        {
            EditorAssetAutomationFileJournal? journal = ApplyPreparedState();
            try
            {
                journal?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _plan.ReportCleanupFailure(
                    $"清理 command settings journal 失败，已保留供人工恢复。{exception.Message}");
            }
        }

        internal void DisposeUncommitted()
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
            {
                return;
            }

            _journal?.Dispose();
            _journal = null;
        }

        private EditorAssetAutomationFileJournal? ApplyPreparedState()
        {
            if (!StateChanged || Volatile.Read(ref _consumed) != 0)
            {
                throw new InvalidOperationException("Prepared settings apply 状态无效。");
            }

            EditorAssetAutomationFileJournal? journal = _journal;
            journal?.ApplyAfter();
            try
            {
                _plan.Restore(_plan.After);
            }
            catch (Exception operationException)
            {
                List<Exception> failures = [operationException];
                TryRollback(() => journal?.ApplyBefore(), failures);
                TryRollback(() => _plan.Restore(_plan.Before), failures);
                throw new AggregateException(
                    $"Prepared settings '{_plan.Name}' 发布失败；已尝试恢复 before-image。",
                    failures);
            }

            _journal = null;
            _ = Interlocked.Exchange(ref _consumed, 1);
            return journal;
        }
    }

    private sealed class EditorAutomationSettingsUndoAction<TState> :
        IAutomationUndoAction,
        IDisposable
        where TState : class
    {
        private readonly TState _before;
        private readonly TState _after;
        private readonly Func<TState> _capture;
        private readonly Action<TState> _restore;
        private readonly Func<TState, TState, bool> _statesEqual;
        private readonly EditorAssetAutomationFileJournal? _journal;
        private readonly Action<string> _reportCleanupFailure;
        private bool _isBefore;
        private int _disposed;

        internal EditorAutomationSettingsUndoAction(
            string name,
            TState before,
            TState after,
            Func<TState> capture,
            Action<TState> restore,
            Func<TState, TState, bool> statesEqual,
            EditorAssetAutomationFileJournal? journal,
            Action<string> reportCleanupFailure)
        {
            Name = name;
            _before = before;
            _after = after;
            _capture = capture;
            _restore = restore;
            _statesEqual = statesEqual;
            _journal = journal;
            _reportCleanupFailure = reportCleanupFailure;
        }

        public string Name { get; }

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

            try
            {
                _journal?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _reportCleanupFailure(
                    $"清理 settings Undo journal 失败，已保留供人工恢复。{exception.Message}");
            }
        }

        private void Apply(bool before)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_isBefore == before)
            {
                return;
            }

            TState source = before ? _after : _before;
            TState target = before ? _before : _after;
            if (!_statesEqual(_capture(), source))
            {
                throw new InvalidOperationException(
                    $"Settings Undo/Redo '{Name}' 的当前内存状态不匹配 source before-image。");
            }

            if (before)
            {
                _journal?.ApplyBefore();
            }
            else
            {
                _journal?.ApplyAfter();
            }

            try
            {
                _restore(target);
                _isBefore = before;
            }
            catch (Exception operationException)
            {
                List<Exception> failures = [operationException];
                TryRollback(
                    () =>
                    {
                        if (before)
                        {
                            _journal?.ApplyAfter();
                        }
                        else
                        {
                            _journal?.ApplyBefore();
                        }
                    },
                    failures);
                TryRollback(() => _restore(source), failures);
                throw new AggregateException(
                    $"Settings Undo/Redo '{Name}' 失败；已尝试恢复原方向。",
                    failures);
            }
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
}
