using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 遮挡图测试：静态遮挡写入、查询与脏区传播。
/// </summary>
public sealed class OccluderMapTests
{
    /// <summary>
    /// 验证Constructor Copies Solidity With Explicit Size。
    /// </summary>
    [Fact]
    public void ConstructorCopiesSolidityWithExplicitSize()
    {
        byte[] source = [0, 10, 20, 30, 40, 50];

        OccluderMap map = new(3, 2, source);

        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(6, map.Length);
        Assert.Equal(30, map.Get(0, 1));
        source[3] = 99;
        Assert.Equal(30, map.Get(0, 1));
    }

    /// <summary>
    /// 验证Copy From Render Aux Buffers Uses Cpu Occluder Channel。
    /// </summary>
    [Fact]
    public void CopyFromRenderAuxBuffersUsesCpuOccluderChannel()
    {
        RenderAuxBuffers aux = new(2, 2);
        aux.Occluder[0] = 11;
        aux.Occluder[3] = 44;
        OccluderMap map = new(2, 2);

        map.CopyFrom(aux);

        Assert.Equal(11, map.Get(0, 0));
        Assert.Equal(44, map.Get(1, 1));
    }

    /// <summary>
    /// 验证Set Get And Clear Use Screen Coordinates。
    /// </summary>
    [Fact]
    public void SetGetAndClearUseScreenCoordinates()
    {
        OccluderMap map = new(4, 3);

        map.Set(2, 1, 200);

        Assert.Equal(200, map.Get(2, 1));
        Assert.Equal(0, map.Get(1, 1));
        map.Clear();
        Assert.Equal(0, map.Get(2, 1));
    }

    /// <summary>
    /// 验证Size Validation Rejects Invalid Or Mismatched Input。
    /// </summary>
    [Fact]
    public void SizeValidationRejectsInvalidOrMismatchedInput()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new OccluderMap(0, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => new OccluderMap(1, 0));

        OccluderMap map = new(2, 2);

        AssertThrows<ArgumentException>(() => map.CopyFrom([1, 2, 3, 4], 4, 1));
        AssertThrows<ArgumentException>(() => map.CopyFrom([1, 2, 3], 2, 2));
    }

    /// <summary>
    /// 验证Coordinate Validation Rejects Out Of Range Access。
    /// </summary>
    [Fact]
    public void CoordinateValidationRejectsOutOfRangeAccess()
    {
        OccluderMap map = new(2, 2);

        AssertThrows<ArgumentOutOfRangeException>(() => map.Set(-1, 0, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => map.Set(0, 2, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => map.Get(2, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => map.Get(0, -1));
    }

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
