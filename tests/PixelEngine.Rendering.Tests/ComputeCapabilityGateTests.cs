using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class ComputeCapabilityGateTests
{
    [Fact]
    public void DesktopGl43WithRequiredCapabilitiesSelectsGlCompute()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 4,
            glMinor: 3,
            hasCompute: true,
            hasSsbo: true,
            hasImageLoadStore: true);

        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.True(gate.GlComputeAvailable);
        Assert.False(gate.ComputeSharpAvailable);
        Assert.False(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.GlCompute, gate.SelectedBackend);
        Assert.True(gate.FeatureSwitches.BloomComputeEnabled);
    }

    [Fact]
    public void Gl33FallsBackToPlan08Baseline()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 3,
            glMinor: 3,
            hasCompute: false,
            hasSsbo: false,
            hasImageLoadStore: false);

        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.False(gate.GlComputeAvailable);
        Assert.True(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.Null, gate.SelectedBackend);
        Assert.Equal(ComputeFeatureSwitches.Disabled, gate.FeatureSwitches);
    }

    [Fact]
    public void MacGl41FallsBackToPlan08Baseline()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 4,
            glMinor: 1,
            hasCompute: false,
            hasSsbo: false,
            hasImageLoadStore: false);

        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.False(gate.GlComputeAvailable);
        Assert.True(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.Null, gate.SelectedBackend);
        Assert.Equal(ComputeFeatureSwitches.Disabled, gate.FeatureSwitches);
    }

    [Fact]
    public void GlComputeFallsBackWhenDeviceWorkGroupLimitIsBelowEngineLocalSize()
    {
        GpuCapabilities capabilities = new(
            glMajorVersion: 4,
            glMinorVersion: 3,
            isGles: false,
            isAngle: false,
            hasComputeShader: true,
            hasShaderStorageBufferObject: true,
            hasShaderImageLoadStore: true,
            maxWorkGroupCountX: 65_535,
            maxWorkGroupCountY: 65_535,
            maxWorkGroupCountZ: 65_535,
            maxWorkGroupSizeX: 8,
            maxWorkGroupSizeY: 16,
            maxWorkGroupSizeZ: 1,
            isWindows: false,
            isDx12Available: false,
            isComputeSharpCompiled: false);

        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.False(gate.GlComputeAvailable);
        Assert.True(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.Null, gate.SelectedBackend);
    }

    [Fact]
    public void AngleContextFallsBackEvenWhenComputeFlagIsPresent()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 3,
            glMinor: 1,
            hasCompute: true,
            hasSsbo: true,
            hasImageLoadStore: true,
            isGles: true,
            isAngle: true);

        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        Assert.False(gate.GlComputeAvailable);
        Assert.True(gate.BaselineFallback);
        Assert.Equal(ComputeBackendKind.Null, gate.SelectedBackend);
    }

    [Fact]
    public void DisabledComputeCreatesNullBackendWithoutGlEntryPoint()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 3,
            glMinor: 3,
            hasCompute: false,
            hasSsbo: false,
            hasImageLoadStore: false);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);

        using IComputeBackend backend = ComputeBackendFactory.Create(gl: null, gate);

        Assert.Equal(ComputeBackendKind.Null, backend.Kind);
        Assert.False(backend.IsAvailable);
    }

    [Fact]
    public void ComputeSharpGateRequiresExplicitAvailability()
    {
        GpuCapabilities notCompiled = CreateCapabilities(
            glMajor: 4,
            glMinor: 3,
            hasCompute: true,
            hasSsbo: true,
            hasImageLoadStore: true,
            isWindows: true,
            isDx12Available: true,
            isComputeSharpCompiled: false);
        GpuCapabilities compiled = notCompiled with { IsComputeSharpCompiled = true };

        ComputeCapabilityGate disabledGate = ComputeCapabilityGate.Evaluate(
            notCompiled,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: true);
        ComputeCapabilityGate enabledGate = ComputeCapabilityGate.Evaluate(
            compiled,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: true);

        Assert.False(disabledGate.ComputeSharpAvailable);
        Assert.Equal(ComputeBackendKind.GlCompute, disabledGate.SelectedBackend);
        Assert.True(enabledGate.ComputeSharpAvailable);
        Assert.Equal(ComputeBackendKind.ComputeSharp, enabledGate.SelectedBackend);
    }

    [Fact]
    public void ComputeSharpFactorySelectionReturnsUnavailableStubUntilBackendIsCompiled()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 4,
            glMinor: 3,
            hasCompute: true,
            hasSsbo: true,
            hasImageLoadStore: true,
            isWindows: true,
            isDx12Available: true,
            isComputeSharpCompiled: true);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: true);

        using IComputeBackend backend = ComputeBackendFactory.Create(gl: null, gate);

        Assert.Equal(ComputeBackendKind.ComputeSharp, backend.Kind);
        Assert.False(backend.IsAvailable);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => backend.LoadKernel("stub", "unused"));
        Assert.Contains("尚未编译进当前发行", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GatePublishesSelectedBackendAndFeatureSwitchesToCoreCounters()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 4,
            glMinor: 3,
            hasCompute: true,
            hasSsbo: true,
            hasImageLoadStore: true);
        ComputeFeatureSwitches features = new(
            BloomComputeEnabled: true,
            RadianceCascadesEnabled: true,
            GpuParticlesEnabled: true,
            NonAuthoritativeAirEnabled: true);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            features,
            preferComputeSharp: false);
        EngineCounters counters = new();

        gate.PublishDiagnostics(counters);

        Assert.Equal((long)ComputeBackendKind.GlCompute, counters.GpuComputeSelectedBackend);
        Assert.Equal(1, counters.GpuComputeGlAvailable);
        Assert.Equal(0, counters.GpuComputeSharpAvailable);
        Assert.Equal(0, counters.GpuComputeBaselineFallback);
        Assert.Equal(1, counters.GpuComputeBloomEnabled);
        Assert.Equal(1, counters.GpuComputeRadianceCascadesEnabled);
        Assert.Equal(1, counters.GpuComputeParticlesEnabled);
        Assert.Equal(1, counters.GpuComputeAirSmokeEnabled);
    }

    [Fact]
    public void BaselineFallbackPublishesDisabledFeatureSwitches()
    {
        GpuCapabilities capabilities = CreateCapabilities(
            glMajor: 3,
            glMinor: 3,
            hasCompute: false,
            hasSsbo: false,
            hasImageLoadStore: false);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: false);
        EngineCounters counters = new();

        gate.PublishDiagnostics(counters);

        Assert.Equal((long)ComputeBackendKind.Null, counters.GpuComputeSelectedBackend);
        Assert.Equal(0, counters.GpuComputeGlAvailable);
        Assert.Equal(0, counters.GpuComputeSharpAvailable);
        Assert.Equal(1, counters.GpuComputeBaselineFallback);
        Assert.Equal(0, counters.GpuComputeBloomEnabled);
        Assert.Equal(0, counters.GpuComputeRadianceCascadesEnabled);
        Assert.Equal(0, counters.GpuComputeParticlesEnabled);
        Assert.Equal(0, counters.GpuComputeAirSmokeEnabled);
    }

    [Fact]
    public void RealGlCapabilitiesKeepComputeSharpDisabledWithoutPackageReference()
    {
        GlCapabilities gl = GlCapabilities.FromRaw(
            "4.4.0 NVIDIA",
            "renderer",
            "vendor",
            []);

        GpuCapabilities capabilities = GpuCapabilities.FromGlCapabilities(gl);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(
            capabilities,
            ComputeFeatureSwitches.Default,
            preferComputeSharp: true);

        Assert.False(capabilities.IsComputeSharpCompiled);
        Assert.False(gate.ComputeSharpAvailable);
        Assert.Equal(ComputeBackendKind.GlCompute, gate.SelectedBackend);
    }

    [Fact]
    public void ResourcesSnapshotValidatesPositiveSize()
    {
        GpuComputeResources resources = new(
            width: 320,
            height: 180,
            worldTexture: 1,
            emissiveTexture: 2,
            occluderTexture: 3,
            visibilityTexture: 4,
            sceneTexture: 5,
            litTexture: 6,
            postATexture: 7,
            postBTexture: 8);

        Assert.Equal(320, resources.Width);
        Assert.Equal(180, resources.Height);
        Assert.Equal(8u, resources.PostBTexture);
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new GpuComputeResources(0, 1, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Fact]
    public void NullBackendIsNoopAndNeverReportsTimerResult()
    {
        using NullComputeBackend backend = new();

        ComputeKernel kernel = backend.LoadKernel("noop", string.Empty);
        backend.BindStorageBuffer(0, 0);
        backend.Dispatch(kernel, 1, 1, 1);
        backend.EndTimerQuery();

        Assert.Equal(ComputeBackendKind.Null, backend.Kind);
        Assert.False(backend.IsAvailable);
        Assert.Equal(0u, backend.BeginTimerQuery("noop"));
        Assert.False(backend.TryGetTimerResult(0, out ulong elapsed));
        Assert.Equal(0ul, elapsed);
    }

    private static GpuCapabilities CreateCapabilities(
        int glMajor,
        int glMinor,
        bool hasCompute,
        bool hasSsbo,
        bool hasImageLoadStore,
        bool isGles = false,
        bool isAngle = false,
        bool isWindows = false,
        bool isDx12Available = false,
        bool isComputeSharpCompiled = false)
    {
        return new GpuCapabilities(
            glMajor,
            glMinor,
            isGles,
            isAngle,
            hasCompute,
            hasSsbo,
            hasImageLoadStore,
            maxWorkGroupCountX: 65535,
            maxWorkGroupCountY: 65535,
            maxWorkGroupCountZ: 65535,
            maxWorkGroupSizeX: 1024,
            maxWorkGroupSizeY: 1024,
            maxWorkGroupSizeZ: 64,
            isWindows,
            isDx12Available,
            isComputeSharpCompiled);
    }
}
