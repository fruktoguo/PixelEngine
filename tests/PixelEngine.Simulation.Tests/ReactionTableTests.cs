using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 04 反应表与 tag 展开规则测试。
/// </summary>
public sealed class ReactionTableTests
{
    /// <summary>
    /// 验证惰性材质 ReactionCount=0 时一次比较早退，不读取 packed 切片。
    /// </summary>
    [Fact]
    public void FindReturnsMissForInertMaterialWithoutTouchingSlice()
    {
        MaterialDef inert = Material(0, "inert") with { ReactionStart = 99, ReactionCount = 0 };
        ReactionTable table = new([], [inert]);

        Assert.Equal(-1, table.Find(0, 1, in inert));
        Assert.Equal(ReactionLookupMode.None, table.ModeByMaterial[0]);
    }

    /// <summary>
    /// 验证小切片使用线性扫描并返回 packed 索引。
    /// </summary>
    [Fact]
    public void FindUsesLinearLookupForSmallSlices()
    {
        MaterialDef owner = Material(1, "acid") with { ReactionStart = 0, ReactionCount = 2 };
        Reaction[] reactions =
        [
            Reaction(1, 3, 4, 5),
            Reaction(1, 7, 8, 9),
        ];
        ReactionTable table = new(reactions, [Material(0, "empty"), owner, Material(2, "other")]);

        int index = table.Find(1, 7, in owner);

        Assert.Equal(ReactionLookupMode.Linear, table.ModeByMaterial[1]);
        Assert.Equal(1, index);
        Assert.Equal(8, table.At(index).OutputA);
        Assert.Equal(-1, table.Find(1, 2, in owner));
    }

    /// <summary>
    /// 验证 Linear 切片存在同 neighbor 普通/fast 两条时优先返回 Fast。
    /// </summary>
    [Fact]
    public void LinearLookupPrefersFastReactionForSameNeighbor()
    {
        MaterialDef owner = Material(1, "fire") with { ReactionStart = 0, ReactionCount = 2 };
        Reaction[] reactions =
        [
            Reaction(1, 2, 3, 4),
            Reaction(1, 2, 5, 6) with { Flags = ReactionFlags.Fast },
        ];
        ReactionTable table = new(reactions, [Material(0, "empty"), owner, Material(2, "wood")]);

        int index = table.Find(1, 2, in owner);

        Assert.Equal(1, index);
        Assert.True((table.At(index).Flags & ReactionFlags.Fast) != 0);
    }

    /// <summary>
    /// 验证中等切片使用按 InputB 升序的二分查找。
    /// </summary>
    [Fact]
    public void FindUsesBinaryLookupForMediumSortedSlices()
    {
        MaterialDef owner = Material(0, "fire") with { ReactionStart = 0, ReactionCount = 10 };
        Reaction[] reactions = new Reaction[10];
        for (ushort i = 0; i < reactions.Length; i++)
        {
            reactions[i] = Reaction(0, (ushort)(i + 1), 20, 30);
        }

        ReactionTable table = new(reactions, CreateMaterials(16, owner));

        Assert.Equal(ReactionLookupMode.Binary, table.ModeByMaterial[0]);
        Assert.Equal(8, table.Find(0, 9, in owner));
        Assert.Equal(-1, table.Find(0, 15, in owner));
    }

    /// <summary>
    /// 验证大切片使用材质私有 direct table，而不是构建全局 int[N*N] 大表。
    /// </summary>
    [Fact]
    public void FindUsesDirectTableForLargeSlices()
    {
        MaterialDef owner = Material(0, "fire") with { ReactionStart = 0, ReactionCount = 40 };
        Reaction[] reactions = new Reaction[40];
        for (ushort i = 0; i < reactions.Length; i++)
        {
            reactions[i] = Reaction(0, (ushort)(i + 1), 70, 80);
        }

        ReactionTable table = new(reactions, CreateMaterials(64, owner));

        Assert.Equal(ReactionLookupMode.DirectTable, table.ModeByMaterial[0]);
        Assert.Equal(32, table.Find(0, 33, in owner));
        Assert.Equal(-1, table.Find(0, 63, in owner));
    }

