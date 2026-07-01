using PixelEngine.Core;
using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class GpuComputeDispatchGridTests
{
    [Theory]
    [InlineData(1, 1, 1u, 1u)]
    [InlineData(16, 16, 1u, 1u)]
    [InlineData(17, 16, 2u, 1u)]
    [InlineData(1920, 1080, 120u, 68u)]
    public void ForTexture2DCoversOutputWithCeilGroups(int width, int height, uint expectedX, uint expectedY)
    {
        ComputeDispatchSize size = GpuComputeDispatchGrid.ForTexture2D(width, height);

        Assert.Equal(expectedX, size.GroupsX);
        Assert.Equal(expectedY, size.GroupsY);
        Assert.Equal(1u, size.GroupsZ);
    }

    [Fact]
    public void WorkGroupSizeComesFromEngineConstants()
    {
        Assert.Equal(EngineConstants.GpuComputeWorkGroupSizeX, GpuComputeDispatchGrid.LocalSizeX);
        Assert.Equal(EngineConstants.GpuComputeWorkGroupSizeY, GpuComputeDispatchGrid.LocalSizeY);
        Assert.Equal(EngineConstants.GpuComputeWorkGroupSizeZ, GpuComputeDispatchGrid.LocalSizeZ);
    }

    [Fact]
    public void ValidateLocalSizeRejectsDeviceBelowConfiguredLocalSize()
    {
        GpuCapabilities tooSmall = new(
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GpuComputeDispatchGrid.ValidateLocalSize(tooSmall));
        Assert.Contains("X", exception.Message, StringComparison.Ordinal);
    }
}
