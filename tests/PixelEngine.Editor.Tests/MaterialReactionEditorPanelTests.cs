using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 材质/反应编辑器热重载服务测试。
/// </summary>
public sealed class MaterialReactionEditorPanelTests
{
    /// <summary>
    /// 验证文件服务写回 JSON、稳定热重载、刷新热表/反应表，并替换 live 网格里的 tombstone 材质。
    /// </summary>
    [Fact]
    public void FileContentServiceAppliesStableReloadAndReplacesDeletedLiveCells()
    {
        using TempContent temp = TempContent.Create();
        MaterialTable materials = new(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, Density = 100, HeatCapacity = 1 },
        ]);
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.Material[0] = 1;
        TestChunkSource chunks = new(chunk);
        bool hotReloaded = false;
        ReactionTable? reloadedReactions = null;
        FileMaterialReactionContentService service = new(
            temp.MaterialsPath,
            temp.ReactionsPath,
            materials,
            chunks,
            fallbackMaterialId: 0,
            applyReactions: table => reloadedReactions = table,
            applyMaterialHotTable: _ => hotReloaded = true);
        MaterialReactionEditorDocument document = new();
        document.Materials.Add(new MaterialEditorRow { Name = "empty", Type = "Empty", HeatCapacity = 1, TextureId = -1 });
        document.Materials.Add(new MaterialEditorRow { Name = "fire", Type = "Fire", HeatCapacity = 1, TextureId = -1, Tags = "fire" });
        document.Reactions.Add(new ReactionEditorRow { InputA = "fire", InputB = "empty", OutputA = "fire", OutputB = "empty", Probability = 100 });

        MaterialReactionApplyResult result = service.Apply(document);

        Assert.Equal([1], result.MaterialReload.TombstoneIds);
        Assert.Equal(1, result.MaterialReload.AddedCount);
        Assert.Equal(1, result.LiveGridFallbackReplacementCount);
        Assert.Equal(0, chunk.Material[0]);
        Assert.Equal(new DirtyRect(0, 0, 63, 63), chunk.WorkingDirty);
        Assert.True(materials.IsTombstone(1));
        Assert.True(materials.TryGetId("fire", out ushort fireId));
        Assert.Equal(2, fireId);
        Assert.True(hotReloaded);
        Assert.NotNull(reloadedReactions);
        Assert.Contains("fallback 替换了 1 个", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("fire", File.ReadAllText(temp.MaterialsPath), StringComparison.Ordinal);
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord = chunks.ToDictionary(static chunk => chunk.Coord);

        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _byCoord.TryGetValue(coord, out chunk!);
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }

    private sealed class TempContent : IDisposable
    {
        private TempContent(string root)
        {
            Root = root;
            MaterialsPath = Path.Combine(root, "materials.json");
            ReactionsPath = Path.Combine(root, "reactions.json");
        }

        public string Root { get; }

        public string MaterialsPath { get; }

        public string ReactionsPath { get; }

        public static TempContent Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "pixelengine-editor-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(root);
            TempContent temp = new(root);
            File.WriteAllText(temp.MaterialsPath, """{ "materials": [ { "name": "empty", "type": "Empty", "heatCapacity": 1 } ] }""");
            File.WriteAllText(temp.ReactionsPath, """{ "reactions": [] }""");
            return temp;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
