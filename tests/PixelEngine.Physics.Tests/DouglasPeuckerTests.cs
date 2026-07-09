using System.Numerics;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// Douglas-Peucker 折线简化测试。
/// 不变式：简化折线顶点单调、误差阈值内保形。
/// </summary>
public sealed class DouglasPeuckerTests
{
    /// <summary>
    /// 验证共线中间点会被移除。
    /// </summary>
    [Fact]
    public void SimplifyOpenLineRemovesCollinearMiddlePoints()
    {
        Vector2[] points =
        [
            new(0, 0),
            new(1, 0),
            new(2, 0),
            new(3, 0),
        ];
        Span<Vector2> destination = stackalloc Vector2[points.Length];

        int written = DouglasPeucker.Simplify(points, destination, epsilon: 0f);

        Assert.Equal(2, written);
        Assert.Equal(points[0], destination[0]);
        Assert.Equal(points[^1], destination[1]);
    }

    /// <summary>
    /// 验证明显拐点会保留。
    /// </summary>
    [Fact]
    public void SimplifyOpenLineKeepsCorner()
    {
        Vector2[] points =
        [
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(1, 2),
        ];
        Span<Vector2> destination = stackalloc Vector2[points.Length];

        int written = DouglasPeucker.Simplify(points, destination, epsilon: 0.1f);

        Assert.Equal(3, written);
        Assert.Equal(new Vector2(1, 0), destination[1]);
    }

    /// <summary>
    /// 验证闭合矩形保留四个角，并保持首尾重复。
    /// </summary>
    [Fact]
    public void SimplifyClosedRectangleKeepsCornersAndClosure()
    {
        Vector2[] points =
        [
            new(0, 0),
            new(1, 0),
            new(2, 0),
            new(2, 1),
            new(2, 2),
            new(1, 2),
            new(0, 2),
            new(0, 1),
            new(0, 0),
        ];
        Span<Vector2> destination = stackalloc Vector2[DouglasPeucker.GetMaximumOutputCount(points, closed: true)];

        int written = DouglasPeucker.Simplify(points, destination, epsilon: 0f, closed: true);

        Assert.Equal(5, written);
        Assert.Equal(destination[0], destination[written - 1]);
        Assert.Contains(new Vector2(0, 0), destination[..written].ToArray());
        Assert.Contains(new Vector2(2, 0), destination[..written].ToArray());
        Assert.Contains(new Vector2(2, 2), destination[..written].ToArray());
        Assert.Contains(new Vector2(0, 2), destination[..written].ToArray());
    }

    /// <summary>
    /// 验证 List 返回 API 可用。
    /// </summary>
    [Fact]
    public void SimplifyListApiReturnsExpectedPoints()
    {
        Vector2[] points =
        [
            new(0, 0),
            new(1, 0),
            new(2, 0),
        ];

        List<Vector2> simplified = DouglasPeucker.Simplify(points, epsilon: 0f);

        Assert.Equal([points[0], points[^1]], simplified);
    }
}
