using PixelEngine.World;

namespace PixelEngine.Hosting;

/// <summary>
/// 对外呈现的编辑/运行模式。
/// </summary>
public enum EditorMode
{
    /// <summary>
    /// 编辑模式：编辑工具可接收输入，sim 可暂停或单步。
    /// </summary>
    Edit,

    /// <summary>
    /// 运行模式：输入交给游戏/脚本，编辑工具让位。
    /// </summary>
    Play,

    /// <summary>
    /// 暂停的 Play session：保留运行时世界，可继续或单步。
    /// </summary>
    Paused,
}

/// <summary>
/// 进入 Play 时使用的世界来源。
/// </summary>
public enum EditorPlaySource
{
    /// <summary>
    /// 直接使用当前 live 世界运行。
    /// </summary>
    CurrentState,

    /// <summary>
    /// 进入 Play 前保存临时快照，退出 Play 时恢复。
    /// </summary>
    TemporarySnapshot,
}

/// <summary>
/// Play session 当前状态。
/// </summary>
public readonly record struct EditorPlaySessionSnapshot(
    EditorMode Mode,
    EditorPlaySource Source,
    bool TemporarySnapshotActive,
    string StatusMessage);

/// <summary>
/// Play session 切换结果。
/// </summary>
public readonly record struct EditorPlaySessionResult(
    bool Succeeded,
    EditorPlaySessionSnapshot Snapshot,
    string Message);

/// <summary>
/// 为临时 Play 提供保存/恢复快照的后端。
/// </summary>
public interface IEditorPlaySnapshotStore
{
    /// <summary>
    /// 保存进入 Play 前的临时快照。
    /// </summary>
    SaveLoadOperationResult SaveTemporarySnapshot();

    /// <summary>
    /// 恢复进入 Play 前保存的临时快照。
    /// </summary>
    SaveLoadOperationResult RestoreTemporarySnapshot();
}

/// <summary>
/// 基于 Engine 执行模式的 Play session 服务。
/// </summary>
/// <param name="engine">运行时 Engine。</param>
/// <param name="snapshotStore">可选临时 Play 快照后端。</param>
public sealed class EngineEditorPlaySessionService(Engine engine, IEditorPlaySnapshotStore? snapshotStore = null)
{
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IEditorPlaySnapshotStore? _snapshotStore = snapshotStore;
    private EditorPlaySource _source = EditorPlaySource.CurrentState;
    private bool _temporarySnapshotActive;
    private bool _sessionActive;
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 捕获当前 Editor 运行模式状态。
    /// </summary>
    /// <returns>当前模式、Play 来源与临时快照状态。</returns>
    public EditorPlaySessionSnapshot Capture()
    {
        EditorMode mode = _engine.Mode == EngineExecutionMode.Play
            ? EditorMode.Play
            : _engine.Mode == EngineExecutionMode.Paused
                ? EditorMode.Paused
                : EditorMode.Edit;
        return new EditorPlaySessionSnapshot(
            mode,
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
        if (_sessionActive)
        {
            _statusMessage = "Play session 已经处于运行或暂停状态。";
            return Failed(_statusMessage);
        }

        // 直接 Play：不捕获快照，退出时保留运行期对世界造成的修改。
        _engine.EnterPlayMode();
        _source = EditorPlaySource.CurrentState;
        _temporarySnapshotActive = false;
        _sessionActive = true;
        _statusMessage = "以当前世界态进入 Play。";
        return Succeeded(_statusMessage);
    }

    /// <summary>
    /// 保存临时快照后进入 Play 模式；没有快照后端时返回失败且不切换模式。
    /// </summary>
    /// <returns>模式切换结果。</returns>
    public EditorPlaySessionResult EnterPlayTemporary()
    {
        if (_sessionActive)
        {
            _statusMessage = "Play session 已经处于运行或暂停状态。";
            return Failed(_statusMessage);
        }

        // 临时 Play：进入前保存世界快照，退出 Edit 时回滚到进入 Play 前的编辑态。
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
        _sessionActive = true;
        _statusMessage = save.Message;
        return Succeeded(_statusMessage);
    }

    /// <summary>
    /// 暂停当前 Play session，而不恢复临时快照。
    /// </summary>
    public EditorPlaySessionResult PausePlay()
    {
        if (_engine.Mode == EngineExecutionMode.Paused)
        {
            _statusMessage = "Play session 已暂停。";
            return Succeeded(_statusMessage);
        }

        if (_engine.Mode != EngineExecutionMode.Play)
        {
            _statusMessage = "当前没有可暂停的 Play session。";
            return Failed(_statusMessage);
        }

        _engine.EnterPauseMode();
        _statusMessage = "Play session 已暂停，可继续或单步。";
        return Succeeded(_statusMessage);
    }

    /// <summary>
    /// 继续已暂停的 Play session。
    /// </summary>
    public EditorPlaySessionResult ResumePlay()
    {
        if (_engine.Mode != EngineExecutionMode.Paused)
        {
            _statusMessage = "当前没有已暂停的 Play session。";
            return Failed(_statusMessage);
        }

        _engine.EnterPlayMode();
        _statusMessage = "Play session 已继续。";
        return Succeeded(_statusMessage);
    }

    /// <summary>
    /// 返回 Edit 模式；若当前是临时 Play，则先恢复进入 Play 前的快照。
    /// </summary>
    /// <returns>模式切换结果。</returns>
    public EditorPlaySessionResult ExitPlay()
    {
        // 先结束脚本 Play session，再按需恢复快照，最后切回 Edit 让编辑工具重新接管输入。
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
        _sessionActive = false;
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
