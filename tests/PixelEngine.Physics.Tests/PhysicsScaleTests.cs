using PixelEngine.Core;
using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 物理尺度与坐标转换测试。
/// 不变式：像素↔物理单位换算双向可逆、常量与文档一致。
/// </summary>
public sealed class PhysicsScaleTests
{
    /// <summary>
    /// 验证物理尺度常量与 Core 集中常量一致。
    /// </summary>
    [Fact]
    public void ConstantsMatchEngineConstants()
    {
        Assert.Equal(16, PhysicsScale.PixelsPerMeter);
        Assert.Equal(EngineConstants.PhysicsPixelsPerMeter, PhysicsScale.PixelsPerMeter);
        Assert.Equal(EngineConstants.MetersPerPixel, PhysicsScale.UnitsPerPixel);
        Assert.Equal(0f, PhysicsScale.SharpPolygonRadius);
    }

    /// <summary>
    /// 验证像素距离与物理距离双向换算。
    /// </summary>
    [Fact]
    public void PixelAndPhysicsDistancesRoundTrip()
    {
        Assert.Equal(2f, PhysicsScale.PixelToPhysics(32));
        Assert.Equal(2.5f, PhysicsScale.PixelToPhysics(40f));
        Assert.Equal(32f, PhysicsScale.PhysicsToPixel(2f));
        Assert.Equal(40f, PhysicsScale.PhysicsToPixel(2.5f));
    }

    /// <summary>
    /// 验证 cell 坐标与 Box2D 坐标换算保持 y 向下语义。
    /// </summary>
    [Fact]
    public void CellAndPhysicsPositionsRoundTrip()
    {
        Vector2i cell = new(32, 48);
        B2Vec2 physics = PhysicsScale.ToPhysics(cell);

        Assert.Equal(2f, physics.X);
        Assert.Equal(3f, physics.Y);
        Assert.Equal(cell, PhysicsScale.ToCell(physics));
    }
}
