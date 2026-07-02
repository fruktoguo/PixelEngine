namespace PixelEngine.Hosting;

/// <summary>
/// Engine 捕获的临时世界快照；用于 Play/Edit 回滚或重开关卡的 world 状态恢复。
/// </summary>
public sealed class EngineWorldSnapshot : IDisposable
{
    private bool _disposed;

    internal EngineWorldSnapshot(string directoryPath, long gameTimeTicks, ulong worldSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = directoryPath;
        GameTimeTicks = gameTimeTicks;
        WorldSeed = worldSeed;
    }

    /// <summary>
    /// 快照保存目录；由 Engine 创建并在 Dispose 时删除。
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// 快照对应的游戏 tick。
    /// </summary>
    public long GameTimeTicks { get; }

    /// <summary>
    /// 快照对应的世界种子。
    /// </summary>
    public ulong WorldSeed { get; }

    /// <summary>
    /// 删除临时快照目录。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
