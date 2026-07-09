using PixelEngine.Rendering.Compute;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// GPU 空气烟雾管线测试：调度网格、资源绑定与输出契约。
/// </summary>
public sealed class GpuAirSmokePipelineTests
{
    /// <summary>
    /// 验证Air Smoke Settings Default Off And Validates Range。
    /// </summary>
    [Fact]
    public void AirSmokeSettingsDefaultOffAndValidatesRange()
    {
        Assert.False(AirSmokeSettings.Default.Enabled);

        RenderPipelineSettings pipelineSettings = new()
        {
            AirSmoke = AirSmokeSettings.Default with { Diffusion = 2f },
        };

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(pipelineSettings.Validate);
        Assert.Contains(nameof(AirSmokeSettings.Diffusion), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Constructor Loads Air Smoke Kernel Only。
    /// </summary>
    [Fact]
    public void ConstructorLoadsAirSmokeKernelOnly()
    {
        RecordingComputeBackend backend = new();

        _ = new GpuAirSmokePipeline(backend);

        Assert.Equal([GpuComputeShaderSources.AirSmokeDiffuseMargolusName], backend.LoadedKernelNames);
    }

    /// <summary>
    /// 验证Dispatch Margolus Step Binds Independent Density Images And Parity。
    /// </summary>
    [Fact]
    public void DispatchMargolusStepBindsIndependentDensityImagesAndParity()
    {
        RecordingComputeBackend backend = new();
        GpuAirSmokePipeline pipeline = new(backend);
        backend.Events.Clear();

        pipeline.DispatchMargolusStep(
            sourceDensity: 10,
            destinationDensity: 11,
            width: 64,
            height: 32,
            parity: 3,
            settings: AirSmokeSettings.Default with { Enabled = true, Diffusion = 0.5f });

        Assert.Contains("Image:0=10:ReadOnly:R16f", backend.Events);
        Assert.Contains("Image:1=11:WriteOnly:R16f", backend.Events);
        Assert.Contains("Uniform2i:air_diffuse_margolus:uOutputSize=64,32", backend.Events);
        Assert.Contains("Uniform1i:air_diffuse_margolus:uParity=1", backend.Events);
        Assert.Contains("Uniform1f:air_diffuse_margolus:uDiffusion=0.5", backend.Events);
        Assert.Contains("Dispatch:air_diffuse_margolus=2,1,1", backend.Events);
        Assert.Contains(backend.Events, static item =>
            item.StartsWith("Barrier:", StringComparison.Ordinal) &&
            item.Contains("ShaderImageAccessBarrierBit", StringComparison.Ordinal) &&
            item.Contains("TextureFetchBarrierBit", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证Source Documents Non Authoritative No Readback Contract。
    /// </summary>
    [Fact]
    public void SourceDocumentsNonAuthoritativeNoReadbackContract()
    {
        string pass = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "AirSmokePass.cs"));
        string resources = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "AirSmokeResources.cs"));

        Assert.Contains("非权威", pass, StringComparison.Ordinal);
        Assert.Contains("绝不作为 CPU sim 输入", pass, StringComparison.Ordinal);
        Assert.Contains("CPU→GPU 单向播种", pass, StringComparison.Ordinal);
        Assert.Contains("独立于 CPU 权威网格", resources, StringComparison.Ordinal);
        Assert.Contains("CPU→GPU 单向播种", resources, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Source And Pipeline Use Same Image Format And Uniform Names。
    /// </summary>
    [Fact]
    public void SourceAndPipelineUseSameImageFormatAndUniformNames()
    {
        string shaderSource = GpuComputeShaderSources.AirSmokeDiffuseMargolus;
        string pipelineSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "GpuAirSmokePipeline.cs"));
        string resourcesSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "AirSmokeResources.cs"));

        Assert.Contains("layout(r16f", shaderSource, StringComparison.Ordinal);
        Assert.Contains("GLEnum.R16f", pipelineSource, StringComparison.Ordinal);
        Assert.Contains("InternalFormat.R16f", resourcesSource, StringComparison.Ordinal);
        Assert.Contains("\"uDiffusion\"", pipelineSource, StringComparison.Ordinal);
        Assert.Contains("uniform float uDiffusion", shaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("uDiffusionRate", shaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("uDissipation", shaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("uDissipation", pipelineSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Dispatch不会Allocate Managed Memory。
    /// </summary>
    [Fact]
    public void DispatchDoesNotAllocateManagedMemory()
    {
        SilentComputeBackend backend = new();
        GpuAirSmokePipeline pipeline = new(backend);
        AirSmokeSettings settings = AirSmokeSettings.Default with { Enabled = true, Diffusion = 0.5f };

        pipeline.DispatchMargolusStep(1, 2, 16, 16, 0, settings);

        long before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.DispatchMargolusStep(1, 2, 16, 16, 1, settings);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
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
        private uint _nextHandle = 1;

        public List<string> LoadedKernelNames { get; } = [];

        public List<string> Events { get; } = [];

        public ComputeBackendKind Kind => ComputeBackendKind.GlCompute;

        public bool IsAvailable => true;

        public ComputeKernel LoadKernel(string name, string source)
        {
            LoadedKernelNames.Add(name);
            Assert.Contains("#version 430", source, StringComparison.Ordinal);
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

    private sealed class SilentComputeBackend : IComputeBackend
    {
        private uint _nextHandle = 1;

        public ComputeBackendKind Kind => ComputeBackendKind.GlCompute;

        public bool IsAvailable => true;

        public ComputeKernel LoadKernel(string name, string source)
        {
            return new ComputeKernel(name, _nextHandle++);
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
