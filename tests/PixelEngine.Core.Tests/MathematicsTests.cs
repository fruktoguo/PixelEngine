using Xunit;

namespace PixelEngine.Core.Tests;

/// <summary>
/// Core 数学基础设施测试。
/// </summary>
public sealed class MathematicsTests
{
    /// <summary>
    /// 验证Floor Div And Mod Match Floored Definition For Negative Coordinates。
    /// </summary>
    [Fact]
    public void FloorDivAndModMatchFlooredDefinitionForNegativeCoordinates()
    {
        for (int x = -130; x <= 130; x++)
        {
            int div = Mathematics.Mathx.FloorDiv(x, EngineConstants.ChunkSize);
            int mod = Mathematics.Mathx.Mod(x, EngineConstants.ChunkSize);

            Assert.InRange(mod, 0, EngineConstants.ChunkSize - 1);
            Assert.Equal(x, (div * EngineConstants.ChunkSize) + mod);
            Assert.True(div <= (double)x / EngineConstants.ChunkSize);
            Assert.True(div + 1 > (double)x / EngineConstants.ChunkSize);
        }
    }

    /// <summary>
    /// 验证Transform2D Inverse Restores Transformed Point。
    /// </summary>
    [Fact]
    public void Transform2DInverseRestoresTransformedPoint()
    {
        Mathematics.Transform2D transform = new(new System.Numerics.Vector2(12.5f, -7.25f), 1.234f);
        System.Numerics.Vector2 local = new(-3.75f, 9.5f);

        System.Numerics.Vector2 roundTrip = transform.InverseTransformPoint(transform.TransformPoint(local));

        Assert.InRange(MathF.Abs(roundTrip.X - local.X), 0, 1e-5f);
        Assert.InRange(MathF.Abs(roundTrip.Y - local.Y), 0, 1e-5f);
    }

    /// <summary>
    /// 验证Rect I Encapsulate And Expand Clamped Stay Inside Chunk Bounds。
    /// </summary>
    [Fact]
    public void RectIEncapsulateAndExpandClampedStayInsideChunkBounds()
    {
        Mathematics.RectI rect = Mathematics.RectI.Empty;
        rect.Encapsulate(63, 63);
        rect.Encapsulate(0, 0);

        Mathematics.RectI bounds = Mathematics.RectI.FromBounds(0, 0, EngineConstants.ChunkSize, EngineConstants.ChunkSize);
        rect.ExpandClamped(EngineConstants.DirtyRectPadding, in bounds);

        Assert.Equal(bounds, rect);
        Assert.True(rect.Contains(0, 0));
        Assert.True(rect.Contains(63, 63));
    }

    /// <summary>
    /// 验证Fixed Arithmetic Uses Stable Raw Representation。
    /// </summary>
    [Fact]
    public void FixedArithmeticUsesStableRawRepresentation()
    {
        Mathematics.Fixed a = Mathematics.Fixed.FromInt(3);
        Mathematics.Fixed b = Mathematics.Fixed.FromInt(2);
        Mathematics.Fixed half = Mathematics.Fixed.Half;

        Assert.Equal(Mathematics.Fixed.FromInt(5).Raw, (a + b).Raw);
        Assert.Equal(Mathematics.Fixed.FromInt(1).Raw, (a - b).Raw);
        Assert.Equal(Mathematics.Fixed.FromInt(6).Raw, (a * b).Raw);
        Assert.Equal(Mathematics.Fixed.FromRaw(Mathematics.Fixed.One.Raw + half.Raw).Raw, (a / b).Raw);
        Assert.True(a > b);
        Assert.Equal(-2, Mathematics.Fixed.FromRaw(-(Mathematics.Fixed.One.Raw + half.Raw)).RoundToInt());
        Assert.Equal(-1, Mathematics.Fixed.FromRaw(-(Mathematics.Fixed.One.Raw + half.Raw)).ToInt());
    }
}
