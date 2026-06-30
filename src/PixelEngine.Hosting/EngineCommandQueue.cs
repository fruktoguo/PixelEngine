namespace PixelEngine.Hosting;

/// <summary>
/// 按目标相位分桶的延迟命令队列，用于脚本/玩法写操作相位安全落地。
/// </summary>
public sealed class EngineCommandQueue
{
    private const int PhaseCount = 12;
    private readonly Bucket[] _buckets = new Bucket[PhaseCount];

    /// <summary>
    /// 创建延迟命令队列。
    /// </summary>
    public EngineCommandQueue()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            _buckets[i] = new Bucket();
        }
    }

    /// <summary>
    /// 入队一个写操作命令，等待目标相位 flush。
    /// </summary>
    public void Enqueue(EnginePhase targetPhase, EngineCommandAction action, object? state = null)
    {
        ValidatePhase(targetPhase);
        ArgumentNullException.ThrowIfNull(action);
        _buckets[(int)targetPhase].Enqueue(new EngineCommand(action, state));
    }

    /// <summary>
    /// 获取指定相位的待执行命令数量。
    /// </summary>
    public int Count(EnginePhase targetPhase)
    {
        ValidatePhase(targetPhase);
        return _buckets[(int)targetPhase].Count;
    }

    /// <summary>
    /// 执行并清空指定相位进入 flush 前已经存在的命令。
    /// </summary>
    public int Flush(EngineTickContext context)
    {
        ValidatePhase(context.Phase);
        return _buckets[(int)context.Phase].Flush(context);
    }

    private static void ValidatePhase(EnginePhase phase)
    {
        if ((uint)phase >= PhaseCount)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "未知 Engine 相位。");
        }
    }

    private readonly record struct EngineCommand(EngineCommandAction Action, object? State);

    private sealed class Bucket
    {
        private EngineCommand[] _commands = [];

        public int Count { get; private set; }

        public void Enqueue(EngineCommand command)
        {
            EnsureCapacity(Count + 1);
            _commands[Count++] = command;
        }

        public int Flush(EngineTickContext context)
        {
            int flushCount = Count;
            for (int i = 0; i < flushCount; i++)
            {
                EngineCommand command = _commands[i];
                _commands[i] = default;
                command.Action(context, command.State);
            }

            int remaining = Count - flushCount;
            if (remaining > 0)
            {
                Array.Copy(_commands, flushCount, _commands, 0, remaining);
                _commands.AsSpan(remaining, flushCount).Clear();
            }

            Count = remaining;
            return flushCount;
        }

        private void EnsureCapacity(int required)
        {
            if (_commands.Length < required)
            {
                Array.Resize(ref _commands, _commands.Length == 0 ? 4 : Math.Max(_commands.Length * 2, required));
            }
        }
    }
}
