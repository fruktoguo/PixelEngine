using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// GPU 计算能力测试：设备特性与着色器编译门控。
/// </summary>
public sealed class GpuComputeCapabilityTests
{
    /// <summary>
    /// 验证Desktop Gl43Core Reports Gl Compute Base Snapshot Inputs。
    /// </summary>
    [Fact]
    public void DesktopGl43CoreReportsGlComputeBaseSnapshotInputs()
    {
        GlCapabilities gl = GlCapabilities.FromRaw("4.3 Mesa", "renderer", "vendor", []);

        GpuCapabilities gpu = GpuCapabilities.FromGlCapabilities(gl);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            gpu,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.False(gpu.IsGles);
        Assert.False(gpu.IsAngle);
        Assert.True(gpu.HasComputeShader);
        Assert.True(gpu.HasShaderStorageBufferObject);
        Assert.True(gpu.HasShaderImageLoadStore);
        Assert.False(GpuComputeDispatchGrid.HasQueriedDeviceLimits(in gpu));
        Assert.False(gate.GlComputeAvailable);
        Assert.True(gate.BaselineFallback);
    }

    /// <summary>
    /// 验证Angle Gles30Forces Baseline Fallback。
    /// </summary>
    [Fact]
    public void AngleGles30ForcesBaselineFallback()
    {
        GlCapabilities gl = GlCapabilities.FromRaw("OpenGL ES 3.0 ANGLE", "ANGLE renderer", "Google", []);
        GpuCapabilities gpu = GpuCapabilities.FromGlCapabilities(gl);

        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            gpu,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.True(gpu.IsGles);
        Assert.True(gpu.IsAngle);
        Assert.False(gate.GlComputeAvailable);
        Assert.True(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.Null, gate.SelectedBackend);
        Assert.Equal(ComputeFeatureSwitches.Disabled, gate.FeatureSwitches);
    }

    /// <summary>
    /// 验证Gl Compute选择Enabled Feature Switches。
    /// </summary>
    [Fact]
    public void GlComputeSelectsEnabledFeatureSwitches()
    {
        GpuCapabilities gpu = new(
            glMajorVersion: 4,
            glMinorVersion: 6,
            isGles: false,
            isAngle: false,
            hasComputeShader: true,
            hasShaderStorageBufferObject: true,
            hasShaderImageLoadStore: true,
            maxWorkGroupCountX: 65_535,
            maxWorkGroupCountY: 65_535,
            maxWorkGroupCountZ: 65_535,
            maxWorkGroupSizeX: 1024,
            maxWorkGroupSizeY: 1024,
            maxWorkGroupSizeZ: 64,
            isWindows: true,
            isDx12Available: false,
            isComputeSharpCompiled: false);

        ComputeFeatureSwitches features = new(
            BloomComputeEnabled: true,
            RadianceCascadesEnabled: true,
            GpuParticlesEnabled: true,
            NonAuthoritativeAirEnabled: false);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpu, features, preferComputeSharp: false);

        Assert.True(gate.GlComputeAvailable);
        Assert.False(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.GlCompute, gate.SelectedBackend);
        Assert.Equal(features, gate.FeatureSwitches);
    }

    /// <summary>
    /// 验证Compute Sharp Requires Explicit Preference Resource Contract And Executable Backend。
    /// </summary>
    [Fact]
    public void ComputeSharpRequiresExplicitPreferenceResourceContractAndExecutableBackend()
    {
        GpuCapabilities gpu = new(
            glMajorVersion: 4,
            glMinorVersion: 1,
            isGles: false,
            isAngle: false,
            hasComputeShader: false,
            hasShaderStorageBufferObject: false,
            hasShaderImageLoadStore: false,
            maxWorkGroupCountX: 0,
            maxWorkGroupCountY: 0,
            maxWorkGroupCountZ: 0,
            maxWorkGroupSizeX: 0,
            maxWorkGroupSizeY: 0,
            maxWorkGroupSizeZ: 0,
            isWindows: true,
            isDx12Available: true,
            isComputeSharpCompiled: true,
            hasComputeSharpResourceContract: true,
            computeSharpResourceContractKind: GpuResourceContractKind.D3D12RenderGraph);

        ComputeCapabilityGate withoutPreference = ComputeCapabilityGate.Evaluate(
            gpu,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);
        ComputeCapabilityGate withPreference = ComputeCapabilityGate.Evaluate(
            gpu,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: true);

        Assert.True(withoutPreference.BaselineFallback);
        Assert.True(withPreference.BaselineFallback);
        Assert.Equal(ComputeBackendKind.Null, withPreference.SelectedBackend);
        Assert.False(withPreference.ComputeSharpAvailable);
    }

    /// <summary>
    /// 验证Gpu Compute Resources Reject Zero Handles。
    /// </summary>
    [Fact]
    public void GpuComputeResourcesRejectZeroHandles()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new GpuComputeResources(
            width: 16,
            height: 16,
            worldTexture: 1,
            emissiveTexture: 2,
            occluderTexture: 3,
            visibilityTexture: 4,
            sceneTexture: 5,
            litTexture: 6,
            postATexture: 7,
            postBTexture: 0));
        Assert.Contains("句柄", exception.Message, StringComparison.Ordinal);
    }
}
