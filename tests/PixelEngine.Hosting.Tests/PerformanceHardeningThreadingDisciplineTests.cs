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
