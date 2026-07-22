using PixelEngine.Core;
using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 在离散场景投影边界执行显式 authoring world provider，并保留等价网格上的画刷编辑。
/// </summary>
internal sealed class AuthoringWorldPreviewRuntime
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private readonly IChunkSource _chunks;
    private readonly IMaterialQuery _materials;
    private readonly IConfigApi _config;
    private readonly ISimulationEditApi _simulationEdit;
    private readonly IAuthoringWorldEditApi _providerEdit;
    private ulong _lastContentFingerprint;
    private long _snapshotVersion;
    private bool _hasAuthoritativeWorld;
    private int _authoringWidthCells;
    private int _authoringHeightCells;

    public AuthoringWorldPreviewRuntime(
        IChunkSource chunks,
        IMaterialQuery materials,
        IConfigApi config,
        ISimulationEditApi edit)
    {
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _simulationEdit = edit ?? throw new ArgumentNullException(nameof(edit));
        _providerEdit = new SimulationAuthoringWorldEditApi(_simulationEdit);
    }

    /// <summary>
    /// 最近一次显式 provider 投影出的 authoring world 状态。
    /// </summary>
    public AuthoringWorldPreviewSnapshot Snapshot { get; private set; }

    /// <summary>
    /// 同步当前 Script Scene 的显式预览提供器。
    /// </summary>
    public AuthoringWorldRefreshResult Refresh(
        Scripting.Scene scene,
        IReadOnlyDictionary<int, int> stableIdToEntityId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(stableIdToEntityId);
        List<ProviderEntry> providers = CaptureProviders(scene, stableIdToEntityId);
        if (providers.Count == 0)
        {
            if (!_hasAuthoritativeWorld)
            {
                return AuthoringWorldRefreshResult.None;
            }

            ClearAuthoringWorld(_authoringWidthCells, _authoringHeightCells);
            _hasAuthoritativeWorld = false;
            _lastContentFingerprint = 0;
            _authoringWidthCells = 0;
            _authoringHeightCells = 0;
            Snapshot = new AuthoringWorldPreviewSnapshot(
                ++_snapshotVersion,
                HasWorld: false,
                default,
                WorldOwnerStableId: null);
            return AuthoringWorldRefreshResult.Cleared;
        }

        AuthoringWorldPreviewDescriptor worldDescriptor = ValidateProviderLayout(providers);
        ulong fingerprint = ComputeFingerprint(providers);
        if (_hasAuthoritativeWorld && fingerprint == _lastContentFingerprint)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                providers[i].Provider.AdoptAuthoringWorld();
            }

            return AuthoringWorldRefreshResult.Reused;
        }

        ClearAuthoringWorld(
            _hasAuthoritativeWorld ? _authoringWidthCells : worldDescriptor.WidthCells,
            _hasAuthoritativeWorld ? _authoringHeightCells : worldDescriptor.HeightCells);
        for (int i = 0; i < providers.Count; i++)
        {
            ProviderEntry entry = providers[i];
            AuthoringWorldPreviewDescriptor descriptor = entry.Descriptor;
            AuthoringWorldPreviewContext context = new(
                _materials,
                _config,
                _providerEdit,
                descriptor.WidthCells,
                descriptor.HeightCells);
            entry.Provider.PopulateAuthoringWorld(in context);
        }

        _lastContentFingerprint = fingerprint;
        _hasAuthoritativeWorld = true;
        _authoringWidthCells = worldDescriptor.WidthCells;
        _authoringHeightCells = worldDescriptor.HeightCells;
        Snapshot = new AuthoringWorldPreviewSnapshot(
            ++_snapshotVersion,
            HasWorld: true,
            new SceneAuthoringBounds(
                0f,
                0f,
                worldDescriptor.WidthCells,
                worldDescriptor.HeightCells),
            providers.Count == 1 ? providers[0].StableId : null);
        return AuthoringWorldRefreshResult.Rebuilt;
    }

    private static List<ProviderEntry> CaptureProviders(
        Scripting.Scene scene,
        IReadOnlyDictionary<int, int> stableIdToEntityId)
    {
        Dictionary<int, int> entityIdToStableId = new(stableIdToEntityId.Count);
        foreach ((int stableId, int entityId) in stableIdToEntityId)
        {
            if (!entityIdToStableId.TryAdd(entityId, stableId))
            {
                throw new InvalidOperationException($"运行时实体 {entityId} 被多个 authoring StableId 映射。");
            }
        }

        ScriptEntityInspection[] entities = scene.CaptureInspectionSnapshot();
        List<ProviderEntry> providers = [];
        for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
        {
            ScriptEntityInspection entity = entities[entityIndex];
            for (int componentIndex = 0; componentIndex < entity.Components.Length; componentIndex++)
            {
                ScriptComponentInspection component = entity.Components[componentIndex];
                if (!component.Enabled || component.Behaviour is not IAuthoringWorldPreviewProvider provider)
                {
                    continue;
                }

                if (!entityIdToStableId.TryGetValue(entity.EntityId, out int stableId))
                {
                    throw new InvalidOperationException(
                        $"authoring world provider 所在运行时实体 {entity.EntityId} 缺少 StableId 映射。");
                }

                AuthoringWorldPreviewDescriptor descriptor = provider.DescribeAuthoringWorld().Validate();
                providers.Add(new ProviderEntry(
                    stableId,
                    component.TypeName,
                    provider,
                    descriptor));
            }
        }

        providers.Sort(static (left, right) =>
        {
            int entityOrder = left.StableId.CompareTo(right.StableId);
            return entityOrder != 0
                ? entityOrder
                : StringComparer.Ordinal.Compare(left.TypeName, right.TypeName);
        });
        return providers;
    }

    private AuthoringWorldPreviewDescriptor ValidateProviderLayout(List<ProviderEntry> providers)
    {
        AuthoringWorldPreviewDescriptor descriptor = providers[0].Descriptor;
        for (int i = 1; i < providers.Count; i++)
        {
            AuthoringWorldPreviewDescriptor candidate = providers[i].Descriptor;
            if (candidate.WidthCells != descriptor.WidthCells || candidate.HeightCells != descriptor.HeightCells)
            {
                throw new InvalidOperationException(
                    "同一场景的 authoring world providers 必须声明完全相同的世界尺寸。");
            }
        }

        int lastChunkX = (descriptor.WidthCells - 1) / EngineConstants.ChunkSize;
        int lastChunkY = (descriptor.HeightCells - 1) / EngineConstants.ChunkSize;
        for (int chunkY = 0; chunkY <= lastChunkY; chunkY++)
        {
            for (int chunkX = 0; chunkX <= lastChunkX; chunkX++)
            {
                ChunkCoord coord = new(chunkX, chunkY);
                if (!_chunks.TryGetChunk(coord, out _))
                {
                    throw new InvalidOperationException(
                        $"authoring world 尺寸 {descriptor.WidthCells}x{descriptor.HeightCells} 超出驻留网格；缺少 chunk {coord}。");
                }
            }
        }

        return descriptor;
    }

    private static ulong ComputeFingerprint(List<ProviderEntry> providers)
    {
        ulong hash = FnvOffset;
        for (int i = 0; i < providers.Count; i++)
        {
            ProviderEntry entry = providers[i];
            hash = Mix(hash, (uint)entry.StableId);
            for (int characterIndex = 0; characterIndex < entry.TypeName.Length; characterIndex++)
            {
                hash = Mix(hash, entry.TypeName[characterIndex]);
            }

            hash = Mix(hash, (uint)entry.Descriptor.WidthCells);
            hash = Mix(hash, (uint)entry.Descriptor.HeightCells);
            hash = Mix(hash, entry.Descriptor.ContentHash);
        }

        return hash;
    }

    private void ClearAuthoringWorld(int widthCells, int heightCells)
    {
        if (widthCells <= 0 || heightCells <= 0)
        {
            return;
        }

        _ = _simulationEdit.ClearRect(0, 0, widthCells - 1, heightCells - 1);
    }

    private static ulong Mix(ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= FnvPrime;
        }

        return hash;
    }

    private readonly record struct ProviderEntry(
        int StableId,
        string TypeName,
        IAuthoringWorldPreviewProvider Provider,
        AuthoringWorldPreviewDescriptor Descriptor);

    private sealed class SimulationAuthoringWorldEditApi(ISimulationEditApi edit) : IAuthoringWorldEditApi
    {
        private readonly ISimulationEditApi _edit = edit ?? throw new ArgumentNullException(nameof(edit));

        public void PaintCell(int worldX, int worldY, MaterialId material)
        {
            _edit.PaintCell(worldX, worldY, material.Value);
        }

        public int PaintRect(int minX, int minY, int maxX, int maxY, MaterialId material)
        {
            return _edit.PaintRect(minX, minY, maxX, maxY, material.Value);
        }

        public void ClearCell(int worldX, int worldY)
        {
            _edit.ClearCell(worldX, worldY);
        }

        public int ClearRect(int minX, int minY, int maxX, int maxY)
        {
            return _edit.ClearRect(minX, minY, maxX, maxY);
        }
    }
}

internal enum AuthoringWorldRefreshResult
{
    None,
    Reused,
    Rebuilt,
    Cleared,
}

/// <summary>
/// 显式 authoring world provider 发布给 Editor UI 的只读快照。
/// </summary>
internal readonly record struct AuthoringWorldPreviewSnapshot(
    long Version,
    bool HasWorld,
    SceneAuthoringBounds Bounds,
    int? WorldOwnerStableId);
