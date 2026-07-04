using PixelEngine.World;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 对外呈现的编辑/运行模式。
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
/// Editor Play session 当前状态。
/// </summary>
/// <param name="Mode">当前 Editor 模式。</param>
/// <param name="Source">当前 Play 来源。</param>
/// <param name="TemporarySnapshotActive">是否存在待恢复的临时快照。</param>
/// <param name="StatusMessage">最近一次状态说明。</param>
public readonly record struct EditorPlaySessionSnapshot(
    EditorMode Mode,
    EditorPlaySource Source,
    bool TemporarySnapshotActive,
    string StatusMessage)
{
    /// <summary>
    /// 编辑工具是否应该响应世界输入。
    /// </summary>
    public bool EditorToolsEnabled => Mode == EditorMode.Edit;

    /// <summary>
    /// 游戏/脚本输入是否应该响应世界输入。
    /// </summary>
    public bool GameInputEnabled => Mode == EditorMode.Play;
}

/// <summary>
/// Editor Play session 切换结果。
/// </summary>
/// <param name="Succeeded">切换是否成功。</param>
/// <param name="Snapshot">切换后的状态。</param>
/// <param name="Message">结果说明。</param>
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
/// Editor 运行模式服务。
/// </summary>
public interface IEditorPlaySessionService
{
    /// <summary>
    /// 捕获当前模式状态。
    /// </summary>
    EditorPlaySessionSnapshot Capture();

    /// <summary>
    /// 以当前 live 世界进入 Play。
    /// </summary>
    EditorPlaySessionResult EnterPlayCurrent();

    /// <summary>
    /// 保存临时快照后进入 Play，退出时恢复。
    /// </summary>
    EditorPlaySessionResult EnterPlayTemporary();

    /// <summary>
    /// 返回 Edit；若当前为临时 Play，则先恢复临时快照。
    /// </summary>
    EditorPlaySessionResult ExitPlay();
}

/// <summary>
/// 基于存读档服务实现的临时 Play 快照后端。
/// </summary>
/// <param name="saveLoad">存读档服务。</param>
/// <param name="slotId">临时快照槽位。</param>
public sealed class SaveLoadPlaySnapshotStore(ISaveLoadService saveLoad, string slotId = "__editor_play_temp") : IEditorPlaySnapshotStore
{
    private readonly ISaveLoadService _saveLoad = saveLoad ?? throw new ArgumentNullException(nameof(saveLoad));
    private readonly string _slotId = string.IsNullOrWhiteSpace(slotId) ? "__editor_play_temp" : slotId;

    /// <inheritdoc />
    public SaveLoadOperationResult SaveTemporarySnapshot()
    {
        return _saveLoad.Save(_slotId);
    }

    /// <inheritdoc />
    public SaveLoadOperationResult RestoreTemporarySnapshot()
    {
        return _saveLoad.Load(_slotId);
    }
}
