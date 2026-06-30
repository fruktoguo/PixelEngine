namespace PixelEngine.Simulation;

/// <summary>
/// 具体材质对的反应定义。InputA 所在 cell 写 OutputA，InputB 所在 cell 写 OutputB。
/// </summary>
public readonly record struct Reaction
{
    /// <summary>
    /// owner 材质，也就是当前切片所属材质。
    /// </summary>
    public ushort InputA { get; init; }

    /// <summary>
    /// 反应对方材质。
    /// </summary>
    public ushort InputB { get; init; }

    /// <summary>
    /// InputA 位置的产物材质。
    /// </summary>
    public ushort OutputA { get; init; }

    /// <summary>
    /// InputB 位置的产物材质。
    /// </summary>
    public ushort OutputB { get; init; }

    /// <summary>
    /// 触发概率，范围 0-255。
    /// </summary>
    public byte Probability { get; init; }

    /// <summary>
    /// 反应行为标记。
    /// </summary>
    public ReactionFlags Flags { get; init; }
}

/// <summary>
/// 反应行为标记。
/// </summary>
[Flags]
public enum ReactionFlags : byte
{
    /// <summary>
    /// 无特殊行为。
    /// </summary>
    None = 0,

    /// <summary>
    /// 快速反应，执行阶段优先于普通反应裁决。
    /// </summary>
    Fast = 1 << 0,

    /// <summary>
    /// 定向反应，只物化 InputA owner 切片。
    /// </summary>
    Directional = 1 << 1,

    /// <summary>
    /// 产物之一应通过粒子抛射请求进入自由粒子系统。
    /// </summary>
    SpawnParticle = 1 << 2,

    /// <summary>
    /// 反应成功后向温度场注热。
    /// </summary>
    EmitHeat = 1 << 3,

    /// <summary>
    /// bit4-7 预留给后续 von Neumann 方向码。
    /// </summary>
    DirectionMask = 0b1111_0000,
}

/// <summary>
/// 反应输入材质对。
/// </summary>
public readonly record struct MaterialPair(ushort A, ushort B);

/// <summary>
/// tag 输出端代表材质映射。
/// </summary>
public readonly record struct MaterialTagRepresentative(MaterialTag Tag, ushort MaterialId);

/// <summary>
/// 反应 tag 展开与规则归一化的纯函数契约，供 Content 加载器调用。
/// </summary>
public static class ReactionExpansionRules
{
    /// <summary>
    /// 将 JSON rate 0-100 映射为 runtime byte 概率 0-255。
    /// </summary>
    public static byte RateToProbabilityByte(int rate)
    {
        return (uint)rate > 100
            ? throw new ArgumentOutOfRangeException(nameof(rate), rate, "反应概率必须位于 0-100。")
            : (byte)Math.Clamp((int)MathF.Round(rate * 255f / 100f), 0, 255);
    }

    /// <summary>
    /// 统计指定 tag 对应的材质成员数量。
    /// </summary>
    public static int CountTagMembers(ReadOnlySpan<MaterialProperty> propertyFlags, MaterialTag tag)
    {
        MaterialProperty mask = MaterialTagMap.ToProperty(tag);
        int count = 0;
        for (int i = 0; i < propertyFlags.Length; i++)
        {
            if ((propertyFlags[i] & mask) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 按 MaterialProperty 位筛选指定 tag 的成员材质 id，返回写入数量。
    /// </summary>
    public static int WriteTagMembers(ReadOnlySpan<MaterialProperty> propertyFlags, MaterialTag tag, Span<ushort> destination)
    {
        MaterialProperty mask = MaterialTagMap.ToProperty(tag);
        int write = 0;
        for (int i = 0; i < propertyFlags.Length; i++)
        {
            if ((propertyFlags[i] & mask) == 0)
            {
                continue;
            }

            if (write == destination.Length)
            {
                throw new ArgumentException("目标 span 容量不足，无法写入完整 tag 成员集合。", nameof(destination));
            }

            destination[write++] = checked((ushort)i);
        }

        return write;
    }

    /// <summary>
    /// 写入输入 tag 展开后的笛卡尔积材质对，返回写入数量。
    /// </summary>
    public static int WriteCartesianProduct(ReadOnlySpan<ushort> inputA, ReadOnlySpan<ushort> inputB, Span<MaterialPair> destination)
    {
        int required = checked(inputA.Length * inputB.Length);
        if (destination.Length < required)
        {
            throw new ArgumentException("目标 span 容量不足，无法写入完整 tag 笛卡尔积。", nameof(destination));
        }

        int write = 0;
        for (int a = 0; a < inputA.Length; a++)
        {
            for (int b = 0; b < inputB.Length; b++)
            {
                destination[write++] = new MaterialPair(inputA[a], inputB[b]);
            }
        }

        return write;
    }

    /// <summary>
    /// 将无序材质对归一为 min/max 形式，用于加载期去重作者重复定义的对称规则。
    /// </summary>
    public static MaterialPair NormalizeUnorderedPair(ushort materialA, ushort materialB)
    {
        return materialA <= materialB
            ? new MaterialPair(materialA, materialB)
            : new MaterialPair(materialB, materialA);
    }

    /// <summary>
    /// 判断普通或定向反应是否应物化到指定 owner 切片。
    /// </summary>
    public static bool ShouldMaterializeOwner(ReactionFlags flags, ushort owner, ushort inputA, ushort inputB)
    {
        return (flags & ReactionFlags.Directional) != 0
            ? owner == inputA
            : owner == inputA || owner == inputB;
    }

    /// <summary>
    /// 查找输出端 tag 的代表材质。
    /// </summary>
    public static ushort RepresentativeOf(MaterialTag tag, ReadOnlySpan<MaterialTagRepresentative> representatives)
    {
        for (int i = 0; i < representatives.Length; i++)
        {
            if (representatives[i].Tag == tag)
            {
                return representatives[i].MaterialId;
            }
        }

        throw new ArgumentException($"缺少 tag {tag} 的代表材质。", nameof(representatives));
    }
}
