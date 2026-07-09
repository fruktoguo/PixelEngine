using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// plan/09 CP-B1..CP-B5 与 CP-L0 的 compute kernel 加载与 dispatch 编排。
/// </summary>
/// <remarks>
/// 该管线只在渲染相位 10 使用，不读取 CPU 权威模拟数据，也不把 GPU 结果回读进 sim。
/// </remarks>
public sealed class GpuComputeBloomPipeline
{
    private readonly IComputeBackend _backend;
    private readonly ComputeKernel _brightPass;
    private readonly ComputeKernel _downsample;
    private readonly ComputeKernel _dualKawaseDown;
    private readonly ComputeKernel _dualKawaseUp;
    private readonly ComputeKernel _upsampleComposite;
    private readonly ComputeKernel _lightComposite;

    /// <summary>
    /// 加载首批 bloom/lighting compute kernels。
    /// </summary>
    /// <param name="backend">compute 后端。</param>
    public GpuComputeBloomPipeline(IComputeBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _brightPass = Load(GpuComputeShaderSources.BloomBrightPassName);
        _downsample = Load(GpuComputeShaderSources.BloomDownsampleName);
        _dualKawaseDown = Load(GpuComputeShaderSources.BloomDualKawaseDownName);
        _dualKawaseUp = Load(GpuComputeShaderSources.BloomDualKawaseUpName);
        _upsampleComposite = Load(GpuComputeShaderSources.BloomUpsampleCompositeName);
        _lightComposite = Load(GpuComputeShaderSources.LightCompositeName);
    }

