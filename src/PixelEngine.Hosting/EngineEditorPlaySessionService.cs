using PixelEngine.Editor;

namespace PixelEngine.Hosting;

/// <summary>
/// 基于 Engine 执行模式的 Editor Play session 服务。
/// </summary>
/// <param name="engine">运行时 Engine。</param>
/// <param name="snapshotStore">可选临时 Play 快照后端。</param>
public sealed class EngineEditorPlaySessionService(Engine engine, IEditorPlaySnapshotStore? snapshotStore = null) : IEditorPlaySessionService
{
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IEditorPlaySnapshotStore? _snapshotStore = snapshotStore;
    private EditorPlaySource _source = EditorPlaySource.CurrentState;
    private bool _temporarySnapshotActive;
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 捕获当前 Editor 运行模式状态。
    /// </summary>
    /// <returns>当前模式、Play 来源与临时快照状态。</returns>
    public EditorPlaySessionSnapshot Capture()
    {
        return new EditorPlaySessionSnapshot(
            _engine.Mode == EngineExecutionMode.Play ? EditorMode.Play : EditorMode.Edit,
            _source,
            _temporarySnapshotActive,
            _statusMessage);
    }

    /// <summary>
    /// 以当前 live 世界进入 Play 模式。
    /// </summary>
    /// <returns>模式切换结果。</returns>
    public EditorPlaySessionResult EnterPlayCurrent()
    {
        _engine.EnterPlayMode();
        _source = EditorPlaySource.CurrentState;
        _temporarySnapshotActive = false;
        _statusMessage = "以当前世界态进入 Play。";
        return Succeeded(_statusMessage);
    }

    /// <summary>
    /// 保存临时快照后进入 Play 模式；没有快照后端时返回失败且不切换模式。
    /// </summary>
    /// <returns>模式切换结果。</returns>
    public EditorPlaySessionResult EnterPlayTemporary()
    {
        if (_snapshotStore is null)
        {
            _statusMessage = "临时 Play 缺少快照后端。";
            return Failed(_statusMessage);
        }

        SaveLoadOperationResult save = _snapshotStore.SaveTemporarySnapshot();
        if (!save.Success)
        {
            _statusMessage = save.Message;
            return Failed(_statusMessage);
        }

        _engine.EnterPlayMode();
        _source = EditorPlaySource.TemporarySnapshot;
        _temporarySnapshotActive = true;
        _statusMessage = save.Message;
        return Succeeded(_statusMessage);
    }

    /// <summary>
    /// 返回 Edit 模式；若当前是临时 Play，则先恢复进入 Play 前的快照。
    /// </summary>
    /// <returns>模式切换结果。</returns>
    public EditorPlaySessionResult ExitPlay()
    {
        _engine.EndScriptPlaySession();
        if (_temporarySnapshotActive)
        {
            SaveLoadOperationResult restore = _snapshotStore?.RestoreTemporarySnapshot()
                ?? new SaveLoadOperationResult(false, "临时 Play 缺少快照后端。", null, null);
            if (!restore.Success)
            {
                _statusMessage = restore.Message;
                return Failed(_statusMessage);
            }

            _temporarySnapshotActive = false;
            _statusMessage = restore.Message;
        }
        else
        {
            _statusMessage = "返回 Edit。";
        }

        _engine.EnterEditMode();
        _source = EditorPlaySource.CurrentState;
        return Succeeded(_statusMessage);
    }

    private EditorPlaySessionResult Succeeded(string message)
    {
        return new EditorPlaySessionResult(true, Capture(), message);
    }

    private EditorPlaySessionResult Failed(string message)
    {
        return new EditorPlaySessionResult(false, Capture(), message);
    }
}
