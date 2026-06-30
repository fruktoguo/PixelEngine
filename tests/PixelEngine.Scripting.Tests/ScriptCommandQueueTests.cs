using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本延迟命令队列测试。
/// </summary>
public sealed class ScriptCommandQueueTests
{
    /// <summary>
    /// 验证不同目标相位的命令互不污染，并且 drain 后清空。
    /// </summary>
    [Fact]
    public void QueueDrainsCommandsByTarget()
    {
        using ScriptCommandQueue queue = new();
        queue.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.SetCell(1, 2, new MaterialId(3)));
        queue.Enqueue(ScriptCommandTarget.Particle, ScriptCommand.SpawnParticle(new ParticleSpawnDesc(1, 2, 3, 4, new MaterialId(5), 6)));

        Span<ScriptCommand> buffer = stackalloc ScriptCommand[2];
        int cellCount = queue.DrainTo(ScriptCommandTarget.CellWrite, buffer);

        Assert.Equal(1, cellCount);
        Assert.Equal(ScriptCommandKind.SetCell, buffer[0].Kind);
        Assert.Equal(new MaterialId(3), buffer[0].Material);
        int particleCount = queue.DrainTo(ScriptCommandTarget.Particle, buffer);
        Assert.Equal(1, particleCount);
        Assert.Equal(ScriptCommandKind.SpawnParticle, buffer[0].Kind);
        Assert.Equal(0, queue.Count(ScriptCommandTarget.CellWrite));
        Assert.Equal(0, queue.Count(ScriptCommandTarget.Particle));
    }

    /// <summary>
    /// 验证队列可接收多个线程的写入，并在单次 drain 中全部取出。
    /// </summary>
    [Fact]
    public async Task QueueCollectsCommandsFromMultipleThreads()
    {
        using ScriptCommandQueue queue = new();
        Task[] tasks =
        [
            Task.Run(() => EnqueueRange(queue, 0, 64)),
            Task.Run(() => EnqueueRange(queue, 64, 64)),
        ];

        await Task.WhenAll(tasks);

        ScriptCommand[] buffer = new ScriptCommand[128];
        int count = queue.DrainTo(ScriptCommandTarget.CellWrite, buffer);

        Assert.Equal(128, count);
        Assert.Equal(0, queue.Count(ScriptCommandTarget.CellWrite));
        Assert.Equal(128, buffer.Take(count).Select(command => command.X).Distinct().Count());
    }

    /// <summary>
    /// 验证 drain 目标缓冲不足时快速失败且不丢命令。
    /// </summary>
    [Fact]
    public void DrainRejectsInsufficientDestination()
    {
        using ScriptCommandQueue queue = new();
        queue.Enqueue(ScriptCommandTarget.Physics, ScriptCommand.ApplyImpulse(new BodyHandle(7), 1, 2));

        ScriptCommand[] empty = [];
        _ = Assert.Throws<ArgumentException>(() => queue.DrainTo(ScriptCommandTarget.Physics, empty));

        Assert.Equal(1, queue.Count(ScriptCommandTarget.Physics));
    }

    private static void EnqueueRange(ScriptCommandQueue queue, int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            queue.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.SetCell(start + i, 0, new MaterialId(1)));
        }
    }
}
