namespace PixelEngine.Simulation;

/// <summary>
/// 每材质 reaction 切片的查找策略。
/// </summary>
public enum ReactionLookupMode : byte
{
    /// <summary>
    /// 无反应。
    /// </summary>
    None,

    /// <summary>
    /// 小切片线性扫描。
    /// </summary>
    Linear,

    /// <summary>
    /// 中等切片按 InputB 二分查找。
    /// </summary>
    Binary,

    /// <summary>
    /// 大切片使用材质私有 direct table。
    /// </summary>
    DirectTable,
}

/// <summary>
/// cache-aware packed 反应表。按 owner 材质连续分组，避免 int[N*N] 大表带来的 cache miss。
/// </summary>
public sealed class ReactionTable
{
    /// <summary>
    /// 小于等于该数量时使用线性扫描。阈值可随后续 benchmark 调整。
    /// </summary>
    public const byte LinearLookupMaxCount = 8;

    /// <summary>
    /// 大于等于该数量时使用 direct table。阈值可随后续 cache-miss benchmark 调整。
    /// </summary>
    public const byte DirectLookupMinCount = 32;

    private const ushort DirectMiss = ushort.MaxValue;
    private readonly Reaction[] _packed;
    private readonly ReactionLookupMode[] _modeByMat;
    private readonly ushort[]?[] _directTables;

    /// <summary>
    /// 创建 packed 反应表。definitions 的 ReactionStart/ReactionCount 指向 packed 中的 owner 切片。
    /// </summary>
    public ReactionTable(ReadOnlySpan<Reaction> packed, ReadOnlySpan<MaterialDef> definitions)
    {
        _packed = packed.ToArray();
        _modeByMat = new ReactionLookupMode[definitions.Length];
        _directTables = new ushort[]?[definitions.Length];

        for (int i = 0; i < definitions.Length; i++)
        {
            ref readonly MaterialDef def = ref definitions[i];
            if (def.ReactionCount == 0)
            {
                _modeByMat[i] = ReactionLookupMode.None;
                continue;
            }

            ValidateSlice(def);
            ReactionLookupMode mode = SelectMode(def.ReactionCount);
            _modeByMat[i] = mode;
            if (mode == ReactionLookupMode.Binary)
            {
                ValidateSortedByNeighbor(def);
            }
            else if (mode == ReactionLookupMode.DirectTable)
            {
                _directTables[i] = BuildDirectTable(def, definitions.Length);
            }
        }
    }

    /// <summary>
    /// packed 反应数量。
    /// </summary>
    public int Count => _packed.Length;

    /// <summary>
    /// 每材质查找策略。
    /// </summary>
    public ReadOnlySpan<ReactionLookupMode> ModeByMaterial => _modeByMat;

    /// <summary>
    /// 在 owner=mat 的切片中查找 neighbor 反应，未命中返回 -1。
    /// </summary>
    public int Find(ushort mat, ushort neighbor, in MaterialDef def)
    {
        return def.ReactionCount == 0
            ? -1
            : mat >= _modeByMat.Length || def.Id != mat
                ? throw new ArgumentOutOfRangeException(nameof(mat), mat, "owner 材质 id 与材质定义不匹配。")
                : _modeByMat[mat] switch
                {
                    ReactionLookupMode.None => -1,
                    ReactionLookupMode.Linear => FindLinear(neighbor, in def),
                    ReactionLookupMode.Binary => FindBinary(neighbor, in def),
                    ReactionLookupMode.DirectTable => FindDirect(mat, neighbor, in def),
                    _ => throw new ArgumentOutOfRangeException(nameof(mat), mat, "未知 reaction 查找模式。"),
                };
    }

    /// <summary>
    /// 返回 packed 反应定义。
    /// </summary>
    public ref readonly Reaction At(int packedIndex)
    {
        if ((uint)packedIndex >= _packed.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(packedIndex), packedIndex, "packed reaction 索引越界。");
        }

        return ref _packed[packedIndex];
    }

    private static ReactionLookupMode SelectMode(byte reactionCount)
    {
        return reactionCount <= LinearLookupMaxCount
            ? ReactionLookupMode.Linear
            : reactionCount < DirectLookupMinCount
                ? ReactionLookupMode.Binary
                : ReactionLookupMode.DirectTable;
    }

    private void ValidateSlice(in MaterialDef def)
    {
        if (def.ReactionStart < 0 ||
            def.ReactionStart > _packed.Length ||
            def.ReactionCount > _packed.Length - def.ReactionStart)
        {
            throw new ArgumentException($"材质 {def.Name} 的 reaction 切片越界。");
        }

        for (int i = 0; i < def.ReactionCount; i++)
        {
            Reaction reaction = _packed[def.ReactionStart + i];
            if (reaction.InputA != def.Id)
            {
                throw new ArgumentException($"材质 {def.Name} 的 reaction 切片包含错误 owner。");
            }
        }
    }

    private void ValidateSortedByNeighbor(in MaterialDef def)
    {
        ushort previous = 0;
        for (int i = 0; i < def.ReactionCount; i++)
        {
            ushort current = _packed[def.ReactionStart + i].InputB;
            if (i != 0 && current <= previous)
            {
                throw new ArgumentException($"材质 {def.Name} 的 Binary reaction 切片必须按 InputB 严格升序。");
            }

            previous = current;
        }
    }

    private ushort[] BuildDirectTable(in MaterialDef def, int materialCount)
    {
        ushort[] table = new ushort[materialCount];
        Array.Fill(table, DirectMiss);
        for (int i = 0; i < def.ReactionCount; i++)
        {
            Reaction reaction = _packed[def.ReactionStart + i];
            if (reaction.InputB >= materialCount)
            {
                throw new ArgumentException($"材质 {def.Name} 的 direct reaction neighbor 超出材质表范围。");
            }

            if (table[reaction.InputB] != DirectMiss)
            {
                throw new ArgumentException($"材质 {def.Name} 的 direct reaction 切片包含重复 neighbor。");
            }

            table[reaction.InputB] = checked((ushort)i);
        }

        return table;
    }

    private int FindLinear(ushort neighbor, in MaterialDef def)
    {
        int start = def.ReactionStart;
        int end = start + def.ReactionCount;
        for (int i = start; i < end; i++)
        {
            if (_packed[i].InputB == neighbor)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindBinary(ushort neighbor, in MaterialDef def)
    {
        int lo = 0;
        int hi = def.ReactionCount - 1;
        while (lo <= hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            ushort current = _packed[def.ReactionStart + mid].InputB;
            if (current == neighbor)
            {
                return def.ReactionStart + mid;
            }

            if (current < neighbor)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }

    private int FindDirect(ushort mat, ushort neighbor, in MaterialDef def)
    {
        ushort[] direct = _directTables[mat] ?? throw new InvalidOperationException("direct reaction table 未初始化。");
        if (neighbor >= direct.Length)
        {
            return -1;
        }

        ushort offset = direct[neighbor];
        return offset == DirectMiss ? -1 : def.ReactionStart + offset;
    }
}
