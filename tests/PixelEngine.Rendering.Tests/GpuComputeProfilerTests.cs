using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering.Compute;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class GpuComputeProfilerTests
{
    [Fact]
    public void MeasureUsesBackendTimerAndRecordsCompletedResultWithoutStalling()
    {
        RecordingComputeBackend backend = new();
        GpuComputeProfiler gpuProfiler = new(backend);
        FrameProfiler frameProfiler = new();
        frameProfiler.BeginFrame();

        using (gpuProfiler.Measure("compute_bloom", FrameSubPhase.GpuComputeBloom))
        {
            Assert.True(backend.Active);
        }

        Assert.Equal(["Begin:compute_bloom:1", "End"], backend.Events);
        gpuProfiler.ResolveCompleted(frameProfiler);
        Assert.Contains("Try:1", backend.Events);
        frameProfiler.EndFrame();
        Assert.Equal(0.25, frameProfiler.LastSubFrame[(int)FrameSubPhase.GpuComputeBloom]);
    }

    [Fact]
    public void ResolveCompletedKeepsPendingQueryUntilResultIsAvailable()
    {
        RecordingComputeBackend backend = new() { ResultAvailable = false };
        GpuComputeProfiler gpuProfiler = new(backend);
        FrameProfiler frameProfiler = new();
        frameProfiler.BeginFrame();

        using (gpuProfiler.Measure("air_smoke", FrameSubPhase.GpuAirSmoke))
        {
        }

        gpuProfiler.ResolveCompleted(frameProfiler);
        backend.ResultAvailable = true;
        gpuProfiler.ResolveCompleted(frameProfiler);
        frameProfiler.EndFrame();

        Assert.Equal(0.25, frameProfiler.LastSubFrame[(int)FrameSubPhase.GpuAirSmoke]);
        Assert.Equal(2, backend.Events.Count(static item => item == "Try:1"));
    }

    [Fact]
    public void NullBackendDoesNotStartTimerQuery()
    {
        NullComputeBackend backend = new();
        GpuComputeProfiler gpuProfiler = new(backend);

        using (gpuProfiler.Measure("noop", FrameSubPhase.GpuComputeBloom))
        {
        }

        gpuProfiler.ResolveCompleted(null);
    }

    [Fact]
    public void RenderPipelineSourceResolvesGpuTimersAndWrapsGpuPasses()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));
        string radianceSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RadianceCascadePass.cs"));
        string airSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "AirSmokePass.cs"));

        Assert.Contains("GpuComputeProfiler", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCompleted(profiler)", source, StringComparison.Ordinal);
        Assert.Contains("Measure(\"compute_bloom\", FrameSubPhase.GpuComputeBloom)", source, StringComparison.Ordinal);
        Assert.Contains("Measure(\"gpu_particles\", FrameSubPhase.GpuParticleDraw)", source, StringComparison.Ordinal);
        Assert.Contains("Measure(\"radiance_cascades\", FrameSubPhase.GpuRadianceCascades)", radianceSource, StringComparison.Ordinal);
        Assert.Contains("Measure(\"air_smoke\", FrameSubPhase.GpuAirSmoke)", airSource, StringComparison.Ordinal);
    }

    private static string ProjectPath(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            path = Directory.GetParent(path)!.FullName;
        }

        return Path.Combine([path, .. parts]);
    }

    private sealed class RecordingComputeBackend : IComputeBackend
    {
        private uint _nextQuery = 1;

        public List<string> Events { get; } = [];

        public ComputeBackendKind Kind => ComputeBackendKind.GlCompute;

        public bool IsAvailable => true;

        public bool Active { get; private set; }

        public bool ResultAvailable { get; set; } = true;

        public ComputeKernel LoadKernel(string name, string source)
        {
            return new ComputeKernel(name, 1);
        }

        public void BindStorageBuffer(uint bindingIndex, uint bufferHandle)
        {
        }

        public void BindTexture(uint unit, uint textureHandle)
        {
        }

        public void BindImage(uint unit, uint textureHandle, int level, bool layered, int layer, GLEnum access, GLEnum format)
        {
        }

        public void SetUniform1(ComputeKernel kernel, string name, int value)
        {
        }

        public void SetUniform1(ComputeKernel kernel, string name, float value)
        {
        }

        public void SetUniform2(ComputeKernel kernel, string name, int x, int y)
        {
        }

        public void SetUniform2(ComputeKernel kernel, string name, float x, float y)
        {
        }

        public void Dispatch(ComputeKernel kernel, uint groupsX, uint groupsY, uint groupsZ)
        {
        }

        public void MemoryBarrier(MemoryBarrierMask barriers)
        {
        }

        public uint BeginTimerQuery(string passName)
        {
            uint query = _nextQuery++;
            Active = true;
            Events.Add($"Begin:{passName}:{query}");
            return query;
        }

        public void EndTimerQuery()
        {
            Active = false;
            Events.Add("End");
        }

        public bool TryGetTimerResult(uint queryHandle, out ulong elapsedNanoseconds)
        {
            Events.Add($"Try:{queryHandle}");
            elapsedNanoseconds = 250_000;
            return ResultAvailable;
        }

        public void DeleteTimerQuery(uint queryHandle)
        {
            Events.Add($"Delete:{queryHandle}");
        }

        public void Dispose()
        {
        }
    }
}
