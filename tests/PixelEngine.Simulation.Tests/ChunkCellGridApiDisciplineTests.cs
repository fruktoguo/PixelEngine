using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 锁定 Chunk/CellGrid 的公开边界：外部只能读或走受控写 API，不能拿到会绕过 dirty/parity/KeepAlive 的裸引用。
/// </summary>
public sealed class ChunkCellGridApiDisciplineTests
{
    /// <summary>
    /// 验证 Chunk 只发布只读 plane，并隐藏数组基址 ref。
    /// </summary>
    [Fact]
    public void ChunkPublishesReadOnlyPlanesAndNoPublicRawBases()
    {
        Assert.Equal(typeof(ReadOnlySpan<ushort>), typeof(Chunk).GetProperty(nameof(Chunk.Material))!.PropertyType);
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(Chunk).GetProperty(nameof(Chunk.Flags))!.PropertyType);
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(Chunk).GetProperty(nameof(Chunk.Lifetime))!.PropertyType);
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(Chunk).GetProperty(nameof(Chunk.Damage))!.PropertyType);
        Assert.DoesNotContain(
            typeof(Chunk).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public),
            static property => property.PropertyType.IsArray);

        const System.Reflection.BindingFlags publicInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        Assert.Null(typeof(Chunk).GetMethod(nameof(Chunk.GetMaterialBase), publicInstance));
        Assert.Null(typeof(Chunk).GetMethod(nameof(Chunk.GetFlagsBase), publicInstance));
        Assert.Null(typeof(Chunk).GetMethod(nameof(Chunk.GetLifetimeBase), publicInstance));
        Assert.Null(typeof(Chunk).GetMethod(nameof(Chunk.GetDamageBase), publicInstance));
    }

    /// <summary>
    /// 验证 CellGrid 与 NeighborWindow 不发布公开 ref cell accessor。
    /// </summary>
    [Fact]
    public void CellGridAndNeighborWindowDoNotPublishRefCellAccessors()
    {
        const System.Reflection.BindingFlags publicInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        System.Reflection.MethodInfo[] publicGridMethods = typeof(CellGrid).GetMethods(publicInstance);
        Assert.All(publicGridMethods, method => Assert.False(method.ReturnType.IsByRef, method.Name));
        Assert.Null(typeof(CellGrid).GetMethod(nameof(CellGrid.MaterialAt), publicInstance));
        Assert.Null(typeof(CellGrid).GetMethod(nameof(CellGrid.FlagsAt), publicInstance));
        Assert.Null(typeof(CellGrid).GetMethod(nameof(CellGrid.LifetimeAt), publicInstance));
        Assert.Null(typeof(CellGrid).GetMethod(nameof(CellGrid.DamageAt), publicInstance));
        Assert.Null(typeof(NeighborWindow).GetMethod(nameof(NeighborWindow.MaterialAt), publicInstance));
        Assert.Null(typeof(NeighborWindow).GetMethod(nameof(NeighborWindow.FlagsAt), publicInstance));
        Assert.Null(typeof(NeighborWindow).GetMethod(nameof(NeighborWindow.LifetimeAt), publicInstance));
        Assert.Null(typeof(NeighborWindow).GetMethod(nameof(NeighborWindow.DamageAt), publicInstance));

        Assert.NotNull(typeof(CellGrid).GetMethod(nameof(CellGrid.GetFlags), publicInstance));
        Assert.NotNull(typeof(CellGrid).GetMethod(nameof(CellGrid.GetLifetime), publicInstance));
        Assert.NotNull(typeof(CellGrid).GetMethod(nameof(CellGrid.GetDamage), publicInstance));
        Assert.NotNull(typeof(CellGrid).GetMethod(nameof(CellGrid.TryClearCell), publicInstance));
    }

    /// <summary>
    /// 验证受控清除在普通 cell 上维护 dirty，在刚体 cell 上保留状态并通知 damage sink。
    /// </summary>
    [Fact]
    public void CellGridControlledClearPreservesRigidDamageAndDirtySemantics()
    {
        DeterministicSimFixture.TestChunkSource source =
            DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialPropsTable materials = new(
            [CellType.Empty, CellType.Solid],
            [0, 200],
            [0, 0],
            [0, 0],
            [0, 0],
            [0, 0]);
        RecordingRigidDamageSink sink = new();
        CellGrid grid = new(source, materials, sink);

        grid.SetMaterial(3, 4, 1);
        chunk.ClearDirty();
        Assert.True(grid.TryClearCell(3, 4));
        Assert.Equal((ushort)0, grid.GetMaterial(3, 4));
        Assert.NotEqual(DirtyRect.Empty, chunk.WorkingDirty);

        chunk.ClearDirty();
        int local = CellAddressing.LocalIndex(3, 4);
        chunk.MaterialBuffer[local] = 1;
        chunk.FlagsBuffer[local] = CellFlags.RigidOwned;
        chunk.LifetimeBuffer[local] = 7;
        chunk.DamageBuffer[local] = 9;

        Assert.False(grid.TryClearCell(3, 4));
        Assert.Equal((ushort)1, grid.GetMaterial(3, 4));
        Assert.True(CellFlags.Has(grid.GetFlags(3, 4), CellFlags.RigidOwned));
        Assert.Equal(7, grid.GetLifetime(3, 4));
        Assert.Equal(9, grid.GetDamage(3, 4));
        Assert.Equal(1, sink.Count);
        Assert.Equal((3, 4), sink.Last);
        Assert.Equal(DirtyRect.Empty, chunk.WorkingDirty);

        grid.SetMaterial(3, 4, 0);
        Assert.Equal((ushort)0, grid.GetMaterial(3, 4));
        Assert.Equal((byte)0, grid.GetFlags(3, 4));
        Assert.Equal((byte)0, grid.GetLifetime(3, 4));
        Assert.Equal((byte)0, grid.GetDamage(3, 4));
        Assert.Equal(2, sink.Count);
        Assert.NotEqual(DirtyRect.Empty, chunk.WorkingDirty);
    }

    private sealed class RecordingRigidDamageSink : IRigidDamageSink
    {
        public int Count { get; private set; }

        public (int X, int Y) Last { get; private set; }

        public void OnOwnedCellDamaged(int wx, int wy)
        {
            Count++;
            Last = (wx, wy);
        }
    }
}
