using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PixelEngine.Core;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/16 多线程覆盖面与 worker 元数据性能纪律测试。
/// </summary>
public sealed class PerformanceHardeningThreadingDisciplineTests
{
    /// <summary>
    /// 验证生产源码不引入每帧临时并行 API，统一经 JobSystem 持久 worker 派发。
    /// </summary>
    [Fact]
    public void ProductionSourcesDoNotUseBclAdHocParallelDispatch()
    {
        string root = FindRepositoryRoot();
        string[] directories =
        [
            Path.Combine(root, "src"),
            Path.Combine(root, "demo"),
            Path.Combine(root, "bench"),
        ];

        Regex forbidden = new(@"\bParallel\s*\.\s*(?:For|ForEach)\s*\(|\bTask\s*\.\s*Run\s*\(|\bThreadPool\s*\.\s*(?:QueueUserWorkItem|UnsafeQueueUserWorkItem)\s*\(");
        foreach (string file in directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)))
        {
            string code = StripComments(File.ReadAllText(file));
            Match match = forbidden.Match(code);

            Assert.False(match.Success, $"{Path.GetRelativePath(root, file)} 禁止使用临时并行 API：{match.Value}");
        }
    }

    /// <summary>
    /// 验证 Box2D task bridge 回调是 unmanaged callback，但没有错误使用 SuppressGCTransition。
    /// </summary>
    [Fact]
    public void Box2DTaskBridgeCallbacksDoNotSuppressGcTransition()
    {
        MethodInfo[] callbackMethods =
        [
            typeof(Box2DTaskBridge).GetMethod(nameof(Box2DTaskBridge.EnqueueTask))!,
            typeof(Box2DTaskBridge).GetMethod(nameof(Box2DTaskBridge.FinishTask))!,
            typeof(Box2DTaskBridge).GetMethod("InvokeTask", BindingFlags.NonPublic | BindingFlags.Static)!,
        ];

        foreach (MethodInfo method in callbackMethods)
        {
            Assert.NotNull(method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>());
            Assert.Null(method.GetCustomAttribute<SuppressGCTransitionAttribute>());
        }

        MethodInfo worldStep = typeof(Box2D).GetMethod(nameof(Box2D.b2World_Step))!;
        Assert.Null(worldStep.GetCustomAttribute<SuppressGCTransitionAttribute>());
    }

    /// <summary>
    /// 验证 CA checkerboard 调度按 4-pass bucket 经 JobSystem 派发，并具备低活跃 chunk 单线程回退。
    /// </summary>
    [Fact]
    public void CheckerboardSchedulerUsesFourPassJobSystemBarrierWithSmallWorkFallback()
    {
        string root = FindRepositoryRoot();
        string source = StripComments(File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Simulation", "CheckerboardScheduler.cs")));

        Assert.Contains("private readonly Chunk[][] _buckets", source, StringComparison.Ordinal);
        Assert.Contains("for (int pass = 0; pass < _buckets.Length; pass++)", source, StringComparison.Ordinal);
        Assert.Contains("jobs.ParallelRange(count, 1, UpdateRangeJob, this)", source, StringComparison.Ordinal);
        Assert.Contains("awakeCount < EngineConstants.SingleThreadChunkThreshold", source, StringComparison.Ordinal);
        Assert.Contains("StepBucketsSingleThread()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lock (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Monitor.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Mutex", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 JobSystem 在 worker 数、任务量或 range 阈值不足时走同步单线程回退。
    /// </summary>
    [Fact]
    public void JobSystemFallsBackToSingleThreadForSmallWork()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 8,
        };

        int[] captured = new int[4];
        jobs.ParallelRange(
            itemCount: 4,
            minRange: 1,
            static (start, end, worker, context) =>
            {
                int[] state = (int[])context!;
                state[0]++;
                state[1] = start;
                state[2] = end;
                state[3] = worker;
            },
            captured);

        Assert.Equal(1, captured[0]);
        Assert.Equal(0, captured[1]);
        Assert.Equal(4, captured[2]);
        Assert.Equal(0, captured[3]);
    }

    /// <summary>
    /// 验证 render buffer 构建按屏幕行分块经 JobSystem 并行。
    /// </summary>
    [Fact]
    public void RenderBufferBuilderDispatchesRowsThroughJobSystem()
    {
        string source = ReadProductionSource("src", "PixelEngine.Rendering", "RenderBufferBuilder.cs");

        Assert.Contains("JobSystem? jobs = null", source, StringComparison.Ordinal);
        Assert.Contains("private readonly JobSystem? _jobs = jobs", source, StringComparison.Ordinal);
        Assert.Contains("BuildRows(0, target.Height, 0, _state)", source, StringComparison.Ordinal);
        Assert.Contains("_jobs.ParallelRange(target.Height, Math.Max(1, _options.MinRowsPerJob), BuildRows, _state)", source, StringComparison.Ordinal);
        Assert.Contains("private static void BuildRows(int start, int end, int workerIndex, object? state)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证粒子积分按活跃前缀分段经 JobSystem 并行，沉积写回保持在后续串行阶段。
    /// </summary>
    [Fact]
    public void ParticleIntegrationDispatchesActivePrefixThroughJobSystem()
    {
        string source = ReadProductionSource("src", "PixelEngine.Simulation", "Particles", "ParticleSystem.cs");

        Assert.Contains("private static readonly RangeJob IntegrateRangeJob", source, StringComparison.Ordinal);
        Assert.Contains("public void IntegrateAndAdvance(JobSystem jobs, CellGrid grid)", source, StringComparison.Ordinal);
        Assert.Contains("jobs.ParallelRange(ActiveCount, 256, IntegrateRangeJob, this)", source, StringComparison.Ordinal);
        Assert.Contains("private void IntegrateRange(int start, int end)", source, StringComparison.Ordinal);
        Assert.Contains("public void ResolveDeposits(SimulationKernel kernel, CellGrid grid)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("jobs.ParallelRange(ActiveCount, 256", source[source.IndexOf("public void ResolveDeposits", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 CCL、轮廓/凸分解准备阶段按刚体工作项经 JobSystem 并行，Box2D apply 留在同步阶段。
    /// </summary>
    [Fact]
    public void RigidBodyDestructionPreparesRebuildPlansThroughJobSystem()
    {
        string source = ReadProductionSource("src", "PixelEngine.Physics", "RigidBodyDestruction.cs");

        Assert.Contains("private static readonly RangeJob PreparePlansJob", source, StringComparison.Ordinal);
        Assert.Contains("jobs.ParallelRange(workItems.Count, 1, PreparePlansJob, batch)", source, StringComparison.Ordinal);
        Assert.Contains("ConnectedComponentLabeler.Label", source, StringComparison.Ordinal);
        Assert.Contains("RigidBodyMaskShapeBuilder.TryBuildConvexPieces", source, StringComparison.Ordinal);
        Assert.Contains("Box2D.b2DestroyBody", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("jobs.ParallelRange(workItems.Count, 1, PreparePlansJob, batch)", StringComparison.Ordinal) <
            source.IndexOf("private RigidDestructionResult ApplyPlan", StringComparison.Ordinal),
            "刚体重建准备阶段必须先并行生成离线计划，再进入 Box2D apply 阶段。");
    }

    /// <summary>
    /// 验证相位 11 序列化字节准备可经 JobSystem 并行，并且 live map 只在提交/应用阶段触碰。
    /// </summary>
    [Fact]
    public void WorldStreamerPreparesOfflineSerializationBatchThroughJobSystem()
    {
        string source = ReadProductionSource("src", "PixelEngine.World", "WorldStreamer.cs");

        Assert.Contains("public int ProcessIoOnce(JobSystem? jobs)", source, StringComparison.Ordinal);
        Assert.Contains("jobs.ParallelRange(requestCount, 1, PrepareRange, _preparationBatch)", source, StringComparison.Ordinal);
        Assert.Contains("private static void PrepareRange(int start, int end, int workerIndex, object? context)", source, StringComparison.Ordinal);
        Assert.Contains("private PreparedStreamingOperation PrepareLoad(ChunkCoord coord)", source, StringComparison.Ordinal);
        Assert.Contains("private PreparedStreamingOperation PrepareUnload(StreamingRequest request)", source, StringComparison.Ordinal);
        Assert.Contains("ThreadLocal<PooledByteBufferWriter>", source, StringComparison.Ordinal);

        int prepareLoad = source.IndexOf("private PreparedStreamingOperation PrepareLoad", StringComparison.Ordinal);
        int prepareUnload = source.IndexOf("private PreparedStreamingOperation PrepareUnload", StringComparison.Ordinal);
        int ensureRequestCapacity = source.IndexOf("private void EnsureRequestCapacity", StringComparison.Ordinal);
        string prepareSection = source[prepareLoad..ensureRequestCapacity];

        Assert.DoesNotContain("_chunks.Add", prepareSection, StringComparison.Ordinal);
        Assert.DoesNotContain("_chunks.TryRemove", prepareSection, StringComparison.Ordinal);
        Assert.True(prepareUnload > prepareLoad);
    }

    /// <summary>
    /// 验证 sand/liquid movement 内层保持标量 gather/scatter，不引入 SIMD 路径。
    /// </summary>
    [Fact]
    public void SandAndLiquidMovementRemainScalar()
    {
        string source = ReadProductionSource("src", "PixelEngine.Simulation", "ChunkUpdater.cs");

        int powderStart = source.IndexOf("private static bool TryMovePowder", StringComparison.Ordinal);
        int lifetimeStart = source.IndexOf("private static void ProcessLifetime", StringComparison.Ordinal);
        Assert.True(powderStart >= 0);
        Assert.True(lifetimeStart > powderStart);

        string movementSection = source[powderStart..lifetimeStart];
        Assert.Contains("TryMoveTo", movementSection, StringComparison.Ordinal);
        Assert.Contains("window.Swap(sourceX, sourceY, targetX, targetY)", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector<", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Numerics", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Runtime.Intrinsics", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Avx", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Sse", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector128", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector256", movementSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector512", movementSection, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 worker-local 槽位至少填充到一个 cache line，避免相邻 worker 元数据 false sharing。
    /// </summary>
    [Fact]
    public void WorkerLocalSlotsArePaddedToCacheLine()
    {
        Type slotType = typeof(WorkerLocal<object>).GetNestedType("PaddedSlot", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WorkerLocal<T>.PaddedSlot 不存在。");

        StructLayoutAttribute? layout = slotType.StructLayoutAttribute;
        Assert.NotNull(layout);
        Assert.Equal(LayoutKind.Sequential, layout.Value);

        int paddingBytes = slotType
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Count(field => field.FieldType == typeof(long))
            * sizeof(long);
        int conservativeSlotBytes = IntPtr.Size + paddingBytes;

        Assert.InRange(conservativeSlotBytes, EngineConstants.CacheLineBytes, int.MaxValue);
    }

    /// <summary>
    /// 验证 per-chunk 入站 dirty 槽显式填充到一个 cache line。
    /// </summary>
    [Fact]
    public void ChunkIncomingDirtySlotsArePaddedToCacheLine()
    {
        Type slotType = typeof(Chunk).Assembly.GetType("PixelEngine.Simulation.PaddedDirtyRectSlot")
            ?? throw new InvalidOperationException("PaddedDirtyRectSlot 不存在。");

        StructLayoutAttribute? layout = slotType.StructLayoutAttribute;
        Assert.NotNull(layout);
        Assert.Equal(LayoutKind.Explicit, layout.Value);
        Assert.Equal(EngineConstants.CacheLineBytes, layout.Size);
        Assert.Equal(EngineConstants.CacheLineBytes, Marshal.SizeOf(slotType));
    }

    private static string StripComments(string source)
    {
        StringBuilder builder = new(source.Length);
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    _ = builder.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            _ = builder.Append(current);
        }

        return builder.ToString();
    }

    private static string ReadProductionSource(params string[] relativePath)
    {
        return StripComments(File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. relativePath])));
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
