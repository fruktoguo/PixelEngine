using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;

IConfig config = DefaultConfig.Instance
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddDiagnoser(ThreadingDiagnoser.Default)
    .AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 3)));

if (string.Equals(
    Environment.GetEnvironmentVariable("PIXELENGINE_BENCH_HARDWARE_COUNTERS"),
    "1",
    StringComparison.Ordinal))
{
    config = config.AddHardwareCounters(
        HardwareCounter.CacheMisses,
        HardwareCounter.BranchMispredictions);
}

if (string.Equals(
    Environment.GetEnvironmentVariable("PIXELENGINE_BENCH_EVENTPIPE"),
    "1",
    StringComparison.Ordinal))
{
    config = config.AddDiagnoser(new EventPipeProfiler());
}

BenchmarkSwitcher.FromAssembly(typeof(PixelEngine.Benchmarks.InfiniteTerrainChunkGenerationBenchmarks).Assembly).Run(args, config);
