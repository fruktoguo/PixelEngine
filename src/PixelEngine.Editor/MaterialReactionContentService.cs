using System.Text.Json;
using System.Security.Cryptography;
using PixelEngine.Content;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

#pragma warning disable IDE0290, IDE0031

/// <summary>
/// 材质/反应编辑器消费的内容服务。
/// </summary>
public interface IMaterialReactionContentService
{
    /// <summary>
    /// 加载当前 materials.json / reactions.json 编辑文档。
    /// </summary>
    MaterialReactionEditorDocument Load();

    /// <summary>
    /// 校验并预览编辑文档的展开结果。
    /// </summary>
    MaterialReactionPreviewResult Preview(MaterialReactionEditorDocument document);

    /// <summary>
    /// 写回 JSON，并触发运行时材质/反应稳定热重载。
    /// </summary>
    MaterialReactionApplyResult Apply(MaterialReactionEditorDocument document);
}

/// <summary>
/// 材质资产重载通知。
/// </summary>
public interface IMaterialAssetReloadSink
{
    /// <summary>
    /// 重新加载变更材质关联的纹理与音效资产。
    /// </summary>
    void ReloadMaterialAssets(IReadOnlyList<MaterialAssetReloadRequest> requests);
}

/// <summary>
/// 单个材质资产重载请求。
/// </summary>
public readonly record struct MaterialAssetReloadRequest(
    string MaterialName,
    ushort RuntimeId,
    bool TextureChanged,
    bool AudioChanged);

/// <summary>
/// 反应展开预览结果。
/// </summary>
public readonly record struct MaterialReactionPreviewResult(
    int MaterialCount,
    int SourceReactionCount,
    int PackedReactionCount,
    string Message);

/// <summary>后台读取并完成语法校验的 materials.json / reactions.json 内容。</summary>
public sealed class MaterialReactionContentFiles
{
    internal MaterialReactionContentFiles(
        MaterialDocumentJson materials,
        ReactionDocumentJson reactions)
    {
        Materials = materials;
        Reactions = reactions;
    }

    /// <summary>已解析的 materials.json。</summary>
    public MaterialDocumentJson Materials { get; }

    /// <summary>已解析的 reactions.json。</summary>
    public ReactionDocumentJson Reactions { get; }
}

/// <summary>
/// 材质/反应热重载结果。
/// </summary>
public sealed class MaterialReactionApplyResult
{
    /// <summary>
    /// 创建材质/反应热重载结果。
    /// </summary>
    public MaterialReactionApplyResult(
        MaterialReloadResult materialReload,
        IReadOnlyList<string> tombstonedMaterialNames,
        int liveGridFallbackReplacementCount,
        int packedReactionCount,
        IReadOnlyList<MaterialAssetReloadRequest> assetReloads,
        bool stateChanged = true,
        string? retainedJournalPath = null,
        string? cleanupError = null)
    {
        if ((retainedJournalPath is null) != (cleanupError is null))
        {
            throw new ArgumentException("retained journal 与 cleanup error 必须同时存在或同时为空。");
        }

        MaterialReload = materialReload;
        TombstonedMaterialNames = tombstonedMaterialNames;
        LiveGridFallbackReplacementCount = liveGridFallbackReplacementCount;
        PackedReactionCount = packedReactionCount;
        AssetReloads = assetReloads;
        StateChanged = stateChanged;
        RetainedJournalPath = retainedJournalPath;
        CleanupError = cleanupError;
        DiagnosticMessage = $"重载后用 fallback 替换了 {liveGridFallbackReplacementCount} 个被删材质的活 cell";
    }

    /// <summary>
    /// MaterialTable 稳定热重载结果。
    /// </summary>
    public MaterialReloadResult MaterialReload { get; }

    /// <summary>本次转为 tombstone 的稳定材质 names。</summary>
    public IReadOnlyList<string> TombstonedMaterialNames { get; }

    /// <summary>
    /// live 网格实际替换到 fallback 的 cell 数量。
    /// </summary>
    public int LiveGridFallbackReplacementCount { get; }

    /// <summary>
    /// packed reaction 数量。
    /// </summary>
    public int PackedReactionCount { get; }

    /// <summary>
    /// 纹理/音效资产重载请求。
    /// </summary>
    public IReadOnlyList<MaterialAssetReloadRequest> AssetReloads { get; }

    /// <summary>双文件或任一运行时 authority 是否实际发生语义变化。</summary>
    public bool StateChanged { get; }

    /// <summary>清理受阻时保留的双文件恢复 journal；正常完成时为 null。</summary>
    public string? RetainedJournalPath { get; }

    /// <summary>保留 journal 的清理错误；正常完成时为 null。</summary>
    public string? CleanupError { get; }

    /// <summary>是否存在需要后续清理的恢复 journal。</summary>
    public bool CleanupPending => RetainedJournalPath is not null;

    /// <summary>
    /// 面板与控制台显示的诊断消息。
    /// </summary>
    public string DiagnosticMessage { get; }
}

/// <summary>
/// 基于本地 materials.json / reactions.json 的完整热重载服务。
/// </summary>
public sealed class FileMaterialReactionContentService : IMaterialReactionContentService
{
    private const long MaximumSourceFileBytes = 16L * 1024 * 1024;
    private readonly string _materialsPath;
    private readonly string _reactionsPath;
    private readonly MaterialTable _runtimeMaterials;
    private readonly IChunkSource _residentChunks;
    private readonly ushort _fallbackMaterialId;
    private readonly Func<ReactionTable> _captureReactions;
    private readonly Action<MaterialHotTable>? _applyMaterialHotTable;
    private readonly Action<ReactionTable> _applyReactions;
    private readonly IMaterialAssetReloadSink? _assetReloadSink;
    private readonly EngineCounters? _counters;

