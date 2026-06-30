namespace PixelEngine.Simulation;

/// <summary>
/// 材质注册表。运行期热路径按 id 访问，name 字典只用于加载、存档 remap 与热重载。
/// </summary>
public sealed class MaterialTable
{
    private MaterialDef[] _defs;
    private bool[] _tombstones;
    private readonly Dictionary<string, ushort> _nameToId;

    /// <summary>
    /// 从已分配 runtime id 的材质定义构建注册表。
    /// </summary>
    public MaterialTable(ReadOnlySpan<MaterialDef> definitions)
    {
        _nameToId = new Dictionary<string, ushort>(definitions.Length, StringComparer.Ordinal);
        _defs = CopyAndValidateInitialDefinitions(definitions, _nameToId);
        _tombstones = new bool[_defs.Length];
        Hot = MaterialHotTable.FromDefinitions(_defs);
    }

    /// <summary>
    /// 完整材质定义数量，包含 tombstone id。
    /// </summary>
    public int Count => _defs.Length;

    /// <summary>
    /// 热路径 SoA 表。
    /// </summary>
    public MaterialHotTable Hot { get; private set; }

    /// <summary>
    /// 按 runtime id 返回材质定义。
    /// </summary>
    public ref readonly MaterialDef Get(ushort id)
    {
        ValidateId(id);
        return ref _defs[id];
    }

    /// <summary>
    /// 判断指定 runtime id 是否为删除材质留下的 tombstone。
    /// </summary>
    public bool IsTombstone(ushort id)
    {
        ValidateId(id);
        return _tombstones[id];
    }

    /// <summary>
    /// 按稳定 name 查 runtime id。
    /// </summary>
    public bool TryGetId(string name, out ushort id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _nameToId.TryGetValue(name, out id);
    }

    /// <summary>
    /// 按稳定 name 查 runtime id，缺失时返回 fallback。
    /// </summary>
    public ushort GetIdOrFallback(string name, ushort fallback)
    {
        ValidateLiveId(fallback, nameof(fallback));
        return TryGetId(name, out ushort id) ? id : fallback;
    }

    /// <summary>
    /// 返回 runtime id 对应的稳定 name。
    /// </summary>
    public string GetName(ushort id)
    {
        ValidateLiveId(id, nameof(id));
        return _defs[id].Name;
    }

    /// <summary>
    /// 导出当前 live id→name 表，供存档头写入。
    /// </summary>
    public (ushort Id, string Name)[] BuildIdNameTable()
    {
        int count = 0;
        for (int i = 0; i < _defs.Length; i++)
        {
            if (!_tombstones[i])
            {
                count++;
            }
        }

        (ushort Id, string Name)[] table = new (ushort Id, string Name)[count];
        int write = 0;
        for (int i = 0; i < _defs.Length; i++)
        {
            if (_tombstones[i])
            {
                continue;
            }

            table[write++] = ((ushort)i, _defs[i].Name);
        }

        return table;
    }

    /// <summary>
    /// 根据存档头中的 savedId→name 表构建 savedId→currentId remap LUT，缺失材质映射到 fallback。
    /// </summary>
    public ushort[] BuildRemapLut(ReadOnlySpan<(ushort SavedId, string Name)> savedTable, ushort fallbackId)
    {
        ValidateLiveId(fallbackId, nameof(fallbackId));
        int maxId = -1;
        for (int i = 0; i < savedTable.Length; i++)
        {
            maxId = Math.Max(maxId, savedTable[i].SavedId);
        }

        ushort[] remap = new ushort[maxId + 1];
        Array.Fill(remap, fallbackId);
        bool[] seen = new bool[remap.Length];
        for (int i = 0; i < savedTable.Length; i++)
        {
            (ushort savedId, string name) = savedTable[i];
            if (seen[savedId])
            {
                throw new ArgumentException($"存档材质表包含重复 id：{savedId}。", nameof(savedTable));
            }

            seen[savedId] = true;
            remap[savedId] = TryGetId(name, out ushort currentId) ? currentId : fallbackId;
        }

        return remap;
    }