    /// <summary>
    /// 验证 Binary 切片必须按 InputB 严格升序，避免运行期二分查找漏反应。
    /// </summary>
    [Fact]
    public void BinarySliceRejectsUnsortedNeighbors()
    {
        MaterialDef owner = Material(0, "bad") with { ReactionStart = 0, ReactionCount = 10 };
        Reaction[] reactions = new Reaction[10];
        for (ushort i = 0; i < reactions.Length; i++)
        {
            reactions[i] = Reaction(0, (ushort)(10 - i), 1, 1);
        }

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new ReactionTable(reactions, CreateMaterials(16, owner)));
        Assert.Contains("严格升序", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 rate、笛卡尔积、无序对归一、双 owner 物化和 representative 规则。
    /// </summary>
    [Fact]
    public void ReactionExpansionRulesDefineTagContract()
    {
        Assert.Equal(0, ReactionExpansionRules.RateToProbabilityByte(0));
        Assert.Equal(128, ReactionExpansionRules.RateToProbabilityByte(50));
        Assert.Equal(255, ReactionExpansionRules.RateToProbabilityByte(100));

        MaterialProperty[] flags =
        [
            MaterialProperty.None,
            MaterialProperty.Fire,
            MaterialProperty.BurnableFast | MaterialProperty.Corrodible,
            MaterialProperty.Fire | MaterialProperty.Emissive,
        ];
        Span<ushort> members = stackalloc ushort[2];
        Assert.Equal(2, ReactionExpansionRules.CountTagMembers(flags, MaterialTag.Fire));
        Assert.Equal(2, ReactionExpansionRules.WriteTagMembers(flags, MaterialTag.Fire, members));
        Assert.Equal(1, members[0]);
        Assert.Equal(3, members[1]);

        Span<MaterialPair> pairs = stackalloc MaterialPair[4];
        int count = ReactionExpansionRules.WriteCartesianProduct([1, 2], [7, 8], pairs);

        Assert.Equal(4, count);
        Assert.Equal(new MaterialPair(1, 7), pairs[0]);
        Assert.Equal(new MaterialPair(1, 8), pairs[1]);
        Assert.Equal(new MaterialPair(2, 7), pairs[2]);
        Assert.Equal(new MaterialPair(2, 8), pairs[3]);
        Assert.Equal(new MaterialPair(3, 9), ReactionExpansionRules.NormalizeUnorderedPair(9, 3));
        Assert.True(ReactionExpansionRules.ShouldMaterializeOwner(ReactionFlags.None, owner: 9, inputA: 3, inputB: 9));
        Assert.False(ReactionExpansionRules.ShouldMaterializeOwner(ReactionFlags.Directional, owner: 9, inputA: 3, inputB: 9));
        Assert.Equal(
            42,
            ReactionExpansionRules.RepresentativeOf(
                MaterialTag.Fire,
                [new MaterialTagRepresentative(MaterialTag.Fire, 42)]));
    }

    private static Reaction Reaction(ushort inputA, ushort inputB, ushort outputA, ushort outputB)
    {
        return new Reaction
        {
            InputA = inputA,
            InputB = inputB,
            OutputA = outputA,
            OutputB = outputB,
            Probability = 255,
            Flags = ReactionFlags.None,
        };
    }

    private static MaterialDef Material(ushort id, string name)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = id == 0 ? CellType.Empty : CellType.Solid,
            HeatCapacity = 1f,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static MaterialDef[] CreateMaterials(int count, MaterialDef owner)
    {
        MaterialDef[] materials = new MaterialDef[count];
        for (ushort i = 0; i < materials.Length; i++)
        {
            materials[i] = Material(i, $"mat_{i}");
        }

        materials[owner.Id] = owner;
        return materials;
    }
}