    /// <summary>
    /// 执行 CP-B1 bright-pass。
    /// </summary>
    public void DispatchBrightPass(uint sourceTexture, uint outputImage, int width, int height, float threshold)
    {
        ValidateHandlePair(sourceTexture, outputImage);
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.BindTexture(0, sourceTexture);
        BindOutput(outputImage);
        _backend.SetUniform1(_brightPass, "uSourceTexture", 0);
        _backend.SetUniform2(_brightPass, "uOutputSize", width, height);
        _backend.SetUniform1(_brightPass, "uThreshold", threshold);
        _backend.Dispatch(_brightPass, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        InsertImageBarrier();
    }

    /// <summary>
    /// 执行 CP-B2 downsample。
    /// </summary>
    public void DispatchDownsample(uint sourceTexture, uint outputImage, int width, int height, float texelX, float texelY)
    {
        ValidateHandlePair(sourceTexture, outputImage);
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.BindTexture(0, sourceTexture);
        BindOutput(outputImage);
        _backend.SetUniform1(_downsample, "uSourceTexture", 0);
        _backend.SetUniform2(_downsample, "uOutputSize", width, height);
        _backend.SetUniform2(_downsample, "uSourceTexelSize", texelX, texelY);
        _backend.Dispatch(_downsample, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        InsertImageBarrier();
    }

    /// <summary>
    /// 执行 CP-B3 dual-Kawase down。
    /// </summary>
    public void DispatchDualKawaseDown(uint sourceTexture, uint outputImage, int width, int height, float texelX, float texelY, float offset)
    {
        DispatchKawase(_dualKawaseDown, sourceTexture, outputImage, width, height, texelX, texelY, offset, intensity: null);
    }

    /// <summary>
    /// 执行 CP-B4 dual-Kawase up。
    /// </summary>
    public void DispatchDualKawaseUp(
        uint sourceTexture,
        uint baseTexture,
        uint outputImage,
        int width,
        int height,
        float texelX,
        float texelY,
        float offset,
        float intensity)
    {
        ValidateHandleTriple(sourceTexture, baseTexture, outputImage);
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.BindTexture(0, sourceTexture);
        _backend.BindTexture(1, baseTexture);
        BindOutput(outputImage);
        _backend.SetUniform1(_dualKawaseUp, "uSourceTexture", 0);
        _backend.SetUniform1(_dualKawaseUp, "uBaseTexture", 1);
        _backend.SetUniform2(_dualKawaseUp, "uOutputSize", width, height);
        _backend.SetUniform2(_dualKawaseUp, "uTexelSize", texelX, texelY);
        _backend.SetUniform1(_dualKawaseUp, "uOffset", offset);
        _backend.SetUniform1(_dualKawaseUp, "uIntensity", intensity);
        _backend.Dispatch(_dualKawaseUp, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        InsertImageBarrier();
    }

    /// <summary>
    /// 执行 CP-B5 bloom upsample composite。
    /// </summary>
    public void DispatchUpsampleComposite(uint sceneTexture, uint bloomTexture, uint outputImage, int width, int height, float bloomIntensity)
    {
        ValidateHandlePair(sceneTexture, bloomTexture);
        ValidateHandle(outputImage);
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.BindTexture(0, sceneTexture);
        _backend.BindTexture(1, bloomTexture);
        BindOutput(outputImage);
        _backend.SetUniform1(_upsampleComposite, "uSceneTexture", 0);
        _backend.SetUniform1(_upsampleComposite, "uBloomTexture", 1);
        _backend.SetUniform2(_upsampleComposite, "uOutputSize", width, height);
        _backend.SetUniform1(_upsampleComposite, "uBloomIntensity", bloomIntensity);
        _backend.Dispatch(_upsampleComposite, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        InsertImageBarrier();
    }

    /// <summary>
    /// 执行 CP-L0 light composite。
    /// </summary>
    public void DispatchLightComposite(uint worldTexture, uint visibilityTexture, uint emissiveTexture, uint outputImage, int width, int height, float exposure)
    {
        ValidateHandleQuad(worldTexture, visibilityTexture, emissiveTexture, outputImage);
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.BindTexture(0, worldTexture);
        _backend.BindTexture(1, visibilityTexture);
        _backend.BindTexture(2, emissiveTexture);
        BindOutput(outputImage);
        _backend.SetUniform1(_lightComposite, "uWorldTexture", 0);
        _backend.SetUniform1(_lightComposite, "uVisibilityTexture", 1);
        _backend.SetUniform1(_lightComposite, "uEmissiveTexture", 2);
        _backend.SetUniform2(_lightComposite, "uOutputSize", width, height);
        _backend.SetUniform1(_lightComposite, "uExposure", exposure);
        _backend.Dispatch(_lightComposite, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        InsertImageBarrier();
    }

    private ComputeKernel Load(string passName)
    {
        return _backend.LoadKernel(passName, GpuComputeShaderSources.GetSource(passName));
    }

    private void DispatchKawase(
        ComputeKernel kernel,
        uint sourceTexture,
        uint outputImage,
        int width,
        int height,
        float texelX,
        float texelY,
        float offset,
        float? intensity)
    {
        ValidateHandlePair(sourceTexture, outputImage);
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.BindTexture(0, sourceTexture);
        BindOutput(outputImage);
        _backend.SetUniform1(kernel, "uSourceTexture", 0);
        _backend.SetUniform2(kernel, "uOutputSize", width, height);
        _backend.SetUniform2(kernel, "uTexelSize", texelX, texelY);
        _backend.SetUniform1(kernel, "uOffset", offset);
        if (intensity.HasValue)
        {
            _backend.SetUniform1(kernel, "uIntensity", intensity.Value);
        }

        _backend.Dispatch(kernel, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        InsertImageBarrier();
    }

    // 所有 bloom/light pass 统一将 RGBA8 输出绑定到 image unit 0。
    private void BindOutput(uint outputImage)
    {
        _backend.BindImage(0, outputImage, level: 0, layered: false, layer: 0, GLEnum.WriteOnly, GLEnum.Rgba8);
    }

    // dispatch 后屏障：保证后续 pass 的 texture/image 采样可见本次写入。
    private void InsertImageBarrier()
    {
        _backend.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    private static void ValidateHandle(uint handle)
    {
        if (handle == 0)
        {
            throw new ArgumentException("compute 纹理句柄不能为 0。", nameof(handle));
        }
    }

    private static void ValidateHandlePair(uint first, uint second)
    {
        ValidateHandle(first);
        ValidateHandle(second);
    }

    private static void ValidateHandleTriple(uint first, uint second, uint third)
    {
        ValidateHandle(first);
        ValidateHandle(second);
        ValidateHandle(third);
    }

    private static void ValidateHandleQuad(uint first, uint second, uint third, uint fourth)
    {
        ValidateHandle(first);
        ValidateHandle(second);
        ValidateHandle(third);
        ValidateHandle(fourth);
    }
}
