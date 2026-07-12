using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 显式 authoring world provider 的投影、去重与生命周期测试。
/// </summary>
public sealed class AuthoringWorldPreviewRuntimeTests
{
    /// <summary>
    /// 验证相同 content hash 复用网格，变化时重建，provider 移除时清场。
    /// </summary>
    [Fact]
    public void ContentHashReusesPaintedWorldAndProviderRemovalClearsIt()
    {
        SingleChunkSource chunks = new();
        RecordingEditApi edit = new();
        AuthoringWorldPreviewRuntime runtime = new(chunks, new StubMaterialQuery(), edit);
        (ScriptScene firstScene, PreviewProvider firstProvider, IReadOnlyDictionary<int, int> firstMap) =
            CreateScene(stableId: 42, contentHash: 10);

        AuthoringWorldRefreshResult first = runtime.Refresh(firstScene, firstMap);

        Assert.Equal(AuthoringWorldRefreshResult.Rebuilt, first);
        Assert.Equal(1, firstProvider.PopulateCount);
        Assert.Equal(0, firstProvider.AdoptCount);
        Assert.Equal(1, edit.ClearRectCount);
        Assert.Equal(1, edit.PaintCellCount);
        Assert.Equal(
            new AuthoringWorldPreviewSnapshot(
                Version: 1,
                HasWorld: true,
                new SceneAuthoringBounds(0f, 0f, 64f, 64f),
                WorldOwnerStableId: 42),
            runtime.Snapshot);

        // 模拟 Scene Brush 写入后 Script Scene 因普通 authoring 变更重投影；相同 hash 必须保留网格。
        edit.PaintCell(20, 21, 1);
        (ScriptScene sameScene, PreviewProvider sameProvider, IReadOnlyDictionary<int, int> sameMap) =
            CreateScene(stableId: 42, contentHash: 10);

        AuthoringWorldRefreshResult reused = runtime.Refresh(sameScene, sameMap);

        Assert.Equal(AuthoringWorldRefreshResult.Reused, reused);
        Assert.Equal(0, sameProvider.PopulateCount);
        Assert.Equal(1, sameProvider.AdoptCount);
        Assert.Equal(1, edit.ClearRectCount);
        Assert.Equal(2, edit.PaintCellCount);
        Assert.Equal(1, runtime.Snapshot.Version);

        (ScriptScene changedScene, PreviewProvider changedProvider, IReadOnlyDictionary<int, int> changedMap) =
            CreateScene(stableId: 42, contentHash: 11);

        AuthoringWorldRefreshResult rebuilt = runtime.Refresh(changedScene, changedMap);

        Assert.Equal(AuthoringWorldRefreshResult.Rebuilt, rebuilt);
        Assert.Equal(1, changedProvider.PopulateCount);
        Assert.Equal(2, edit.ClearRectCount);
        Assert.Equal(3, edit.PaintCellCount);
        Assert.Equal(2, runtime.Snapshot.Version);

        ScriptScene empty = new();
        AuthoringWorldRefreshResult cleared = runtime.Refresh(empty, new Dictionary<int, int>());

        Assert.Equal(AuthoringWorldRefreshResult.Cleared, cleared);
        Assert.Equal(3, edit.ClearRectCount);
        Assert.False(runtime.Snapshot.HasWorld);
        Assert.Equal(3, runtime.Snapshot.Version);
        Assert.Equal(
            AuthoringWorldRefreshResult.None,
            runtime.Refresh(empty, new Dictionary<int, int>()));
        Assert.Equal(3, edit.ClearRectCount);
    }

    /// <summary>
    /// 验证 provider 描述不能越过已驻留世界容量。
    /// </summary>
    [Fact]
    public void ProviderDescriptorMustFitResidentWorld()
    {
        AuthoringWorldPreviewRuntime runtime = new(
            new SingleChunkSource(),
            new StubMaterialQuery(),
            new RecordingEditApi());
        (ScriptScene scene, PreviewProvider provider, IReadOnlyDictionary<int, int> map) =
            CreateScene(stableId: 5, contentHash: 1, width: 65, height: 64);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => runtime.Refresh(scene, map));

        Assert.Contains("超出驻留网格", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, provider.PopulateCount);
        Assert.False(runtime.Snapshot.HasWorld);
    }

    private static (ScriptScene Scene, PreviewProvider Provider, IReadOnlyDictionary<int, int> Map) CreateScene(
        int stableId,
        ulong contentHash,
        int width = 64,
        int height = 64)
    {
        ScriptScene scene = new();
        Entity entity = scene.CreateEntity();
        PreviewProvider provider = entity.AddComponent<PreviewProvider>();
        provider.Descriptor = new AuthoringWorldPreviewDescriptor(width, height, contentHash);
        return (scene, provider, new Dictionary<int, int> { [stableId] = entity.Id });
    }

    private sealed class PreviewProvider : Behaviour, IAuthoringWorldPreviewProvider
    {
        public PreviewProvider()
        {
        }

        public AuthoringWorldPreviewDescriptor Descriptor { get; set; } = new(64, 64, 1);

        public int PopulateCount { get; private set; }

        public int AdoptCount { get; private set; }

        public AuthoringWorldPreviewDescriptor DescribeAuthoringWorld()
        {
            return Descriptor;
        }

        public void PopulateAuthoringWorld(in AuthoringWorldPreviewContext context)
        {
            PopulateCount++;
            context.Edit.PaintCell(4, 5, new MaterialId(1));
        }

        public void AdoptAuthoringWorld()
        {
            AdoptCount++;
        }
    }

    private sealed class StubMaterialQuery : IMaterialQuery
    {
        public MaterialId Resolve(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? MaterialId.Invalid : new MaterialId(1);
        }

        public bool TryResolve(string name, out MaterialId id)
        {
            id = Resolve(name);
            return id.IsValid;
        }

        public MaterialInfo GetInfo(MaterialId id)
        {
            return default;
        }
    }

    private sealed class SingleChunkSource : IChunkSource
    {
        private readonly Chunk[] _chunks = [new Chunk(new ChunkCoord(0, 0))];

        public ReadOnlySpan<Chunk> ResidentChunks => _chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            if (coord == new ChunkCoord(0, 0))
            {
                chunk = _chunks[0];
                return true;
            }

            chunk = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }

    private sealed class RecordingEditApi : ISimulationEditApi
    {
        public int PaintCellCount { get; private set; }

        public int ClearRectCount { get; private set; }

        public void PaintCell(int worldX, int worldY, ushort material)
        {
            PaintCellCount++;
        }

        public int PaintRect(int minX, int minY, int maxX, int maxY, ushort material)
        {
            return checked((maxX - minX + 1) * (maxY - minY + 1));
        }

        public void ClearCell(int worldX, int worldY)
        {
        }

        public int ClearRect(int minX, int minY, int maxX, int maxY)
        {
            ClearRectCount++;
            return checked((maxX - minX + 1) * (maxY - minY + 1));
        }

        public void AddTemperature(int worldX, int worldY, float deltaCelsius)
        {
        }

        public void SetTemperature(int worldX, int worldY, float targetCelsius)
        {
        }
    }
}
