using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// plan/09 G1-G4 compute 能力门控与后端选择结果。
/// </summary>
public readonly record struct ComputeCapabilityGate
{
    private ComputeCapabilityGate(
        bool glComputeAvailable,
        bool computeSharpAvailable,
        bool baselineFallback,
        ComputeFeatureSwitches featureSwitches,
        ComputeBackendKind selectedBackend)
    {
        GlComputeAvailable = glComputeAvailable;
        ComputeSharpAvailable = computeSharpAvailable;
        BaselineFallback = baselineFallback;
        FeatureSwitches = featureSwitches;
        SelectedBackend = selectedBackend;
    }

    /// <summary>G1：GL 4.3 compute + SSBO + image load/store 是否可用。</summary>
    public bool GlComputeAvailable { get; }

    /// <summary>G2：Windows/DX12/ComputeSharp 显式启用路径是否可用。</summary>
    public bool ComputeSharpAvailable { get; }

    /// <summary>G3：是否回退到 plan/08 fragment/CPU 基线路径。</summary>
    public bool BaselineFallback { get; }

    /// <summary>G4：逐特性开关。</summary>
    public ComputeFeatureSwitches FeatureSwitches { get; }

    /// <summary>最终选择的后端。</summary>
    public ComputeBackendKind SelectedBackend { get; }

    /// <summary>
    /// 根据能力和用户配置计算门控结果。
    /// </summary>
    /// <param name="capabilities">GPU compute 能力。</param>
    /// <param name="features">逐特性开关。</param>
    /// <param name="preferComputeSharp">是否显式优先选择 ComputeSharp。</param>
    /// <returns>门控结果。</returns>
    public static ComputeCapabilityGate Evaluate(
        GpuCapabilities capabilities,
        ComputeFeatureSwitches features,
        bool preferComputeSharp)
    {
        bool glComputeAvailable =
            !capabilities.IsGles &&
            !capabilities.IsAngle &&
            capabilities.HasComputeShader &&
            capabilities.HasShaderStorageBufferObject &&
            capabilities.HasShaderImageLoadStore &&
            GpuComputeDispatchGrid.IsLocalSizeSupported(in capabilities);
        bool computeSharpAvailable =
            preferComputeSharp &&
            capabilities.IsWindows &&
            capabilities.IsDx12Available &&
            capabilities.IsComputeSharpCompiled &&
            capabilities.HasComputeSharpResourceContract &&
            capabilities.ComputeSharpResourceContractKind != GpuResourceContractKind.OpenGlTextureNames &&
            ComputeSharpBackend.IsExecutable;
        ComputeBackendKind backend = computeSharpAvailable
            ? ComputeBackendKind.ComputeSharp
            : glComputeAvailable
                ? ComputeBackendKind.GlCompute
                : ComputeBackendKind.Null;
        // ARCH-005：air/smoke 目前只有独立 pass 与资源契约，RenderPipeline 尚未拥有
        // seed→dispatch→composite 的完整生命周期。先把该位从生产能力结果中强制清零，
        // 防止诊断/HUD 把未接入的组件报告成运行时能力；接入完成后再移除此边界。
        ComputeFeatureSwitches effectiveFeatures = backend == ComputeBackendKind.Null
            ? ComputeFeatureSwitches.Disabled
            : features with { NonAuthoritativeAirEnabled = false };
        return new ComputeCapabilityGate(
            glComputeAvailable,
            computeSharpAvailable,
            backend == ComputeBackendKind.Null,
            effectiveFeatures,
            backend);
    }

    /// <summary>
    /// 将 G1-G4 门控结果发布到 Core 诊断计数器，供 HUD 与预算监控读取。
    /// </summary>
    /// <param name="counters">Core 诊断计数器。</param>
    public void PublishDiagnostics(EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        counters.SetGpuComputeDiagnostics(
            (long)SelectedBackend,
            ToCounter(GlComputeAvailable),
            ToCounter(ComputeSharpAvailable),
            ToCounter(BaselineFallback),
            ToCounter(FeatureSwitches.BloomComputeEnabled),
            ToCounter(FeatureSwitches.RadianceCascadesEnabled),
            ToCounter(FeatureSwitches.GpuParticlesEnabled),
            ToCounter(FeatureSwitches.NonAuthoritativeAirEnabled));
    }

    private static long ToCounter(bool value)
    {
        return value ? 1L : 0L;
    }
}
