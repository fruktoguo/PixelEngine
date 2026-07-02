using PixelEngine.Editor;
using PixelEngine.Scripting;
using PixelEngine.World;

namespace PixelEngine.Hosting;

/// <summary>
/// 基于 Engine resident world 快照的 Editor 临时 Play 快照后端。
/// </summary>
/// <param name="engine">运行时 Engine。</param>
public sealed class EngineWorldSnapshotStore(Engine engine) : IEditorPlaySnapshotStore, IDisposable
{
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private EngineWorldSnapshot? _snapshot;
    private ScriptPlaySessionSnapshot? _scriptSnapshot;

    /// <summary>
    /// 捕获当前 world 状态，覆盖旧的临时快照。
    /// </summary>
    /// <returns>保存结果。</returns>
    public SaveLoadOperationResult SaveTemporarySnapshot()
    {
        try
        {
            _snapshot?.Dispose();
            _snapshot = _engine.CaptureWorldSnapshot();
            _scriptSnapshot = _engine.CaptureScriptPlaySessionSnapshot();
            return new SaveLoadOperationResult(
                true,
                $"已保存临时 Play 快照：tick={_snapshot.GameTimeTicks}。",
                CreateSlotInfo(_snapshot, chunkCount: 0),
                null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SaveLoadOperationResult(false, exception.Message, null, null);
        }
    }

    /// <summary>
    /// 恢复最近一次临时快照。
    /// </summary>
    /// <returns>恢复结果。</returns>
    public SaveLoadOperationResult RestoreTemporarySnapshot()
    {
        if (_snapshot is null)
        {
            return new SaveLoadOperationResult(false, "没有可恢复的临时 Play 快照。", null, null);
        }

        try
        {
            WorldLoadResult result = _engine.RestoreWorldSnapshot(_snapshot);
            _engine.RestoreScriptPlaySessionSnapshot(_scriptSnapshot);
            string path = _snapshot.DirectoryPath;
            _snapshot.Dispose();
            _snapshot = null;
            _scriptSnapshot = null;
            return new SaveLoadOperationResult(
                true,
                $"已恢复临时 Play 快照：tick={result.GameTimeTicks}, chunks={result.LoadedChunkCount}。",
                new SaveSlotInfo(
                    "__editor_play_temp",
                    path,
                    DateTimeOffset.UtcNow,
                    0,
                    result.WorldSeed,
                    result.GameTimeTicks,
                    result.LoadedChunkCount),
                result);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SaveLoadOperationResult(false, exception.Message, null, null);
        }
    }

    /// <summary>
    /// 删除仍未恢复的临时快照。
    /// </summary>
    public void Dispose()
    {
        _snapshot?.Dispose();
        _snapshot = null;
        _scriptSnapshot = null;
    }

    private static SaveSlotInfo CreateSlotInfo(EngineWorldSnapshot snapshot, int chunkCount)
    {
        return new SaveSlotInfo(
            "__editor_play_temp",
            snapshot.DirectoryPath,
            DateTimeOffset.UtcNow,
            0,
            snapshot.WorldSeed,
            snapshot.GameTimeTicks,
            chunkCount);
    }
}
