using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 验证脚本 ALC 在多轮 Roslyn 编译与卸载后仍可回收。
/// </summary>
[Collection("ALC collectibility")]
public sealed class AlcCollectibilityTests
{
    /// <summary>
    /// 验证多轮编译、装载、实例化与卸载不会强引用旧 ScriptLoadContext。
    /// </summary>
    [Fact]
    public void RepeatedCompileLoadAndUnloadCollectsAllScriptLoadContexts()
    {
        const int WarmupRounds = 6;
        const int MeasuredRounds = 40;
        ScriptCompiler compiler = new();

        for (int i = 0; i < WarmupRounds; i++)
        {
            WeakReference warmupReference = CompileLoadAndUnload(compiler, i);
            Assert.True(WaitForUnload(warmupReference), $"预热第 {i + 1} 轮脚本 ALC 未释放。");
        }

        ForceFullCollection();
        long[] heapSamples = new long[MeasuredRounds];
        WeakReference[] references = new WeakReference[MeasuredRounds];

        for (int i = 0; i < MeasuredRounds; i++)
        {
            references[i] = CompileLoadAndUnload(compiler, WarmupRounds + i);
            Assert.True(WaitForUnload(references[i]), $"第 {i + 1} 轮脚本 ALC 未释放。");
            heapSamples[i] = GC.GetTotalMemory(forceFullCollection: true);
        }

        for (int i = 0; i < references.Length; i++)
        {
            Assert.False(references[i].IsAlive, $"第 {i + 1} 轮脚本 ALC 仍被强引用持有。");
        }

        AssertNoObviousManagedHeapGrowth(heapSamples);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompileLoadAndUnload(ScriptCompiler compiler, int round)
    {
        string typeName = $"CollectibleScript{round}";
        ScriptCompilationResult result = compiler.Compile(
            $"PixelEngine.UserScripts.Collectible.{round}.{Guid.NewGuid():N}",
            [new ScriptSourceFile($"{typeName}.cs", CreateScriptSource(typeName, round))]);
        Assert.True(result.Success, FormatDiagnostics(result));

        ScriptLoadContext loadContext = new($"script-collectible-{round}-{Guid.NewGuid():N}");
        Assembly assembly = loadContext.LoadFromImages(result.PeImage, result.PdbImage);
        Type type = assembly.GetType($"UserScripts.{typeName}", throwOnError: true)!;
        Assert.True(typeof(Behaviour).IsAssignableFrom(type));

        Behaviour behaviour = (Behaviour)Activator.CreateInstance(type)!;
        PropertyInfo property = type.GetProperty("Round", BindingFlags.Instance | BindingFlags.Public)!;
        Assert.Equal(round, (int)property.GetValue(behaviour)!);

        WeakReference reference = new(loadContext, trackResurrection: false);
        loadContext.Unload();
        return reference;
    }

    private static string CreateScriptSource(string typeName, int round)
    {
        return $$"""
            using PixelEngine.Scripting;

            namespace UserScripts;

            public sealed class {{typeName}} : Behaviour
            {
                public int Round => {{round}};

                protected override void OnUpdate(float dt)
                {
                }
            }
            """;
    }

    private static string FormatDiagnostics(ScriptCompilationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static bool WaitForUnload(WeakReference reference)
    {
        for (int i = 0; reference.IsAlive && i < 50; i++)
        {
            ForceFullCollection();
        }

        return !reference.IsAlive;
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void AssertNoObviousManagedHeapGrowth(ReadOnlySpan<long> heapSamples)
    {
        const long MaxRetainedGrowthBytes = 96L * 1024 * 1024;
        const int WindowSize = 8;

        long earlyMin = long.MaxValue;
        for (int i = 0; i < WindowSize; i++)
        {
            earlyMin = Math.Min(earlyMin, heapSamples[i]);
        }

        long lateMax = 0;
        for (int i = heapSamples.Length - WindowSize; i < heapSamples.Length; i++)
        {
            lateMax = Math.Max(lateMax, heapSamples[i]);
        }

        long retainedGrowth = lateMax - earlyMin;
        Assert.True(
            retainedGrowth <= MaxRetainedGrowthBytes,
            $"多轮编译/卸载后的托管堆增长过大：{retainedGrowth:n0} bytes，" +
            $"早期最低 {earlyMin:n0} bytes，后期最高 {lateMax:n0} bytes。");
    }
}

/// <summary>
/// 独占执行 ALC 可回收测试，避免并行测试干扰托管堆样本。
/// </summary>
[CollectionDefinition("ALC collectibility", DisableParallelization = true)]
public sealed class AlcCollectibilityCollection;