    /// <summary>
    /// 稳定热重载：既有 name 保留 id，新 name 追加 id，删除 name 留 tombstone；绝不重排现有 id。
    /// </summary>
    public MaterialReloadResult ReloadStable(
        ReadOnlySpan<MaterialDef> newDefinitions,
        ReadOnlySpan<int> liveCellCountsByMaterial = default,
        ushort fallbackId = 0)
    {
        ValidateLiveId(fallbackId, nameof(fallbackId));
        if (!liveCellCountsByMaterial.IsEmpty && liveCellCountsByMaterial.Length < _defs.Length)
        {
            throw new ArgumentException("live cell 计数列长度不能小于旧材质表长度。", nameof(liveCellCountsByMaterial));
        }

        Dictionary<string, MaterialDef> incoming = ValidateReloadDefinitions(newDefinitions);
        bool[] kept = new bool[_defs.Length];
        List<MaterialDef> appended = [];
        int preservedCount = 0;
        int fallbackReplacementCount = 0;

        foreach (KeyValuePair<string, MaterialDef> entry in incoming)
        {
            if (_nameToId.TryGetValue(entry.Key, out ushort existingId))
            {
                _defs[existingId] = entry.Value with { Id = existingId };
                _tombstones[existingId] = false;
                kept[existingId] = true;
                preservedCount++;
            }
            else
            {
                appended.Add(entry.Value);
            }
        }

        List<ushort> tombstones = [];
        for (int i = 0; i < _defs.Length; i++)
        {
            if (_tombstones[i] || kept[i])
            {
                continue;
            }

            tombstones.Add((ushort)i);
            if (!liveCellCountsByMaterial.IsEmpty)
            {
                fallbackReplacementCount += liveCellCountsByMaterial[i];
            }

            _defs[i] = MaterialDef.Tombstone((ushort)i);
            _tombstones[i] = true;
        }

        if (appended.Count != 0)
        {
            int oldLength = _defs.Length;
            Array.Resize(ref _defs, oldLength + appended.Count);
            Array.Resize(ref _tombstones, _defs.Length);
            for (int i = 0; i < appended.Count; i++)
            {
                ushort id = checked((ushort)(oldLength + i));
                _defs[id] = appended[i] with { Id = id };
            }
        }

        RebuildNameIndex();
        Hot = MaterialHotTable.FromDefinitions(_defs);
        return new MaterialReloadResult([.. tombstones], appended.Count, preservedCount, fallbackReplacementCount);
    }

    private static MaterialDef[] CopyAndValidateInitialDefinitions(
        ReadOnlySpan<MaterialDef> definitions,
        Dictionary<string, ushort> nameToId)
    {
        MaterialDef[] copy = definitions.ToArray();
        for (int i = 0; i < copy.Length; i++)
        {
            ValidateDefinition(copy[i], i);
            if (!nameToId.TryAdd(copy[i].Name, copy[i].Id))
            {
                throw new ArgumentException($"重复材质 name：{copy[i].Name}。", nameof(definitions));
            }
        }

        return copy;
    }

    private static Dictionary<string, MaterialDef> ValidateReloadDefinitions(ReadOnlySpan<MaterialDef> definitions)
    {
        Dictionary<string, MaterialDef> byName = new(definitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
        {
            MaterialDef def = definitions[i];
            ValidateDefinitionForReload(def, i);
            if (!byName.TryAdd(def.Name, def))
            {
                throw new ArgumentException($"重复材质 name：{def.Name}。", nameof(definitions));
            }
        }

        return byName;
    }

    private static void ValidateDefinition(MaterialDef def, int expectedId)
    {
        if (def.Id != expectedId)
        {
            throw new ArgumentException($"材质 {def.Name} 的 Id={def.Id}，但期望数组下标 {expectedId}。");
        }

        ValidateDefinitionForReload(def, expectedId);
    }

    private static void ValidateDefinitionForReload(MaterialDef def, int index)
    {
        if (string.IsNullOrWhiteSpace(def.Name))
        {
            throw new ArgumentException($"第 {index} 个材质缺少稳定 name。");
        }

        if (def.HeatCapacity == 0)
        {
            throw new ArgumentException($"材质 {def.Name} 的 HeatCapacity 不能为 0。");
        }
    }

    private void RebuildNameIndex()
    {
        _nameToId.Clear();
        for (int i = 0; i < _defs.Length; i++)
        {
            if (_tombstones[i])
            {
                continue;
            }

            _nameToId.Add(_defs[i].Name, (ushort)i);
        }
    }

    private void ValidateId(ushort id)
    {
        if (id >= _defs.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "材质 id 超出材质表范围。");
        }
    }

    private void ValidateLiveId(ushort id, string parameterName)
    {
        if (id >= _defs.Length || _tombstones[id])
        {
            throw new ArgumentOutOfRangeException(parameterName, id, "材质 id 超出材质表范围或指向 tombstone。");
        }
    }
}

/// <summary>
/// 材质稳定热重载的结果摘要。
/// </summary>
public readonly record struct MaterialReloadResult(
    ushort[] TombstoneIds,
    int AddedCount,
    int PreservedCount,
    int FallbackReplacementCount);
