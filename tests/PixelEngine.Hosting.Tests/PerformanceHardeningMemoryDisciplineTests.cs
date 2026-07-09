using System.Reflection;
using System.Runtime;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/16 GC 与缓冲池化性能纪律测试。
/// 不变式：缓冲池化与 GC 压力门禁在 CI 可扫描、违规即失败。
/// </summary>
public sealed class PerformanceHardeningMemoryDisciplineTests
{
    /// <summary>
    /// 验证 sim/physics/render 的长寿跨界缓冲使用 POH pinned array 或 NativeMemory。
    /// </summary>
    [Fact]
    public void CrossBoundaryBuffersUsePohOrNativeMemory()
    {
        // Arrange：准备输入与初始状态
        string chunk = ReadProductionSource("src", "PixelEngine.Simulation", "Chunk.cs");
        // Assert：验证预期结果
        Assert.Contains("GC.AllocateArray<ushort>(EngineConstants.ChunkArea, pinned: true)", chunk, StringComparison.Ordinal);
        Assert.Contains("GC.AllocateArray<byte>(EngineConstants.ChunkArea, pinned: true)", chunk, StringComparison.Ordinal);
        Assert.Contains("GC.AllocateArray<PaddedDirtyRectSlot>(IncomingSlotCount, pinned: true)", chunk, StringComparison.Ordinal);

        string box2dTaskBridge = ReadProductionSource("src", "PixelEngine.Interop", "Box2D", "Box2DTaskBridge.cs");
        Assert.Contains("NativeMemory.AllocZeroed", box2dTaskBridge, StringComparison.Ordinal);
        Assert.Contains("NativeMemory.Free", box2dTaskBridge, StringComparison.Ordinal);

        string bodyLocalMask = ReadProductionSource("src", "PixelEngine.Physics", "BodyLocalMask.cs");
        Assert.Contains("GC.AllocateArray<byte>(area, pinned: true)", bodyLocalMask, StringComparison.Ordinal);
        Assert.Contains("GC.AllocateArray<ushort>(area, pinned: true)", bodyLocalMask, StringComparison.Ordinal);

        string renderBuffer = ReadProductionSource("src", "PixelEngine.Rendering", "RenderBuffer.cs");
        Assert.Contains("GC.AllocateArray<uint>(checked(width * height), pinned: true)", renderBuffer, StringComparison.Ordinal);

        string renderAuxBuffers = ReadProductionSource("src", "PixelEngine.Rendering", "RenderAuxBuffers.cs");
        Assert.Contains("GC.AllocateArray<uint>(checked(width * height), pinned: true)", renderAuxBuffers, StringComparison.Ordinal);
        Assert.Contains("GC.AllocateArray<byte>(checked(width * height), pinned: true)", renderAuxBuffers, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 particle/body/shape/scratch/serialization 临时缓冲走池化或预分配 pinned array。
    /// </summary>
    [Fact]

    // —— 内存池化与分配纪律 ——
    public void ShortLivedParticleBodyShapeScratchBuffersUsePools()
    {
        // Arrange：准备输入与初始状态
        string particles = ReadProductionSource("src", "PixelEngine.Simulation", "Particles", "ParticleSystem.cs");
        // Assert：验证预期结果
        Assert.Contains("GC.AllocateArray<Particle>(capacity, pinned: true)", particles, StringComparison.Ordinal);
        Assert.Contains("GC.AllocateArray<ParticleOutcome>(capacity, pinned: true)", particles, StringComparison.Ordinal);
        Assert.Contains("GC.AllocateArray<EjectionRequest>(EngineConstants.ParticleEjectMaxPerTick, pinned: true)", particles, StringComparison.Ordinal);

        string destruction = ReadProductionSource("src", "PixelEngine.Physics", "RigidBodyDestruction.cs");
        Assert.Contains("GC.AllocateArray<int>(jobs.WorkerCount, pinned: true)", destruction, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Rent(area)", destruction, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<ushort>.Shared.Rent(area)", destruction, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<int>.Shared.Rent(area)", destruction, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<ConnectedComponent>.Shared.Rent(area)", destruction, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Return", destruction, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<ushort>.Shared.Return", destruction, StringComparison.Ordinal);

        string shapeBuilder = ReadProductionSource("src", "PixelEngine.Physics", "RigidBodyMaskShapeBuilder.cs");
        Assert.Contains("ArrayPool<Vector2>.Shared.Rent", shapeBuilder, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<Vector2>.Shared.Return", shapeBuilder, StringComparison.Ordinal);

        string codec = ReadProductionSource("src", "PixelEngine.Serialization", "ChunkCodec.cs");
        Assert.Contains("PooledByteBufferWriter payloadWriter", codec, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Rent(snapshot.Flags.Length)", codec, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Rent(header.UncompressedPayloadBytes)", codec, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Return", codec, StringComparison.Ordinal);

        string streamer = ReadProductionSource("src", "PixelEngine.World", "WorldStreamer.cs");
        Assert.Contains("ThreadLocal<PooledByteBufferWriter>", streamer, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<Half>.Shared.Rent(TemperatureField.BlockArea)", streamer, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<Half>.Shared.Return", streamer, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 GC 对比基准在进程内显式切入低延迟模式并在结束后恢复。
    /// </summary>
    [Fact]
    public void GcPauseBenchmarkUsesSustainedLowLatencyAndRestoresOriginalMode()
    {
        string benchmark = ReadProductionSource("bench", "PixelEngine.Benchmarks", "GcPauseBenchmark.cs");
        Assert.Contains("[GlobalSetup]", benchmark, StringComparison.Ordinal);
        Assert.Contains("[GlobalCleanup]", benchmark, StringComparison.Ordinal);
        Assert.Contains("_originalLatencyMode = GCSettings.LatencyMode", benchmark, StringComparison.Ordinal);
        Assert.Contains("GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency", benchmark, StringComparison.Ordinal);
        Assert.Contains("GCSettings.LatencyMode = _originalLatencyMode", benchmark, StringComparison.Ordinal);
        Assert.Contains("public bool IsServerGc => GCSettings.IsServerGC", benchmark, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 plan/20 游戏大 UI 具备 MemoryDiagnoser 零分配基准入口。
    /// </summary>
    [Fact]
    public void GameUiAllocationBenchmarkCoversStaticCleanFramePaths()
    {
        string benchmark = ReadProductionSource("bench", "PixelEngine.Benchmarks", "GameUiAllocationBenchmarks.cs");
        Assert.Contains("[MemoryDiagnoser]", benchmark, StringComparison.Ordinal);
        Assert.Contains("public class GameUiAllocationBenchmarks", benchmark, StringComparison.Ordinal);
        Assert.Contains("RunStaticUiPhaseFrame", benchmark, StringComparison.Ordinal);
        Assert.Contains("CompositeCleanFrameSkip", benchmark, StringComparison.Ordinal);
        Assert.Contains("DrawGuiCleanFrameSkip", benchmark, StringComparison.Ordinal);
        Assert.Contains("PumpIdleInput", benchmark, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Hosting 通过统一协调器操作进程级 GC 状态，避免并发 Engine 构建与 NoGCRegion 临界帧互相踩踏。
    /// </summary>
    [Fact]
    public void HostingGcStateChangesAreSerializedThroughCoordinator()
    {
        // Arrange：准备输入与初始状态
        string builder = ReadProductionSource("src", "PixelEngine.Hosting", "EngineBuilder.cs");
        string engine = ReadProductionSource("src", "PixelEngine.Hosting", "Engine.cs");
        string coordinator = ReadProductionSource("src", "PixelEngine.Hosting", "EngineGcCoordinator.cs");

        // Assert：验证预期结果
        Assert.Contains("EngineGcCoordinator.ApplyLatencyMode(options.GcMode.ToLatencyMode())", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("GCSettings.LatencyMode =", builder, StringComparison.Ordinal);
        Assert.Contains("EngineGcCoordinator.TryBeginNoGcRegion(budgetBytes)", engine, StringComparison.Ordinal);
        Assert.Contains("EngineGcCoordinator.EndNoGcRegion()", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.TryStartNoGCRegion", engine, StringComparison.Ordinal);
        Assert.Contains("private static readonly object Gate", coordinator, StringComparison.Ordinal);
        Assert.Contains("Monitor.Enter(Gate)", coordinator, StringComparison.Ordinal);
        Assert.Contains("Monitor.Exit(Gate)", coordinator, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 TryBeginNoGcRegion 的运行时拒绝路径不会泄漏全局 GC 状态门。
    /// </summary>
    [Fact]
    public async Task TryBeginNoGcRegionReleasesCoordinatorGateWhenStartFails()
    {
        GCLatencyMode original = GCSettings.LatencyMode;
        try
        {
            try
            {
                bool started = InvokeTryBeginNoGcRegion(0);
                if (started)
                {
                    InvokeEndNoGcRegion();
                }
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            await AssertCoordinatorAllowsLatencyModeChangeFromWorkerAsync(original);
        }
        finally
        {
            GCSettings.LatencyMode = original;
        }
    }

    /// <summary>
    /// 验证 NoGCRegion 持有期间延迟模式切换会被协调器阻塞，EndNoGcRegion 后再释放。
    /// </summary>
    [Fact]
    public async Task NoGcRegionCoordinatorBlocksLatencyModeChangesUntilRegionEnds()
    {
        // Arrange：搭建测试场景与依赖
        const long BudgetBytes = 64L * 1024 * 1024;
        GCLatencyMode original = GCSettings.LatencyMode;
        bool started = false;
        try
        {
            started = InvokeTryBeginNoGcRegion(BudgetBytes);
            if (!started)
            {
                // Act：执行被测操作
                await AssertCoordinatorAllowsLatencyModeChangeFromWorkerAsync(original);
                return;
            }

            using ManualResetEventSlim workerEntered = new();
            Exception? workerException = null;
            Thread latencyThread = new(() =>
            {
                try
                {
                    workerEntered.Set();
                    InvokeApplyLatencyMode(original);
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            latencyThread.Start();
            // Assert：验证不变式与预期结果
            Assert.True(workerEntered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(latencyThread.Join(TimeSpan.FromMilliseconds(100)));

            InvokeEndNoGcRegion();
            started = false;

            Assert.True(latencyThread.Join(TimeSpan.FromSeconds(2)));
            if (workerException is not null)
            {
                throw workerException;
            }
        }
        finally
        {
            if (started)
            {
                try
                {
                    InvokeEndNoGcRegion();
                }
                catch (InvalidOperationException)
                {
                }
            }

            GCSettings.LatencyMode = original;
        }
    }

    private static bool InvokeTryBeginNoGcRegion(long budgetBytes)
    {
        return (bool)InvokeCoordinator("TryBeginNoGcRegion", budgetBytes)!;
    }

    private static void InvokeEndNoGcRegion()
    {
        _ = InvokeCoordinator("EndNoGcRegion");
    }

    private static void InvokeApplyLatencyMode(GCLatencyMode mode)
    {
        _ = InvokeCoordinator("ApplyLatencyMode", mode);
    }

    private static async Task AssertCoordinatorAllowsLatencyModeChangeFromWorkerAsync(GCLatencyMode mode)
    {
        Task latencyTask = Task.Run(() => InvokeApplyLatencyMode(mode));
        Task completed = await Task.WhenAny(latencyTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(latencyTask, completed);
        await latencyTask;
    }

    private static object? InvokeCoordinator(string methodName, params object?[] parameters)
    {
        Type coordinatorType = typeof(Engine).Assembly.GetType("PixelEngine.Hosting.EngineGcCoordinator", throwOnError: true)!;
        MethodInfo method = coordinatorType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(coordinatorType.FullName, methodName);
        try
        {
            return method.Invoke(null, parameters);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static string ReadProductionSource(params string[] relativePath)
    {
        return File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. relativePath]));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
