using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 连续 cell span 批量扫描 SIMD helper 测试。
/// </summary>
public sealed class CellSpanOpsTests
{
    /// <summary>
    /// 验证非零 ushort 计数在短 span、vector 宽度和 tail 情况下与标量 oracle 一致。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(127)]
    public void CountNonZeroUShortMatchesScalarOracle(int length)
    {
        ushort[] values = new ushort[length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (i % 5) == 0 || (i % 7) == 0 ? (ushort)(i + 1) : (ushort)0;
        }

        Assert.Equal(CountScalar(values), CellSpanOps.CountNonZeroUShort(values));
    }

    /// <summary>
    /// 验证全空与全满两个极端输入。
    /// </summary>
    [Fact]
    public void CountNonZeroUShortHandlesEmptyAndFullInputs()
    {
        ushort[] empty = new ushort[128];
        ushort[] full = new ushort[128];
        Array.Fill(full, (ushort)3);

        Assert.Equal(0, CellSpanOps.CountNonZeroUShort(empty));
        Assert.Equal(full.Length, CellSpanOps.CountNonZeroUShort(full));
    }

    /// <summary>
    /// 验证批量 parity 写入只触碰 material 或 flags 非零的 cell，并保留其它 flag 位。
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(19)]
    [InlineData(33)]
    public void SetParityForOccupiedCellsOnlyTouchesOccupiedCells(int length)
    {
        ushort[] material = new ushort[length];
        byte[] flags = new byte[length];
        byte[] expected = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bool hasMaterial = (i % 3) == 1;
            bool hasFlagOnly = (i % 5) == 2;
            if (hasMaterial)
            {
                material[i] = (ushort)(i + 1);
            }

            if (hasFlagOnly)
            {
                flags[i] = CellFlags.Burning;
            }

            expected[i] = hasMaterial || hasFlagOnly
                ? CellFlags.SetParity(flags[i], CellFlags.Parity)
                : (byte)0;
        }

        CellSpanOps.SetParityForOccupiedCells(material, flags, CellFlags.Parity);

        Assert.Equal(expected, flags);
    }

    private static int CountScalar(ReadOnlySpan<ushort> values)
    {
        int count = 0;
        for (int i = 0; i < values.Length; i++)
        {
            count += values[i] != 0 ? 1 : 0;
        }

        return count;
    }
}