    /// <summary>
    /// 创建文件内容服务。
    /// </summary>
    public FileMaterialReactionContentService(
        string materialsPath,
        string reactionsPath,
        MaterialTable runtimeMaterials,
        IChunkSource residentChunks,
        ushort fallbackMaterialId,
        Func<ReactionTable> captureReactions,
        Action<ReactionTable> applyReactions,
        Action<MaterialHotTable>? applyMaterialHotTable = null,
        IMaterialAssetReloadSink? assetReloadSink = null,
        EngineCounters? counters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reactionsPath);
        ArgumentNullException.ThrowIfNull(runtimeMaterials);
        ArgumentNullException.ThrowIfNull(residentChunks);
        ArgumentNullException.ThrowIfNull(captureReactions);
        ArgumentNullException.ThrowIfNull(applyReactions);
        _ = runtimeMaterials.GetName(fallbackMaterialId);
        _materialsPath = materialsPath;
        _reactionsPath = reactionsPath;
        _runtimeMaterials = runtimeMaterials;
        _residentChunks = residentChunks;
        _fallbackMaterialId = fallbackMaterialId;
        _captureReactions = captureReactions;
        _applyMaterialHotTable = applyMaterialHotTable;
        _applyReactions = applyReactions;
        _assetReloadSink = assetReloadSink;
        _counters = counters;
    }

    /// <inheritdoc />
    public MaterialReactionEditorDocument Load()
    {
        return CreateEditorDocument(LoadFiles(CancellationToken.None));
    }

    /// <summary>在后台有界读取并严格解析双文件，不访问运行时材质表。</summary>
    /// <param name="cancellationToken">读取与解析取消令牌。</param>
    /// <returns>已完成语法校验的 Content DTO。</returns>
    public MaterialReactionContentFiles LoadFiles(CancellationToken cancellationToken)
    {
        byte[] materialBytes = ReadSourceFile(_materialsPath, cancellationToken);
        byte[] reactionBytes = ReadSourceFile(_reactionsPath, cancellationToken);
        return new MaterialReactionContentFiles(
            ReadMaterialDocument(materialBytes),
            ReadReactionDocument(reactionBytes));
    }

    /// <summary>在引擎安全阶段把已解析文件映射成带 runtime ID 诊断的面板文档。</summary>
    /// <param name="files">后台读取结果。</param>
    /// <returns>新的完整面板草稿。</returns>
    public MaterialReactionEditorDocument CreateEditorDocument(MaterialReactionContentFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return MaterialReactionEditorDocument.FromContent(
            files.Materials,
            files.Reactions,
            _runtimeMaterials);
    }

    /// <inheritdoc />
    public MaterialReactionPreviewResult Preview(MaterialReactionEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MaterialDocumentJson materials = document.ToMaterialDocument();
        ReactionDocumentJson reactions = document.ToReactionDocument();
        MaterialContentStableReloadResult stable = MaterialContentLoader.BuildStableReload(materials, reactions, _runtimeMaterials);
        int packedCount = CountPackedReactions(stable.Definitions);
        int sourceCount = reactions.Reactions?.Length ?? 0;
        return new MaterialReactionPreviewResult(
            stable.Definitions.Length,
            sourceCount,
            packedCount,
            $"源规则 {sourceCount} 条，展开后 packed reaction {packedCount} 条");
    }

    /// <inheritdoc />
    public MaterialReactionApplyResult Apply(MaterialReactionEditorDocument document)
    {
        using PreparedApply prepared = PrepareApply(document);
        prepared.PrepareFiles(CancellationToken.None);
        return prepared.Commit();
    }

    /// <summary>
    /// 在引擎安全阶段捕获材质表、反应表与 live grid before-image，并构建可在后台准备文件的提交计划。
    /// </summary>
    /// <param name="document">待校验与提交的完整编辑文档。</param>
    /// <returns>只能准备并提交一次的原子热重载计划。</returns>
    public PreparedApply PrepareApply(MaterialReactionEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MaterialTableSnapshot materialBefore = _runtimeMaterials.CaptureState();
        ReactionTable reactionsBefore = _captureReactions();
        long fallbackHitsBefore = _counters?.MaterialRemapFallbackHits ?? 0;
        MaterialDocumentJson materials = document.ToMaterialDocument();
        ReactionDocumentJson reactions = document.ToReactionDocument();
        MaterialContentStableReloadResult stable = MaterialContentLoader.BuildStableReload(materials, reactions, _runtimeMaterials);
        IReadOnlyList<MaterialAssetReloadRequest> assetReloads = DetectAssetReloads(stable.Definitions);
        byte[] materialBytes = JsonSerializer.SerializeToUtf8Bytes(
            materials,
            MaterialContentJsonContext.Default.MaterialDocumentJson);
        byte[] reactionBytes = JsonSerializer.SerializeToUtf8Bytes(
            reactions,
            MaterialContentJsonContext.Default.ReactionDocumentJson);
        int[] liveCounts = MaterialLiveGridRemapper.CountResidentCellsByMaterial(_residentChunks, _runtimeMaterials.Count);
        ushort[] expectedTombstoneIds = DetermineTombstoneIds(stable.Definitions);
        string[] expectedTombstoneNames = new string[expectedTombstoneIds.Length];
        for (int i = 0; i < expectedTombstoneIds.Length; i++)
        {
            expectedTombstoneNames[i] = _runtimeMaterials.GetName(expectedTombstoneIds[i]);
        }

        MaterialGridRemapSnapshot gridBefore = MaterialLiveGridRemapper.CaptureReplacementState(
            _residentChunks,
            expectedTombstoneIds);
        EnsureAuthorityStateMatches(
            materialBefore,
            reactionsBefore,
            gridBefore,
            "材质/反应权威状态在构建提交计划期间发生变化。");

        return new PreparedApply(
            this,
            stable,
            assetReloads,
            materialBytes,
            reactionBytes,
            liveCounts,
            expectedTombstoneIds,
            expectedTombstoneNames,
            materialBefore,
            reactionsBefore,
            gridBefore,
            fallbackHitsBefore);
    }

    /// <summary>
    /// 材质/反应双文件与运行时状态的一次性原子提交计划。
    /// </summary>
    public sealed class PreparedApply : IDisposable
    {
        private readonly Lock _sync = new();
        private readonly FileMaterialReactionContentService _owner;
        private readonly MaterialContentStableReloadResult _stable;
        private readonly IReadOnlyList<MaterialAssetReloadRequest> _assetReloads;
        private readonly byte[] _materialBytes;
        private readonly byte[] _reactionBytes;
        private readonly int[] _liveCounts;
        private readonly ushort[] _expectedTombstoneIds;
        private readonly string[] _expectedTombstoneNames;
        private readonly MaterialTableSnapshot _materialBefore;
        private readonly ReactionTable _reactionsBefore;
        private readonly MaterialGridRemapSnapshot _gridBefore;
        private readonly long _fallbackHitsBefore;
        private MaterialReactionFileJournal? _journal;
        private bool _stateChanged;
        private PreparedApplyState _state;

        internal PreparedApply(
            FileMaterialReactionContentService owner,
            MaterialContentStableReloadResult stable,
            IReadOnlyList<MaterialAssetReloadRequest> assetReloads,
            byte[] materialBytes,
            byte[] reactionBytes,
            int[] liveCounts,
            ushort[] expectedTombstoneIds,
            string[] expectedTombstoneNames,
            MaterialTableSnapshot materialBefore,
            ReactionTable reactionsBefore,
            MaterialGridRemapSnapshot gridBefore,
            long fallbackHitsBefore)
        {
            _owner = owner;
            _stable = stable;
            _assetReloads = assetReloads;
            _materialBytes = materialBytes;
            _reactionBytes = reactionBytes;
            _liveCounts = liveCounts;
            _expectedTombstoneIds = expectedTombstoneIds;
            _expectedTombstoneNames = expectedTombstoneNames;
            _materialBefore = materialBefore;
            _reactionsBefore = reactionsBefore;
            _gridBefore = gridBefore;
            _fallbackHitsBefore = fallbackHitsBefore;
        }

        /// <summary>
        /// 在后台线程创建并 fsync 同卷 before/after journal；此方法不修改编辑器运行时状态或目标文件。
        /// </summary>
        /// <param name="cancellationToken">准备阶段取消令牌。</param>
        public void PrepareFiles(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                EnsureState(PreparedApplyState.Created, "文件只能准备一次。");
                try
                {
                    _journal = MaterialReactionFileJournal.Create(
                        _owner._materialsPath,
                        _owner._reactionsPath,
                        _materialBytes,
                        _reactionBytes,
                        cancellationToken);
                    _stateChanged = _journal.ContentChanged ||
                        _owner._runtimeMaterials.WouldReloadChange(_stable.Definitions) ||
                        !_reactionsBefore.ContentEquals(_stable.Reactions);
                    _state = PreparedApplyState.FilesPrepared;
                }
                catch
                {
                    _state = PreparedApplyState.Consumed;
                    throw;
                }
            }
        }

        /// <summary>后台准备完成后，判断提交是否会改变双文件或运行时 authority。</summary>
        public bool StateChanged
        {
            get
            {
                lock (_sync)
                {
                    EnsureState(PreparedApplyState.FilesPrepared, "必须先完成文件准备才能读取 change 状态。");
                    return _stateChanged;
                }
            }
        }

        /// <summary>
        /// 在引擎安全阶段重新验证 before-image，并一次提交运行时状态与双文件。
        /// </summary>
        /// <returns>稳定 ID 热重载、live-grid remap 与 journal 清理结果。</returns>
        public MaterialReactionApplyResult Commit()
        {
            lock (_sync)
            {
                EnsureState(PreparedApplyState.FilesPrepared, "必须先在后台完成文件准备。");
                _state = PreparedApplyState.Consumed;
                return CommitCore(
                    _journal ?? throw new InvalidOperationException("材质/反应 journal 未准备。"),
                    retainJournalForUndo: false,
                    out _);
            }
        }

        /// <summary>
        /// 提交热重载并保留双文件 journal 与 before/after-image，供统一 Editor Undo/Redo 使用。
        /// </summary>
        /// <returns>拥有 journal 生命周期的可逆提交。</returns>
        public CommittedApply CommitReversible()
        {
            lock (_sync)
            {
                EnsureState(PreparedApplyState.FilesPrepared, "必须先在后台完成文件准备。");
                if (!_stateChanged)
                {
                    throw new InvalidOperationException("no-change 材质/反应计划不需要可逆 journal。");
                }

                _state = PreparedApplyState.Consumed;
                _ = CommitCore(
                    _journal ?? throw new InvalidOperationException("材质/反应 journal 未准备。"),
                    retainJournalForUndo: true,
                    out CommittedApply? committed);
                return committed ?? throw new InvalidOperationException("可逆材质/反应提交未生成 Undo 状态。");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_sync)
            {
                if (_state == PreparedApplyState.Consumed)
                {
                    return;
                }

                _state = PreparedApplyState.Consumed;
                Exception? cleanupFailure = _journal?.TryCleanup();
                if (cleanupFailure is not null)
                {
                    throw new IOException(
                        $"未提交的材质/反应 journal 清理失败：{_journal!.JournalPath}",
                        cleanupFailure);
                }
            }
        }

        private MaterialReactionApplyResult CommitCore(
            MaterialReactionFileJournal journal,
            bool retainJournalForUndo,
            out CommittedApply? committed)
        {
            committed = null;
            bool runtimeMutationStarted = false;
            bool gridApplied = false;
            bool diskApplied = false;
            try
            {
                _owner.EnsureAuthorityStateMatches(
                    _materialBefore,
                    _reactionsBefore,
                    _gridBefore,
                    "材质/反应权威状态在后台准备期间发生变化；已拒绝旧计划。");

                if (!_stateChanged)
                {
                    journal.ValidateTransition(before: false);
                    MaterialReactionApplyResult noChange = new(
                        new MaterialReloadResult([], 0, _stable.Definitions.Length, 0),
                        [],
                        0,
                        CountPackedReactions(_stable.Definitions),
                        [],
                        stateChanged: false);
                    Exception? noChangeCleanupFailure = journal.TryCleanup();
                    return noChangeCleanupFailure is null
                        ? noChange
                        : new MaterialReactionApplyResult(
                            noChange.MaterialReload,
                            [],
                            0,
                            noChange.PackedReactionCount,
                            [],
                            stateChanged: false,
                            retainedJournalPath: journal.JournalPath,
                            cleanupError: noChangeCleanupFailure.Message);
                }

                runtimeMutationStarted = true;
                MaterialReloadResult reload = _owner._runtimeMaterials.ReloadStable(
                    _stable.Definitions,
                    _liveCounts,
                    _owner._fallbackMaterialId);
                if (!reload.TombstoneIds.AsSpan().SequenceEqual(_expectedTombstoneIds) ||
                    reload.FallbackReplacementCount != _gridBefore.CellCount)
                {
                    throw new InvalidOperationException("材质稳定重载结果与已验证提交计划不一致。");
                }

                int replaced = MaterialLiveGridRemapper.ApplyFallback(
                    _gridBefore,
                    _owner._fallbackMaterialId);
                gridApplied = true;
                _owner._applyMaterialHotTable?.Invoke(_owner._runtimeMaterials.Hot);
                _owner._applyReactions(_stable.Reactions);
                journal.ApplyAfter();
                diskApplied = true;
                _owner._assetReloadSink?.ReloadMaterialAssets(_assetReloads);
                _owner._counters?.AddMaterialRemapFallbackHits(replaced);

                MaterialTableSnapshot materialAfter = _owner._runtimeMaterials.CaptureState();
                ReactionTable reactionsAfter = _owner._captureReactions();
                long fallbackHitsAfter = _owner._counters?.MaterialRemapFallbackHits ?? 0;
                MaterialReactionApplyResult result = new(
                    reload,
                    _expectedTombstoneNames,
                    replaced,
                    CountPackedReactions(_stable.Definitions),
                    _assetReloads,
                    stateChanged: true,
                    retainedJournalPath: null,
                    cleanupError: null);
                if (retainJournalForUndo)
                {
                    committed = new CommittedApply(
                        _owner,
                        journal,
                        result,
                        _assetReloads,
                        _materialBefore,
                        materialAfter,
                        _reactionsBefore,
                        reactionsAfter,
                        _gridBefore,
                        _fallbackHitsBefore,
                        fallbackHitsAfter);
                    return result;
                }

                Exception? cleanupFailure = journal.TryCleanup();
                return cleanupFailure is null
                    ? result
                    : new MaterialReactionApplyResult(
                        reload,
                        _expectedTombstoneNames,
                        replaced,
                        CountPackedReactions(_stable.Definitions),
                        _assetReloads,
                        stateChanged: true,
                        retainedJournalPath: journal.JournalPath,
                        cleanupError: cleanupFailure.Message);
            }
            catch (Exception operationException)
            {
                List<Exception>? rollbackFailures = null;
                if (diskApplied)
                {
                    TryRollback(
                        journal.ApplyBefore,
                        operationException,
                        ref rollbackFailures);
                }

                if (gridApplied)
                {
                    TryRollback(
                        () => MaterialLiveGridRemapper.RestoreReplacementState(_gridBefore),
                        operationException,
                        ref rollbackFailures);
                }

                if (runtimeMutationStarted)
                {
                    TryRollback(
                        () =>
                        {
                            _owner._runtimeMaterials.RestoreState(_materialBefore);
                            _owner._applyMaterialHotTable?.Invoke(_owner._runtimeMaterials.Hot);
                        },
                        operationException,
                        ref rollbackFailures);
                    TryRollback(
                        () => _owner._applyReactions(_reactionsBefore),
                        operationException,
                        ref rollbackFailures);
                }

                if (_owner._counters is not null)
                {
                    _owner._counters.MaterialRemapFallbackHits = _fallbackHitsBefore;
                }

                Exception? cleanupFailure = journal.TryCleanup();
                if (cleanupFailure is not null)
                {
                    (rollbackFailures ??= [operationException]).Add(cleanupFailure);
                }

                if (rollbackFailures is not null)
                {
                    throw new AggregateException(
                        $"材质/反应热重载失败，且 before-image 回滚或 journal 清理失败；journal={journal.JournalPath}",
                        rollbackFailures);
                }

                throw;
            }
        }

        private void EnsureState(PreparedApplyState expected, string message)
        {
            if (_state != expected)
            {
                throw new InvalidOperationException(message);
            }
        }

        private enum PreparedApplyState : byte
        {
            Created,
            FilesPrepared,
            Consumed,
        }
    }

    /// <summary>拥有材质/反应热重载 before/after-image 与双文件 journal 的可逆提交。</summary>
    public sealed class CommittedApply : IDisposable
    {
        private readonly Lock _sync = new();
        private readonly FileMaterialReactionContentService _owner;
        private readonly MaterialReactionFileJournal _journal;
        private readonly IReadOnlyList<MaterialAssetReloadRequest> _assetReloads;
        private readonly MaterialTableSnapshot _materialBefore;
        private readonly MaterialTableSnapshot _materialAfter;
        private readonly ReactionTable _reactionsBefore;
        private readonly ReactionTable _reactionsAfter;
        private readonly MaterialGridRemapSnapshot _gridBefore;
        private readonly long _fallbackHitsBefore;
        private readonly long _fallbackHitsAfter;
        private bool _isAfter = true;
        private bool _disposed;

        internal CommittedApply(
            FileMaterialReactionContentService owner,
            MaterialReactionFileJournal journal,
            MaterialReactionApplyResult result,
            IReadOnlyList<MaterialAssetReloadRequest> assetReloads,
            MaterialTableSnapshot materialBefore,
            MaterialTableSnapshot materialAfter,
            ReactionTable reactionsBefore,
            ReactionTable reactionsAfter,
            MaterialGridRemapSnapshot gridBefore,
            long fallbackHitsBefore,
            long fallbackHitsAfter)
        {
            _owner = owner;
            _journal = journal;
            Result = result;
            _assetReloads = assetReloads;
            _materialBefore = materialBefore;
            _materialAfter = materialAfter;
            _reactionsBefore = reactionsBefore;
            _reactionsAfter = reactionsAfter;
            _gridBefore = gridBefore;
            _fallbackHitsBefore = fallbackHitsBefore;
            _fallbackHitsAfter = fallbackHitsAfter;
        }

        /// <summary>首次提交的结构化结果。</summary>
        public MaterialReactionApplyResult Result { get; }

        /// <summary>恢复双文件、MaterialTable、ReactionTable、live grid 与计数器 before-image。</summary>
        public void Undo()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_isAfter)
                {
                    throw new InvalidOperationException("材质/反应热重载已经处于 before 状态。");
                }

                Transition(toAfter: false);
            }
        }

        /// <summary>重新应用已验证的双文件与运行时 after-image。</summary>
        public void Redo()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_isAfter)
                {
                    throw new InvalidOperationException("材质/反应热重载已经处于 after 状态。");
                }

                Transition(toAfter: true);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                Exception? cleanupFailure = _journal.TryCleanup();
                if (cleanupFailure is not null)
                {
                    throw new IOException(
                        $"材质/反应 Undo journal 清理失败：{_journal.JournalPath}",
                        cleanupFailure);
                }

                _disposed = true;
            }
        }

        private void Transition(bool toAfter)
        {
            EnsureSourceAuthority(toAfter);
            _journal.ValidateTransition(before: !toAfter);
            bool runtimeApplied = false;
            bool gridApplied = false;
            bool diskApplied = false;
            bool assetReloadInvoked = false;
            try
            {
                runtimeApplied = true;
                ApplyRuntimeState(toAfter);
                gridApplied = true;
                ApplyGridState(toAfter);
                if (toAfter)
                {
                    _journal.ApplyAfter();
                }
                else
                {
                    _journal.ApplyBefore();
                }

                diskApplied = true;
                assetReloadInvoked = _owner._assetReloadSink is not null;
                _owner._assetReloadSink?.ReloadMaterialAssets(_assetReloads);
                if (_owner._counters is not null)
                {
                    _owner._counters.MaterialRemapFallbackHits = toAfter
                        ? _fallbackHitsAfter
                        : _fallbackHitsBefore;
                }

                _isAfter = toAfter;
            }
            catch (Exception operationException)
            {
                List<Exception>? rollbackFailures = null;
                if (diskApplied)
                {
                    TryRollback(
                        toAfter ? _journal.ApplyBefore : _journal.ApplyAfter,
                        operationException,
                        ref rollbackFailures);
                }

                if (gridApplied)
                {
                    TryRollback(
                        () => ApplyGridState(!toAfter),
                        operationException,
                        ref rollbackFailures);
                }

                if (runtimeApplied)
                {
                    TryRollback(
                        () => ApplyRuntimeState(!toAfter),
                        operationException,
                        ref rollbackFailures);
                }

                if (_owner._counters is not null)
                {
                    _owner._counters.MaterialRemapFallbackHits = toAfter
                        ? _fallbackHitsBefore
                        : _fallbackHitsAfter;
                }

                if (assetReloadInvoked)
                {
                    TryRollback(
                        () => _owner._assetReloadSink!.ReloadMaterialAssets(_assetReloads),
                        operationException,
                        ref rollbackFailures);
                }

                if (rollbackFailures is not null)
                {
                    throw new AggregateException(
                        $"材质/反应 {(toAfter ? "Redo" : "Undo")} 失败，且 before/after-image 回滚不完整；journal={_journal.JournalPath}",
                        rollbackFailures);
                }

                throw;
            }
        }

        private void EnsureSourceAuthority(bool toAfter)
        {
            bool sourceIsAfter = !toAfter;
            MaterialTableSnapshot material = sourceIsAfter ? _materialAfter : _materialBefore;
            ReactionTable reactions = sourceIsAfter ? _reactionsAfter : _reactionsBefore;
            bool gridMatches = sourceIsAfter
                ? MaterialLiveGridRemapper.FallbackStateEquals(
                    _gridBefore,
                    _owner._fallbackMaterialId)
                : MaterialLiveGridRemapper.ReplacementStateEquals(_gridBefore);
            long expectedFallbackHits = sourceIsAfter ? _fallbackHitsAfter : _fallbackHitsBefore;
            if (!_owner._runtimeMaterials.StateEquals(material) ||
                !ReferenceEquals(_owner._captureReactions(), reactions) ||
                !gridMatches ||
                (_owner._counters is not null &&
                 _owner._counters.MaterialRemapFallbackHits != expectedFallbackHits))
            {
                throw new InvalidOperationException(
                    $"材质/反应 {(toAfter ? "Redo" : "Undo")} source authority 已变化，拒绝覆盖较新的状态。");
            }
        }

        private void ApplyRuntimeState(bool after)
        {
            _owner._runtimeMaterials.RestoreState(after ? _materialAfter : _materialBefore);
            _owner._applyMaterialHotTable?.Invoke(_owner._runtimeMaterials.Hot);
            _owner._applyReactions(after ? _reactionsAfter : _reactionsBefore);
        }

        private void ApplyGridState(bool after)
        {
            if (after)
            {
                _ = MaterialLiveGridRemapper.ApplyFallback(
                    _gridBefore,
                    _owner._fallbackMaterialId);
            }
            else
            {
                MaterialLiveGridRemapper.RestoreReplacementState(_gridBefore);
            }
        }
    }

    private static MaterialDocumentJson ReadMaterialDocument(ReadOnlySpan<byte> json)
    {
        try
        {
            if (JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.MaterialDocumentJson) is { } document)
            {
                return document;
            }
        }
        catch (JsonException)
        {
        }

        MaterialJson[]? array = JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.MaterialJsonArray);
        return array is null
            ? throw new JsonException("materials.json 为空或格式无效。")
            : new MaterialDocumentJson { Materials = array };
    }

    private static ReactionDocumentJson ReadReactionDocument(ReadOnlySpan<byte> json)
    {
        try
        {
            if (JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.ReactionDocumentJson) is { } document)
            {
                return document;
            }
        }
        catch (JsonException)
        {
        }

        ReactionJson[]? array = JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.ReactionJsonArray);
        return array is null
            ? throw new JsonException("reactions.json 为空或格式无效。")
            : new ReactionDocumentJson { Reactions = array };
    }

    private static byte[] ReadSourceFile(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRegularSource(path);
        FileInfo before = new(path);
        if (before.Length > MaximumSourceFileBytes)
        {
            throw new InvalidDataException($"材质/反应文件超过 {MaximumSourceFileBytes} 字节：{path}");
        }

        byte[] bytes = new byte[checked((int)before.Length)];
        using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read = 0;
            while (read < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0)
                {
                    throw new EndOfStreamException($"材质/反应文件在读取时提前结束：{path}");
                }

                read += count;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        FileInfo after = new(path);
        return after.Exists &&
            after.Length == before.Length &&
            after.LastWriteTimeUtc == before.LastWriteTimeUtc
                ? bytes
                : throw new IOException($"读取材质/反应文件时发生变化：{path}");
    }

    private static void EnsureRegularSource(string path)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("材质/反应文件不存在。", fullPath);
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"材质/反应文件不能是 reparse point：{fullPath}");
        }

        DirectoryInfo? directory = info.Directory;
        while (directory is not null)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"材质/反应目录不能是 reparse point：{directory.FullName}");
            }

            directory = directory.Parent;
        }
    }

    private static void TryRollback(
        Action rollback,
        Exception operationException,
        ref List<Exception>? failures)
    {
        try
        {
            rollback();
        }
        catch (Exception rollbackException)
        {
            (failures ??= [operationException]).Add(rollbackException);
        }
    }

    private static int CountPackedReactions(ReadOnlySpan<MaterialDef> definitions)
    {
        int count = 0;
        for (int i = 0; i < definitions.Length; i++)
        {
            count += definitions[i].ReactionCount;
        }

        return count;
    }

    private ushort[] DetermineTombstoneIds(ReadOnlySpan<MaterialDef> newDefinitions)
    {
        HashSet<string> incomingNames = new(newDefinitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < newDefinitions.Length; i++)
        {
            _ = incomingNames.Add(newDefinitions[i].Name);
        }

        (ushort Id, string Name)[] current = _runtimeMaterials.BuildIdNameTable();
        List<ushort> tombstoneIds = [];
        for (int i = 0; i < current.Length; i++)
        {
            if (!incomingNames.Contains(current[i].Name))
            {
                tombstoneIds.Add(current[i].Id);
            }
        }

        return [.. tombstoneIds];
    }

    private void EnsureAuthorityStateMatches(
        MaterialTableSnapshot materialSnapshot,
        ReactionTable reactions,
        MaterialGridRemapSnapshot gridSnapshot,
        string message)
    {
        if (!_runtimeMaterials.StateEquals(materialSnapshot) ||
            !ReferenceEquals(_captureReactions(), reactions) ||
            !MaterialLiveGridRemapper.ReplacementStateEquals(gridSnapshot))
        {
            throw new InvalidOperationException(message);
        }
    }

    private IReadOnlyList<MaterialAssetReloadRequest> DetectAssetReloads(ReadOnlySpan<MaterialDef> newDefinitions)
    {
        List<MaterialAssetReloadRequest> requests = [];
        for (int i = 0; i < newDefinitions.Length; i++)
        {
            MaterialDef next = newDefinitions[i];
            if (!_runtimeMaterials.TryGetId(next.Name, out ushort id))
            {
                continue;
            }

            ref readonly MaterialDef previous = ref _runtimeMaterials.Get(id);
            bool textureChanged = previous.TextureId != next.TextureId;
            bool audioChanged = previous.AudioCues != next.AudioCues;
            if (textureChanged || audioChanged)
            {
                requests.Add(new MaterialAssetReloadRequest(next.Name, id, textureChanged, audioChanged));
            }
        }

        return requests;
    }
}

