using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderingProjectDisciplineTests
{
    [Fact]
    public void RenderingProjectReferencesRequiredProjectsAndAllowsUnsafe()
    {
        XDocument project = XDocument.Load(ProjectPath("src", "PixelEngine.Rendering", "PixelEngine.Rendering.csproj"));
        string xml = project.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.Core\\PixelEngine.Core.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.Simulation\\PixelEngine.Simulation.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.Content\\PixelEngine.Content.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.World\\PixelEngine.World.csproj", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderWindowDoesNotUseSilkRunLoop()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderWindow.cs"));

        Assert.Contains(".DoEvents()", source, StringComparison.Ordinal);
        Assert.Contains(".SwapBuffers()", source, StringComparison.Ordinal);
        Assert.Contains("VSyncEnabled", source, StringComparison.Ordinal);
        Assert.Contains("_window.VSync", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Run(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderPipelineUsesRealGpuTimestampAndSeparatesPresentWait()
    {
        string pipeline = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));
        string gpuFrame = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GlGpuFrameProfiler.cs"));

        Assert.Contains("IRenderPresentationControl", pipeline, StringComparison.Ordinal);
        Assert.Contains("get => _window.VSyncEnabled", pipeline, StringComparison.Ordinal);
        Assert.Contains("FrameSubPhase.PresentWait", pipeline, StringComparison.Ordinal);
        Assert.Contains("FrameSubPhase.GpuFrame", gpuFrame, StringComparison.Ordinal);
        Assert.Contains("QueryCounterTarget.Timestamp", gpuFrame, StringComparison.Ordinal);
        Assert.Contains("QueryObjectParameterName.ResultAvailable", gpuFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Finish()", gpuFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Flush()", gpuFrame, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingSourcesDoNotReadBackGpuDataIntoSimulation()
    {
        string source = ReadRenderingSources();

        Assert.DoesNotContain("ReadPixels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTexImage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTextureImage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBufferRange(BufferTargetARB.PixelPackBuffer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelPackBuffer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlResourceCountsReportsTotalAndPerKind()
    {
        GlResourceCounts counts = new(
            Textures: 1,
            Buffers: 2,
            Framebuffers: 3,
            ShaderPrograms: 4,
            ComputePrograms: 5,
            Shaders: 6,
            VertexArrays: 7,
            TimerQueries: 8);

        Assert.Equal(36, counts.Total);
        Assert.Equal(1, counts.GetCount(GlResourceKind.Texture));
        Assert.Equal(2, counts.GetCount(GlResourceKind.Buffer));
        Assert.Equal(3, counts.GetCount(GlResourceKind.Framebuffer));
        Assert.Equal(4, counts.GetCount(GlResourceKind.ShaderProgram));
        Assert.Equal(5, counts.GetCount(GlResourceKind.ComputeProgram));
        Assert.Equal(6, counts.GetCount(GlResourceKind.Shader));
        Assert.Equal(7, counts.GetCount(GlResourceKind.VertexArray));
        Assert.Equal(8, counts.GetCount(GlResourceKind.TimerQuery));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => counts.GetCount((GlResourceKind)int.MaxValue));
    }

    [Fact]
    public void GlResourceTrackerCoversRenderingOwnedNativeObjects()
    {
        string tracker = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GlResourceTracker.cs"));
        string texture = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GlTexture.cs"));
        string buffer = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GlBuffer.cs"));
        string framebuffer = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Framebuffer.cs"));
        string shaderProgram = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "ShaderProgram.cs"));
        string fullscreenQuad = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "FullscreenQuad.cs"));
        string gpuParticles = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GpuParticleRenderer.cs"));
        string overlay = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "OverlayRenderer.cs"));
        string compute = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "GLComputeBackend.cs"));
        string detector = File.ReadAllText(ProjectPath("tools", "PixelEngine.Tools.ManagedNativeLeakDetector", "Program.cs"));

        Assert.Contains("Volatile.Read", tracker, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment", tracker, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Decrement", tracker, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.Texture", texture, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.Texture", texture, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.Buffer", buffer, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.Buffer", buffer, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.Framebuffer", framebuffer, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.Framebuffer", framebuffer, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.ShaderProgram", shaderProgram, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.ShaderProgram", shaderProgram, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.Shader", shaderProgram, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.Shader", shaderProgram, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.VertexArray", fullscreenQuad, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.VertexArray", fullscreenQuad, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.Buffer", fullscreenQuad, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.Buffer", fullscreenQuad, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.VertexArray", gpuParticles, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.VertexArray", gpuParticles, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.VertexArray", overlay, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.VertexArray", overlay, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.ComputeProgram", compute, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.ComputeProgram", compute, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.Shader", compute, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.Shader", compute, StringComparison.Ordinal);
        Assert.Contains("TrackCreated(GlResourceKind.TimerQuery", compute, StringComparison.Ordinal);
        Assert.Contains("TrackDeleted(GlResourceKind.TimerQuery", compute, StringComparison.Ordinal);
        Assert.Contains("RenderWindow.Create", detector, StringComparison.Ordinal);
        Assert.Contains("GlTexture", detector, StringComparison.Ordinal);
        Assert.Contains("GlBuffer", detector, StringComparison.Ordinal);
        Assert.Contains("Framebuffer", detector, StringComparison.Ordinal);
        Assert.Contains("ShaderProgram.Create", detector, StringComparison.Ordinal);
        Assert.Contains("gl_context_rendering_wrappers", detector, StringComparison.Ordinal);
        Assert.Contains("GlResourceTracker.Snapshot().Total", detector, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingDoesNotHardDependOnVulkanGl4OrComputeSharpPackage()
    {
        string source = ReadRenderingSources();
        string project = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PixelEngine.Rendering.csproj"));
        string support = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "ComputeSharpSupport.cs"));

        Assert.DoesNotContain("Vulkan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GLFW_CONTEXT_VERSION_MAJOR, 4", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextVersion = new API(4", source, StringComparison.Ordinal);
        Assert.Contains("<EnableComputeSharpBackend>false</EnableComputeSharpBackend>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ComputeSharp\" Condition=\"'$(EnableComputeSharpBackend)' == 'true'\" />", project, StringComparison.Ordinal);
        Assert.Contains("PIXELENGINE_COMPUTESHARP", support, StringComparison.Ordinal);
        Assert.Contains("ComputeSharp.GraphicsDevice.EnumerateDevices()", support, StringComparison.Ordinal);
        Assert.DoesNotContain("ComputeSharp.GraphicsDevice", source.Replace(support, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeSharpDx12ResourceContractIsDocumentedAndBlockedUntilRealInteropExists()
    {
        string document = File.ReadAllText(ProjectPath("docs", "rendering-computesharp-resource-contract.md"));
        string plan = File.ReadAllText(ProjectPath("plan", "09-gpu-compute.md"));
        string resources = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "GpuComputeResources.cs"));
        string contract = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "ComputeSharpResourceContract.cs"));
        string contractKind = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "GpuResourceContractKind.cs"));
        string capabilities = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "GpuCapabilities.cs"));
        string gate = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "ComputeCapabilityGate.cs"));
        string backend = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "ComputeSharpBackend.cs"));
        string interfaceSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "Compute", "IComputeBackend.cs"));
        string project = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PixelEngine.Rendering.csproj"));

        Assert.Contains("OpenGL texture handle", document, StringComparison.Ordinal);
        Assert.Contains("不能直接消费这些句柄", document, StringComparison.Ordinal);
        Assert.Contains("不得在 `ComputeSharpBackend` 中把 `uint textureHandle` 当作 DX12 资源使用", document, StringComparison.Ordinal);
        Assert.Contains("D3D 渲染后端", document, StringComparison.Ordinal);
        Assert.Contains("GL-DX12 共享资源层", document, StringComparison.Ordinal);
        Assert.Contains("GPU→CPU readback", document, StringComparison.Ordinal);
        Assert.Contains("resize", document, StringComparison.Ordinal);
        Assert.Contains("barrier", document, StringComparison.Ordinal);
        Assert.Contains("fallback", document, StringComparison.Ordinal);
        Assert.Contains("no-readback", document, StringComparison.Ordinal);
        Assert.Contains("IsComputeSharpCompiled=false", document, StringComparison.Ordinal);
        Assert.Contains("HasComputeSharpResourceContract", document, StringComparison.Ordinal);
        Assert.Contains("ComputeSharpBackend.IsExecutable", document, StringComparison.Ordinal);
        Assert.Contains("plan/15", document, StringComparison.Ordinal);

        Assert.Contains("docs/rendering-computesharp-resource-contract.md", plan, StringComparison.Ordinal);
        Assert.Contains("禁止路线", plan, StringComparison.Ordinal);
        Assert.Contains("不能用 GL 句柄模拟 DX12 resource", plan, StringComparison.Ordinal);

        Assert.Contains("OpenGL texture name", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("GL-only 契约", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("D3D resource owner", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("GL-DX12 shared resource/fence 层", interfaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("其它后端可把该值解释为后端资源句柄", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("uint textureHandle", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("plan/08 世界纹理句柄", resources, StringComparison.Ordinal);
        Assert.Contains("ResourceContractKind => GpuResourceContractKind.OpenGlTextureNames", resources, StringComparison.Ordinal);
        Assert.Contains("CanBeConsumedByComputeSharp => false", resources, StringComparison.Ordinal);
        Assert.Contains("D3D12RenderGraph", contractKind, StringComparison.Ordinal);
        Assert.Contains("GlDx12SharedResources", contractKind, StringComparison.Ordinal);
        Assert.Contains("ComputeSharpResourceContractKind", capabilities, StringComparison.Ordinal);
        Assert.Contains("不能声明为 OpenGL texture name", capabilities, StringComparison.Ordinal);
        Assert.Contains("ArgumentOutOfRangeException", capabilities, StringComparison.Ordinal);
        Assert.Contains("ComputeSharpResourceContractKind != GpuResourceContractKind.OpenGlTextureNames", gate, StringComparison.Ordinal);
        Assert.Contains("不能使用 OpenGL texture name", contract, StringComparison.Ordinal);
        Assert.Contains("ValidateKind", contract, StringComparison.Ordinal);
        Assert.Contains("未知的 ComputeSharp/DX12 资源契约类型", contract, StringComparison.Ordinal);
        Assert.Contains("FenceHandle", contract, StringComparison.Ordinal);
        Assert.Contains("CreateD3D12", contract, StringComparison.Ordinal);
        Assert.Contains("CreateGlDx12Shared", contract, StringComparison.Ordinal);
        Assert.Contains("资源契约", backend, StringComparison.Ordinal);
        Assert.Contains("ComputeSharpResourceContract", backend, StringComparison.Ordinal);
        Assert.Contains("真实可执行实现", backend, StringComparison.Ordinal);
        Assert.Contains("IsExecutable = false", backend, StringComparison.Ordinal);
        Assert.Contains("IsAvailable => false", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("using ComputeSharp", backend, StringComparison.Ordinal);
        Assert.Contains("PackageReference Include=\"ComputeSharp\" Condition=\"'$(EnableComputeSharpBackend)' == 'true'\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingHotPathSourcesAvoidLinqAndIteratorAllocations()
    {
        string source = string.Join(
            Environment.NewLine,
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderBufferBuilder.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PaletteBgraConverter.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "BgraColorMixer.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "ParticleCompositor.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "OverlayRenderer.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "BloomPass.cs")));

        Assert.DoesNotContain("System.Linq", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("yield return", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Action<", source.Replace("event Action<GL>?", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void BgraColorNoiseMixingHasSimdFallbackAndBenchmark()
    {
        string mixer = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "BgraColorMixer.cs"));
        string builder = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderBufferBuilder.cs"));
        string benchmark = File.ReadAllText(ProjectPath("bench", "PixelEngine.Benchmarks", "PaletteBgraConversionBenchmarks.cs"));

        Assert.Contains("Sse41.IsSupported", mixer, StringComparison.Ordinal);
        Assert.Contains("Ssse3.IsSupported", mixer, StringComparison.Ordinal);
        Assert.Contains("ApplyColorNoiseScalar", mixer, StringComparison.Ordinal);
        Assert.Contains("BgraColorMixer.ApplyColorNoise", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("!hot.HasColorNoise", builder, StringComparison.Ordinal);
        Assert.Contains("BgraColorNoiseBenchmarks", benchmark, StringComparison.Ordinal);
    }

    [Fact]
    public void PaletteBgraConversionHasSimdFallbackAndBenchmark()
    {
        string converter = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PaletteBgraConverter.cs"));
        string benchmark = File.ReadAllText(ProjectPath("bench", "PixelEngine.Benchmarks", "PaletteBgraConversionBenchmarks.cs"));

        Assert.Contains("Ssse3.IsSupported", converter, StringComparison.Ordinal);
        Assert.Contains("TryConvertSsse3Shuffle16", converter, StringComparison.Ordinal);
        Assert.Contains("Shuffle", converter, StringComparison.Ordinal);
        Assert.Contains("Avx2.IsSupported", converter, StringComparison.Ordinal);
        Assert.Contains("GatherVector256", converter, StringComparison.Ordinal);
        Assert.Contains("ConvertScalar", converter, StringComparison.Ordinal);
        Assert.Contains("Params(16, 256)", benchmark, StringComparison.Ordinal);
        Assert.Contains("Convert()", benchmark, StringComparison.Ordinal);
        Assert.Contains("MemoryDiagnoser", benchmark, StringComparison.Ordinal);
        Assert.Contains("ConvertAvx2Experimental", benchmark, StringComparison.Ordinal);
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

    private static string ReadRenderingSources()
    {
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(ProjectPath("src", "PixelEngine.Rendering"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));
    }
}
