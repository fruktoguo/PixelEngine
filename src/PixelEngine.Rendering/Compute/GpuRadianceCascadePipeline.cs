using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// Radiance Cascades CP-R0..CP-R3 的 GL compute dispatch 编排。
/// </summary>
public sealed class GpuRadianceCascadePipeline
{
    private readonly IComputeBackend _backend;
    private readonly ComputeKernel _sdfJfa;
    private readonly ComputeKernel _cascadeBuild;
    private readonly ComputeKernel _merge;
    private readonly ComputeKernel _apply;

    /// <summary>
    /// 加载 Radiance Cascades compute kernels。
    /// </summary>
    public GpuRadianceCascadePipeline(IComputeBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _sdfJfa = Load(GpuComputeShaderSources.RadianceCascadeSdfJfaName);
        _cascadeBuild = Load(GpuComputeShaderSources.RadianceCascadeBuildName);
        _merge = Load(GpuComputeShaderSources.RadianceCascadeMergeName);
        _apply = Load(GpuComputeShaderSources.RadianceCascadeApplyName);
    }

    /// <summary>
    /// CP-R0 初始化：occluder/solidity → JFA seed texture。
    /// </summary>
    public void DispatchSdfInitialize(uint occluderTexture, uint outputSdf, int width, int height, float threshold)
    {
        ValidateHandlePair(occluderTexture, outputSdf);
        BindSdf(outputSdf);
        _backend.BindTexture(0, occluderTexture);
        _backend.SetUniform1(_sdfJfa, "uSeedTexture", 0);
        _backend.SetUniform2(_sdfJfa, "uOutputSize", width, height);
        _backend.SetUniform1(_sdfJfa, "uJumpStep", 0);
        _backend.SetUniform1(_sdfJfa, "uInitialize", 1);
        _backend.SetUniform1(_sdfJfa, "uOccluderThreshold", threshold);
        Dispatch(_sdfJfa, width, height, MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    /// <summary>
    /// CP-R0 单步 Jump Flood：上一轮 SDF → 下一轮 SDF。
    /// </summary>
    public void DispatchSdfJump(uint sourceSdf, uint outputSdf, int width, int height, int jumpStep)
    {
        ValidateHandlePair(sourceSdf, outputSdf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jumpStep);
        BindSdf(outputSdf);
        _backend.BindTexture(0, sourceSdf);
        _backend.SetUniform1(_sdfJfa, "uSeedTexture", 0);
        _backend.SetUniform2(_sdfJfa, "uOutputSize", width, height);
        _backend.SetUniform1(_sdfJfa, "uJumpStep", jumpStep);
        _backend.SetUniform1(_sdfJfa, "uInitialize", 0);
        _backend.SetUniform1(_sdfJfa, "uOccluderThreshold", 0.5f);
        Dispatch(_sdfJfa, width, height, MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    /// <summary>
    /// CP-R1：SDF + emissive → 单层 cascade radiance。
    /// </summary>
    public void DispatchCascadeBuild(uint sdfTexture, uint emissiveTexture, uint outputCascade, int width, int height, RadianceCascadeSettings settings, int cascadeIndex)
    {
        settings = settings.Validate();
        ValidateHandleTriple(sdfTexture, emissiveTexture, outputCascade);
        BindRadiance(outputCascade);
        _backend.BindTexture(0, sdfTexture);
        _backend.BindTexture(1, emissiveTexture);
        _backend.SetUniform1(_cascadeBuild, "uSdfTexture", 0);
        _backend.SetUniform1(_cascadeBuild, "uEmissiveTexture", 1);
        _backend.SetUniform2(_cascadeBuild, "uOutputSize", width, height);
        _backend.SetUniform1(_cascadeBuild, "uCascadeIndex", cascadeIndex);
        _backend.SetUniform1(_cascadeBuild, "uRayCount", checked(settings.BaseRayCount << cascadeIndex));
        _backend.SetUniform1(_cascadeBuild, "uCascadeRadius", settings.BaseStepPixels * MathF.Pow(2f, cascadeIndex + 1));
        _backend.SetUniform1(_cascadeBuild, "uMaxRaySteps", settings.MaxRaySteps);
        Dispatch(_cascadeBuild, width, height, MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    /// <summary>
    /// CP-R2：高层 cascade 合并到近层 cascade。
    /// </summary>
    public void DispatchMerge(uint nearCascadeTexture, uint farCascadeTexture, uint outputCascade, int width, int height, int cascadeIndex, float mergeFactor)
    {
        ValidateHandleTriple(nearCascadeTexture, farCascadeTexture, outputCascade);
        BindRadiance(outputCascade);
        _backend.BindTexture(0, nearCascadeTexture);
        _backend.BindTexture(1, farCascadeTexture);
        _backend.SetUniform1(_merge, "uNearCascadeTexture", 0);
        _backend.SetUniform1(_merge, "uFarCascadeTexture", 1);
        _backend.SetUniform2(_merge, "uOutputSize", width, height);
        _backend.SetUniform1(_merge, "uCascadeIndex", cascadeIndex);
        _backend.SetUniform1(_merge, "uMergeFactor", Math.Clamp(mergeFactor, 0f, 1f));
        Dispatch(_merge, width, height, MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    /// <summary>
    /// CP-R3：radiance 应用到 scene 输出。
    /// </summary>
    public void DispatchApply(uint sceneTexture, uint radianceTexture, uint outputTexture, int width, int height, float intensity)
    {
        ValidateHandleTriple(sceneTexture, radianceTexture, outputTexture);
        _backend.BindImage(0, outputTexture, level: 0, layered: false, layer: 0, GLEnum.WriteOnly, GLEnum.Rgba8);
        _backend.BindTexture(0, sceneTexture);
        _backend.BindTexture(1, radianceTexture);
        _backend.SetUniform1(_apply, "uSceneTexture", 0);
        _backend.SetUniform1(_apply, "uRadianceTexture", 1);
        _backend.SetUniform2(_apply, "uOutputSize", width, height);
        _backend.SetUniform1(_apply, "uRadianceIntensity", intensity);
        Dispatch(_apply, width, height, MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    private ComputeKernel Load(string passName)
    {
        return _backend.LoadKernel(passName, GpuComputeShaderSources.GetSource(passName));
    }

    private void BindSdf(uint outputSdf)
    {
        _backend.BindImage(0, outputSdf, level: 0, layered: false, layer: 0, GLEnum.WriteOnly, GLEnum.Rgba32f);
    }

    private void BindRadiance(uint outputCascade)
    {
        _backend.BindImage(0, outputCascade, level: 0, layered: false, layer: 0, GLEnum.WriteOnly, GLEnum.Rgba16f);
    }

    // 通用 16×16 tile dispatch：按视口尺寸计算 work group，dispatch 后插入 image/texture 屏障。
    private void Dispatch(ComputeKernel kernel, int width, int height, MemoryBarrierMask barrier)
    {
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D(width, height);
        _backend.Dispatch(kernel, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        _backend.MemoryBarrier(barrier);
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
}