internal sealed class MaterialReactionFileJournal
{
    private const long MaximumFileBytes = 16L * 1024 * 1024;
    private readonly JournalEntry[] _entries;
    private bool _isBefore = true;
    private bool _preserve;

    private MaterialReactionFileJournal(string journalPath, JournalEntry[] entries)
    {
        JournalPath = journalPath;
        _entries = entries;
    }

    internal string JournalPath { get; }

    internal bool ContentChanged
    {
        get
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].BeforeIdentity != _entries[i].AfterIdentity)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static MaterialReactionFileJournal Create(
        string materialsPath,
        string reactionsPath,
        ReadOnlySpan<byte> materialBytes,
        ReadOnlySpan<byte> reactionBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string materialTarget = Path.GetFullPath(materialsPath);
        string reactionTarget = Path.GetFullPath(reactionsPath);
        string materialDirectory = Path.GetDirectoryName(materialTarget) ??
            throw new InvalidOperationException("materials.json 缺少父目录。");
        string reactionDirectory = Path.GetDirectoryName(reactionTarget) ??
            throw new InvalidOperationException("reactions.json 缺少父目录。");
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(materialDirectory, reactionDirectory, comparison))
        {
            throw new InvalidOperationException("materials.json 与 reactions.json 必须位于同一目录才能原子提交。");
        }

        if (string.Equals(materialTarget, reactionTarget, comparison))
        {
            throw new InvalidOperationException("materials.json 与 reactions.json 不能指向同一文件。");
        }

        EnsureRegularTarget(materialTarget);
        EnsureRegularTarget(reactionTarget);
        cancellationToken.ThrowIfCancellationRequested();
        string journalRoot = PrepareJournalRoot(materialDirectory);
        string journalPath = Path.Combine(journalRoot, Guid.NewGuid().ToString("N"));
        string beforeRoot = Path.Combine(journalPath, "before");
        string afterRoot = Path.Combine(journalPath, "after");
        _ = Directory.CreateDirectory(beforeRoot);
        _ = Directory.CreateDirectory(afterRoot);
        try
        {
            JournalEntry[] entries =
            [
                CreateEntry(0, materialTarget, beforeRoot, afterRoot, materialBytes, cancellationToken),
                CreateEntry(1, reactionTarget, beforeRoot, afterRoot, reactionBytes, cancellationToken),
            ];
            cancellationToken.ThrowIfCancellationRequested();
            return new MaterialReactionFileJournal(journalPath, entries);
        }
        catch
        {
            if (Directory.Exists(journalPath))
            {
                Directory.Delete(journalPath, recursive: true);
            }

            throw;
        }
    }

    internal void ApplyAfter()
    {
        Transition(before: false);
    }

    internal void ApplyBefore()
    {
        Transition(before: true);
    }

    internal void ValidateTransition(bool before)
    {
        if (_isBefore == before)
        {
            return;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            FileIdentity expected = _isBefore ? _entries[i].BeforeIdentity : _entries[i].AfterIdentity;
            if (CaptureIdentity(_entries[i].TargetPath) != expected)
            {
                throw new IOException($"材质/反应目标在提交期间被外部修改：{_entries[i].TargetPath}");
            }

            string targetArchive = before
                ? _entries[i].BeforeArchivePath
                : _entries[i].AfterArchivePath;
            if (!File.Exists(targetArchive))
            {
                throw new IOException($"材质/反应 target archive 缺失：{targetArchive}");
            }
        }
    }

    internal Exception? TryCleanup()
    {
        if (_preserve)
        {
            return new IOException("双文件 journal 因回滚失败被保留。");
        }

        try
        {
            if (Directory.Exists(JournalPath))
            {
                Directory.Delete(JournalPath, recursive: true);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception;
        }
    }

    private void Transition(bool before)
    {
        if (_isBefore == before)
        {
            return;
        }

        ValidateTransition(before);

        int movedToSource = 0;
        int movedToTarget = 0;
        try
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                string sourceArchive = _isBefore
                    ? _entries[i].BeforeArchivePath
                    : _entries[i].AfterArchivePath;
                if (File.Exists(sourceArchive))
                {
                    throw new IOException($"材质/反应 source archive 已存在：{sourceArchive}");
                }

                File.Move(_entries[i].TargetPath, sourceArchive);
                movedToSource++;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                string targetArchive = before
                    ? _entries[i].BeforeArchivePath
                    : _entries[i].AfterArchivePath;
                if (!File.Exists(targetArchive))
                {
                    throw new IOException($"材质/反应 target archive 缺失：{targetArchive}");
                }

                File.Move(targetArchive, _entries[i].TargetPath);
                movedToTarget++;
            }

            _isBefore = before;
        }
        catch (Exception operationException)
        {
            List<Exception>? rollbackFailures = null;
            for (int i = movedToTarget - 1; i >= 0; i--)
            {
                string targetArchive = before
                    ? _entries[i].BeforeArchivePath
                    : _entries[i].AfterArchivePath;
                TryMove(
                    _entries[i].TargetPath,
                    targetArchive,
                    operationException,
                    ref rollbackFailures);
            }

            for (int i = movedToSource - 1; i >= 0; i--)
            {
                string sourceArchive = _isBefore
                    ? _entries[i].BeforeArchivePath
                    : _entries[i].AfterArchivePath;
                TryMove(
                    sourceArchive,
                    _entries[i].TargetPath,
                    operationException,
                    ref rollbackFailures);
            }

            if (rollbackFailures is not null)
            {
                _preserve = true;
                throw new AggregateException(
                    $"材质/反应双文件切换失败，且回滚失败；journal={JournalPath}",
                    rollbackFailures);
            }

            throw;
        }
    }

    private static JournalEntry CreateEntry(
        int index,
        string targetPath,
        string beforeRoot,
        string afterRoot,
        ReadOnlySpan<byte> afterBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (afterBytes.Length > MaximumFileBytes)
        {
            throw new InvalidDataException($"材质/反应文件超过 {MaximumFileBytes} 字节：{targetPath}");
        }

        string archiveName = $"{index}-{Path.GetFileName(targetPath)}";
        string afterArchive = Path.Combine(afterRoot, archiveName);
        WritePrepared(afterArchive, afterBytes, cancellationToken);
        return new JournalEntry(
            targetPath,
            Path.Combine(beforeRoot, archiveName),
            afterArchive,
            CaptureIdentity(targetPath, cancellationToken),
            IdentityFromBytes(afterBytes));
    }

    private static void WritePrepared(
        string path,
        ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken)
    {
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        const int writeBlockBytes = 64 * 1024;
        int written = 0;
        while (written < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(writeBlockBytes, bytes.Length - written);
            stream.Write(bytes.Slice(written, count));
            written += count;
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Flush(flushToDisk: true);
    }

    private static FileIdentity CaptureIdentity(string path)
    {
        return CaptureIdentity(path, CancellationToken.None);
    }

    private static FileIdentity CaptureIdentity(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRegularTarget(path);
        FileInfo before = new(path);
        if (before.Length > MaximumFileBytes)
        {
            throw new InvalidDataException($"材质/反应文件超过 {MaximumFileBytes} 字节：{path}");
        }

        byte[] bytes = new byte[checked((int)before.Length)];
        using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read = 0;
            while (read < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0)
                {
                    throw new EndOfStreamException($"材质/反应文件在读取时提前结束：{path}");
                }

                read += count;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        FileInfo after = new(path);
        return !after.Exists || after.Length != before.Length ||
            after.LastWriteTimeUtc != before.LastWriteTimeUtc
                ? throw new IOException($"读取材质/反应文件时发生变化：{path}")
                : IdentityFromBytes(bytes);
    }

    private static FileIdentity IdentityFromBytes(ReadOnlySpan<byte> bytes)
    {
        return new FileIdentity(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static void EnsureRegularTarget(string path)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("材质/反应文件不存在。", fullPath);
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"材质/反应文件不能是 reparse point：{fullPath}");
        }

        DirectoryInfo? directory = info.Directory;
        while (directory is not null)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"材质/反应目录不能是 reparse point：{directory.FullName}");
            }

            directory = directory.Parent;
        }
    }

    private static string PrepareJournalRoot(string contentDirectory)
    {
        string metadataRoot = Path.Combine(contentDirectory, ".pixelengine");
        string journalRoot = Path.Combine(metadataRoot, "material-reaction-journals");
        EnsureOrCreateRegularDirectory(metadataRoot);
        EnsureOrCreateRegularDirectory(journalRoot);
        return journalRoot;
    }

    private static void EnsureOrCreateRegularDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"材质/反应 journal 目录不能是 reparse point：{path}");
            }

            return;
        }

        _ = Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"材质/反应 journal 目录不能是 reparse point：{path}");
        }
    }

    private static void TryMove(
        string source,
        string destination,
        Exception operationException,
        ref List<Exception>? failures)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
        catch (Exception rollbackException)
        {
            (failures ??= [operationException]).Add(rollbackException);
        }
    }

    private sealed record JournalEntry(
        string TargetPath,
        string BeforeArchivePath,
        string AfterArchivePath,
        FileIdentity BeforeIdentity,
        FileIdentity AfterIdentity);

    private readonly record struct FileIdentity(long Length, string Sha256);
}

#pragma warning restore IDE0290, IDE0031
