using PixelEngine.Core;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// chunk 快照视图与 flags 持久化规则测试。
/// </summary>
public sealed class ChunkSnapshotTests
{
    /// <summary>
    /// 验证 ChunkSnapshot 接受正确长度的 SoA 与温度 span。
    /// </summary>
    [Fact]
    public void ChunkSnapshotAcceptsExpectedSpanLengths()
    {
        ushort[] material = new ushort[EngineConstants.ChunkArea];
        byte[] flags = new byte[EngineConstants.ChunkArea];
        byte[] lifetime = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];

        ChunkSnapshot snapshot = new(new ChunkCoord(1, -2), material, flags, lifetime, temperature);

        Assert.Equal(new ChunkCoord(1, -2), snapshot.Coord);
        Assert.Equal(EngineConstants.ChunkArea, snapshot.Material.Length);
        Assert.Equal(ChunkSnapshot.TemperatureCellCount, snapshot.Temperature.Length);
    }

    /// <summary>
    /// 验证 ChunkSnapshot 拒绝错误长度，防止 codec 读写越界。
    /// </summary>
    [Fact]
    public void ChunkSnapshotRejectsInvalidSpanLengths()
    {
        ushort[] material = new ushort[EngineConstants.ChunkArea - 1];
        byte[] flags = new byte[EngineConstants.ChunkArea];
        byte[] lifetime = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            _ = new ChunkSnapshot(new ChunkCoord(0, 0), material, flags, lifetime, temperature));

        Assert.Contains("Material", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证只有 burning 持久化，parity/settled/freefalling/rigid-owned 都会被剥离。
    /// </summary>
    [Fact]
    public void PersistentFlagsStripTransientRuntimeBits()
    {
        byte flags = CellFlags.Parity |
            CellFlags.Settled |
            CellFlags.Burning |
            CellFlags.FreeFalling |
            CellFlags.RigidOwned;

        byte persisted = PersistentCellFlags.StripTransient(flags);

        Assert.Equal(CellFlags.Burning, persisted);
    }

    /// <summary>
    /// 验证读档后 parity 被设置为与当前帧不同，且 runtime 瞬时位不会恢复。
    /// </summary>
    [Theory]
    [InlineData((byte)0, CellFlags.Parity)]
    [InlineData(CellFlags.Parity, (byte)0)]
    public void ResetTransientSetsParityOppositeToCurrentFrame(byte currentParity, byte expectedParity)
    {
        byte persisted = CellFlags.Burning | CellFlags.Settled | CellFlags.RigidOwned;

        byte reset = PersistentCellFlags.ResetTransient(persisted, currentParity);

        Assert.Equal((byte)(CellFlags.Burning | expectedParity), reset);
    }

    /// <summary>
    /// 验证批量重置会处理整个 span。
    /// </summary>
    [Fact]
    public void ResetTransientInPlaceProcessesWholeSpan()
    {
        byte[] flags =
        [
            CellFlags.Burning | CellFlags.Settled,
            CellFlags.FreeFalling,
        ];

        PersistentCellFlags.ResetTransientInPlace(flags, currentParityBit: 0);

        Assert.Equal((byte)(CellFlags.Burning | CellFlags.Parity), flags[0]);
        Assert.Equal(CellFlags.Parity, flags[1]);
    }
}
