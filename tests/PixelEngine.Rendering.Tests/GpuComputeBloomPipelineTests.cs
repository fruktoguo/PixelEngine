using PixelEngine.Rendering.Compute;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class GpuComputeBloomPipelineTests
{
    [Fact]
    public void ConstructorLoadsInitialBloomAndLightKernels()
    {
        RecordingComputeBackend backend = new();

        _ = new GpuComputeBloomPipeline(backend);

        Assert.Equal(GpuComputeShaderSources.PassNames, backend.LoadedKernelNames);
        Assert.All(backend.LoadedSources, static source => Assert.Contains("#version 430", source, StringComparison.Ordinal));
    }

    [Fact]
    public void BrightPassBindsSourceOutputUniformsAndDispatchesCeilGroups()
    {
        RecordingComputeBackend backend = new();
        GpuComputeBloomPipeline pipeline = new(backend);
        backend.Events.Clear();

        pipeline.DispatchBrightPass(sourceTexture: 10, outputImage: 20, width: 1920, height: 1080, threshold: 0.75f);

        Assert.Contains("Texture:0=10", backend.Events);
        Assert.Contains("Image:0=20:WriteOnly:Rgba8", backend.Events);
        Assert.Contains("Uniform1i:bloom_brightpass:uSourceTexture=0", backend.Events);
        Assert.Contains("Uniform2i:bloom_brightpass:uOutputSize=1920,1080", backend.Events);
        Assert.Contains("Uniform1f:bloom_brightpass:uThreshold=0.75", backend.Events);
        Assert.Contains("Dispatch:bloom_brightpass=120,68,1", backend.Events);
        Assert.Contains(backend.Events, static item =>
            item.StartsWith("Barrier:", StringComparison.Ordinal) &&
            item.Contains("ShaderImageAccessBarrierBit", StringComparison.Ordinal) &&
            item.Contains("TextureFetchBarrierBit", StringComparison.Ordinal));
    }

    [Fact]
    public void LightCompositeBindsThreeInputsAndOutput()
    {
        RecordingComputeBackend backend = new();
        GpuComputeBloomPipeline pipeline = new(backend);
        backend.Events.Clear();

        pipeline.DispatchLightComposite(
            worldTexture: 1,
            lightTexture: 2,
            emissiveTexture: 3,
            outputImage: 4,
            width: 16,
            height: 16,
            exposure: 1.25f);

        Assert.Contains("Texture:0=1", backend.Events);
        Assert.Contains("Texture:1=2", backend.Events);
        Assert.Contains("Texture:2=3", backend.Events);
        Assert.Contains("Image:0=4:WriteOnly:Rgba8", backend.Events);
        Assert.Contains("Uniform1f:light_composite:uExposure=1.25", backend.Events);
        Assert.Contains("Dispatch:light_composite=1,1,1", backend.Events);
    }

    [Fact]
    public void RejectsZeroHandlesBeforeDispatch()
    {
        RecordingComputeBackend backend = new();
        GpuComputeBloomPipeline pipeline = new(backend);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => pipeline.DispatchDownsample(0, 1, 16, 16, 1f, 1f));

        Assert.Contains("句柄", exception.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingComputeBackend : IComputeBackend
    {
        private uint _nextHandle = 1;

        public List<string> LoadedKernelNames { get; } = [];

        public List<string> LoadedSources { get; } = [];

        public List<string> Events { get; } = [];

        public ComputeBackendKind Kind => ComputeBackendKind.GlCompute;

        public bool IsAvailable => true;

        public ComputeKernel LoadKernel(string name, string source)
        {
            LoadedKernelNames.Add(name);
            LoadedSources.Add(source);
            return new ComputeKernel(name, _nextHandle++);
        }

        public void BindStorageBuffer(uint bindingIndex, uint bufferHandle)
        {
            Events.Add($"Storage:{bindingIndex}={bufferHandle}");
        }

        public void BindTexture(uint unit, uint textureHandle)
        {
            Events.Add($"Texture:{unit}={textureHandle}");
        }

        public void BindImage(uint unit, uint textureHandle, int level, bool layered, int layer, GLEnum access, GLEnum format)
        {
            Events.Add($"Image:{unit}={textureHandle}:{access}:{format}");
        }

        public void SetUniform1(ComputeKernel kernel, string name, int value)
        {
            Events.Add($"Uniform1i:{kernel.Name}:{name}={value}");
        }

        public void SetUniform1(ComputeKernel kernel, string name, float value)
        {
            Events.Add($"Uniform1f:{kernel.Name}:{name}={value}");
        }

        public void SetUniform2(ComputeKernel kernel, string name, int x, int y)
        {
            Events.Add($"Uniform2i:{kernel.Name}:{name}={x},{y}");
        }

        public void SetUniform2(ComputeKernel kernel, string name, float x, float y)
        {
            Events.Add($"Uniform2f:{kernel.Name}:{name}={x},{y}");
        }

        public void Dispatch(ComputeKernel kernel, uint groupsX, uint groupsY, uint groupsZ)
        {
            Events.Add($"Dispatch:{kernel.Name}={groupsX},{groupsY},{groupsZ}");
        }

        public void MemoryBarrier(MemoryBarrierMask barriers)
        {
            Events.Add($"Barrier:{barriers}");
        }

        public uint BeginTimerQuery(string passName)
        {
            return 0;
        }

        public void EndTimerQuery()
        {
        }

        public bool TryGetTimerResult(uint queryHandle, out ulong elapsedNanoseconds)
        {
            elapsedNanoseconds = 0;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
