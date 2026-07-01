using PixelEngine.Rendering.Compute;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class GpuRadianceCascadePipelineTests
{
    [Fact]
    public void ConstructorLoadsRadianceCascadeKernelsOnly()
    {
        RecordingComputeBackend backend = new();

        _ = new GpuRadianceCascadePipeline(backend);

        Assert.Equal(
            [
                GpuComputeShaderSources.RadianceCascadeSdfJfaName,
                GpuComputeShaderSources.RadianceCascadeBuildName,
                GpuComputeShaderSources.RadianceCascadeMergeName,
                GpuComputeShaderSources.RadianceCascadeApplyName,
            ],
            backend.LoadedKernelNames);
    }

    [Fact]
    public void SdfInitializeAndJumpUseNoReadbackImageBarriers()
    {
        RecordingComputeBackend backend = new();
        GpuRadianceCascadePipeline pipeline = new(backend);
        backend.Events.Clear();

        pipeline.DispatchSdfInitialize(occluderTexture: 1, outputSdf: 2, width: 32, height: 16, threshold: 0.5f);
        pipeline.DispatchSdfJump(sourceSdf: 2, outputSdf: 3, width: 32, height: 16, jumpStep: 8);

        Assert.Contains("Image:0=2:WriteOnly:Rgba32f", backend.Events);
        Assert.Contains("Uniform1i:rc_sdf_jfa:uInitialize=1", backend.Events);
        Assert.Contains("Uniform1i:rc_sdf_jfa:uJumpStep=8", backend.Events);
        Assert.Contains("Dispatch:rc_sdf_jfa=2,1,1", backend.Events);
        Assert.All(backend.Events.Where(static item => item.StartsWith("Barrier:", StringComparison.Ordinal)), static item =>
        {
            Assert.Contains("ShaderImageAccessBarrierBit", item, StringComparison.Ordinal);
            Assert.Contains("TextureFetchBarrierBit", item, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CascadeBuildMergeAndApplyBindExpectedResources()
    {
        RecordingComputeBackend backend = new();
        GpuRadianceCascadePipeline pipeline = new(backend);
        backend.Events.Clear();
        RadianceCascadeSettings settings = RadianceCascadeSettings.Default with
        {
            CascadeCount = 2,
            BaseRayCount = 8,
            MaxRaySteps = 4,
        };

        pipeline.DispatchCascadeBuild(10, 11, 12, 16, 16, settings, cascadeIndex: 1);
        pipeline.DispatchMerge(12, 13, 14, 16, 16, cascadeIndex: 0, mergeFactor: 0.5f);
        pipeline.DispatchApply(20, 14, 21, 16, 16, intensity: 0.75f);

        Assert.Contains("Image:0=12:WriteOnly:Rgba16f", backend.Events);
        Assert.Contains("Uniform1i:rc_cascade_build:uRayCount=16", backend.Events);
        Assert.Contains("Uniform1i:rc_cascade_build:uMaxRaySteps=4", backend.Events);
        Assert.Contains("Uniform1f:rc_merge:uMergeFactor=0.5", backend.Events);
        Assert.Contains("Image:0=21:WriteOnly:Rgba8", backend.Events);
        Assert.Contains("Uniform1f:rc_apply:uRadianceIntensity=0.75", backend.Events);
    }

    private sealed class RecordingComputeBackend : IComputeBackend
    {
        private uint _nextHandle = 1;

        public List<string> LoadedKernelNames { get; } = [];

        public List<string> Events { get; } = [];

        public ComputeBackendKind Kind => ComputeBackendKind.GlCompute;

        public bool IsAvailable => true;

        public ComputeKernel LoadKernel(string name, string source)
        {
            LoadedKernelNames.Add(name);
            return new ComputeKernel(name, _nextHandle++);
        }

        public void BindStorageBuffer(uint bindingIndex, uint bufferHandle)
        {
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

        public void DeleteTimerQuery(uint queryHandle)
        {
        }

        public void Dispose()
        {
        }
    }
}
