using PixelEngine.Content;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 材质/反应编辑器热重载服务测试。
/// 不变式：材质/反应编辑触发热重载且表内容一致。
/// </summary>
public sealed class MaterialReactionEditorPanelTests
{
    /// <summary>
    /// 验证文件服务写回 JSON、稳定热重载、刷新热表/反应表，并替换 live 网格里的 tombstone 材质。
    /// </summary>
    [Fact]
    public void FileContentServiceAppliesStableReloadAndReplacesDeletedLiveCells()
    {
        // Arrange：准备输入与初始状态
        using TempContent temp = TempContent.Create();
        MaterialTable materials = new(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, Density = 100, HeatCapacity = 1 },
        ]);
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.MaterialBuffer[0] = 1;
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

        // Assert：验证预期结果
        Assert.Equal([1], result.MaterialReload.TombstoneIds);
        Assert.Equal(1, result.MaterialReload.AddedCount);
        Assert.Equal(1, result.LiveGridFallbackReplacementCount);
        Assert.Equal(0, chunk.MaterialBuffer[0]);
        Assert.Equal(new DirtyRect(0, 0, 63, 63), chunk.WorkingDirty);
        Assert.True(materials.IsTombstone(1));
        Assert.True(materials.TryGetId("fire", out ushort fireId));
        Assert.Equal(2, fireId);
        Assert.True(hotReloaded);
        Assert.NotNull(reloadedReactions);
        Assert.Contains("fallback 替换了 1 个", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("fire", File.ReadAllText(temp.MaterialsPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证预览会展开 reaction 输入 tag，并把输出 tag 映射到代表材质。
    /// </summary>
    [Fact]
    public void PreviewExpandsTagInputsAndOutputRepresentatives()
    {
        // Arrange：准备输入与初始状态
        using TempContent temp = TempContent.Create();
        MaterialTable materials = new(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1 },
            new MaterialDef { Id = 1, Name = "water", Type = CellType.Liquid, HeatCapacity = 1 },
            new MaterialDef { Id = 2, Name = "steam", Type = CellType.Gas, HeatCapacity = 1 },
        ]);
        FileMaterialReactionContentService service = new(
            temp.MaterialsPath,
            temp.ReactionsPath,
            materials,
            new TestChunkSource(),
            fallbackMaterialId: 0,
            applyReactions: static _ => { });
        MaterialReactionEditorDocument document = new();
        document.Materials.Add(new MaterialEditorRow { Name = "empty", Type = "Empty", HeatCapacity = 1, TextureId = -1 });
        document.Materials.Add(new MaterialEditorRow { Name = "water", Type = "Liquid", HeatCapacity = 1, TextureId = -1, Tags = "Cold" });
        document.Materials.Add(new MaterialEditorRow { Name = "steam", Type = "Gas", HeatCapacity = 1, TextureId = -1, Tags = "Fire" });
        document.TagRepresentatives.Add(new TagRepresentativeEditorRow { Tag = "Fire", Material = "steam" });
        document.Reactions.Add(new ReactionEditorRow { InputA = "[Cold]", InputB = "empty", OutputA = "[Fire]", OutputB = "empty", Probability = 100 });

        MaterialReactionPreviewResult preview = service.Preview(document);

        // Assert：验证预期结果
        Assert.Equal(3, preview.MaterialCount);
        Assert.Equal(1, preview.SourceReactionCount);
        Assert.Equal(2, preview.PackedReactionCount);
        Assert.Contains("展开后 packed reaction 2 条", preview.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证纹理或音效变化只产生资产重载请求，并保持既有 runtime id 不变。
    /// </summary>
    [Fact]
    public void ApplyReloadsChangedAssetsWithoutChangingStableRuntimeId()
    {
        // Arrange：准备输入与初始状态
        using TempContent temp = TempContent.Create();
        MaterialTable materials = new(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                HeatCapacity = 1,
                TextureId = 3,
                AudioCues = new AudioCueSet { ImpactCue = 1 },
            },
        ]);
        RecordingAssetReloadSink assetSink = new();
        FileMaterialReactionContentService service = new(
            temp.MaterialsPath,
            temp.ReactionsPath,
            materials,
            new TestChunkSource(),
            fallbackMaterialId: 0,
            applyReactions: static _ => { },
            assetReloadSink: assetSink);
        MaterialReactionEditorDocument document = new();
        document.Materials.Add(new MaterialEditorRow
        {
            Name = "empty",
            Type = "Empty",
            HeatCapacity = 1,
            TextureId = 7,
            ImpactCue = 2,
        });

        MaterialReactionApplyResult result = service.Apply(document);

        // Assert：验证预期结果
        Assert.Empty(result.MaterialReload.TombstoneIds);
        Assert.Equal(0, result.MaterialReload.AddedCount);
        Assert.True(materials.TryGetId("empty", out ushort emptyId));
        Assert.Equal(0, emptyId);
        MaterialAssetReloadRequest request = Assert.Single(result.AssetReloads);
        Assert.Equal("empty", request.MaterialName);
        Assert.Equal(0, request.RuntimeId);
        Assert.True(request.TextureChanged);
        Assert.True(request.AudioChanged);
        Assert.Equal(result.AssetReloads, assetSink.Requests);
    }

    /// <summary>
    /// 验证编辑器热重载遇到缺失 RubbleTarget 时走 fallback，并保持材质 runtime id 稳定。
    /// </summary>
    [Fact]
    public void ApplyFallsBackMissingRubbleTargetWithoutReorderingRuntimeIds()
    {
        // Arrange：准备输入与初始状态
        using TempContent temp = TempContent.Create();
        MaterialTable materials = new(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1 },
            new MaterialDef { Id = 1, Name = "stone", Type = CellType.Solid, HeatCapacity = 1, Integrity = 20 },
        ]);
        FileMaterialReactionContentService service = new(
            temp.MaterialsPath,
            temp.ReactionsPath,
            materials,
            new TestChunkSource(),
            fallbackMaterialId: 0,
            applyReactions: static _ => { });
        MaterialReactionEditorDocument document = new();
        document.Materials.Add(new MaterialEditorRow { Name = "empty", Type = "Empty", HeatCapacity = 1, TextureId = -1 });
        document.Materials.Add(new MaterialEditorRow
        {
            Name = "stone",
            Type = "Solid",
            HeatCapacity = 1,
            TextureId = -1,
            MaxIntegrity = 40,
            RubbleTarget = "missing_gravel",
        });

        MaterialReactionApplyResult result = service.Apply(document);

        // Assert：验证预期结果
        Assert.Empty(result.MaterialReload.TombstoneIds);
        Assert.True(materials.TryGetId("stone", out ushort stoneId));
        Assert.Equal(1, stoneId);
        ref readonly MaterialDef stone = ref materials.Get(stoneId);
        Assert.Equal(40, stone.MaxIntegrity);
        Assert.Equal(0, stone.RubbleTarget);
        Assert.Equal(0, materials.Hot.RubbleTarget[stoneId]);
    }

    /// <summary>
    /// 验证材质编辑器文档往返保留 M14 可玩性与视觉字段，避免保存时丢 schema。
    /// </summary>
    [Fact]
    public void MaterialEditorDocumentRoundTripsPlayableAndVisualFields()
    {
        // Arrange：准备输入与初始状态
        MaterialDocumentJson materials = new()
        {
            Materials =
            [
                new MaterialJson
                {
                    Name = "stone",
                    Type = "Solid",
                    HeatCapacity = 1,
                    Durability = 20,
                    Integrity = 80,
                    DestroyedTarget = "gravel",
                    DebrisCount = 4,
                    MineYield = 1,
                    RenderStyle = "Destructible",
                    LegendCategory = "Destructible",
                    EdgeColorBGRA = 0xFF101820,
                    Opacity = 220,
                    HighlightColorBGRA = 0xFF8090A0,
                    DisplayName = "Stone",
                    LegendVisible = false,
                    Tags = ["static", "diggable"],
                },
            ],
        };
        ReactionDocumentJson reactions = new() { Reactions = [] };

        MaterialReactionEditorDocument document = MaterialReactionEditorDocument.FromContent(materials, reactions);
        MaterialDocumentJson roundTripped = document.ToMaterialDocument();
        // Assert：验证预期结果
        MaterialJson row = Assert.Single(roundTripped.Materials!);

        Assert.Equal(20, row.Durability);
        Assert.Equal(80, row.Integrity);
        Assert.Equal("gravel", row.DestroyedTarget);
        Assert.Equal(4, row.DebrisCount);
        Assert.Equal(1, row.MineYield);
        Assert.Equal("Destructible", row.RenderStyle);
        Assert.Equal("Destructible", row.LegendCategory);
        Assert.Equal(0xFF101820u, row.EdgeColorBGRA);
        Assert.Equal((byte)220, row.Opacity);
        Assert.Equal(0xFF8090A0u, row.HighlightColorBGRA);
        Assert.Equal("Stone", row.DisplayName);
        Assert.False(row.LegendVisible);
        Assert.NotNull(row.Tags);
        Assert.Equal(["static", "diggable"], row.Tags);
    }

    /// <summary>
    /// 验证编辑器作者语义别名写回现有 Content schema，并在编辑器侧先 clamp FlowRate 到 MoveCap。
    /// </summary>
    [Fact]
    public void MaterialEditorRowAliasesGameplayAndVisualFieldNames()
    {
        // Arrange：准备输入与初始状态
        MaterialEditorRow row = new()
        {
            Name = "stone",
            Type = "Solid",
            HeatCapacity = 1,
            FlowRate = 255,
            Durability = 12,
            MaxIntegrity = 300,
            RubbleTarget = "gravel",
            DebrisCount = 4,
            MineYield = 2,
            RenderStyle = "Destructible",
            LegendCategory = "Terrain",
            OutlineColorBGRA = 0xFF010203,
            Alpha = 180,
            FlowTintBGRA = 0xFF102030,
            DisplayName = "Stone",
            LegendVisible = true,
        };

        MaterialJson json = row.ToContent();

        // Assert：验证预期结果
        Assert.Equal(32, json.Dispersion);
        Assert.Equal(12, json.Durability);
        Assert.Equal(300, json.Integrity);
        Assert.Equal("gravel", json.DestroyedTarget);
        Assert.Equal(4, json.DebrisCount);
        Assert.Equal(2, json.MineYield);
        Assert.Equal("Destructible", json.RenderStyle);
        Assert.Equal("Terrain", json.LegendCategory);
        Assert.Equal(0xFF010203u, json.EdgeColorBGRA);
        Assert.Equal((byte)180, json.Opacity);
        Assert.Equal(0xFF102030u, json.HighlightColorBGRA);
        Assert.Equal("Stone", json.DisplayName);
        Assert.True(json.LegendVisible);
    }

    /// <summary>
    /// 验证 MaterialLegendPreview 按分类给出全材质只读 swatch 与关键玩法数值。
    /// </summary>
    [Fact]
    public void MaterialLegendPreviewGroupsAllMaterialsWithSwatchAndGameplayValues()
    {
        // Arrange：准备输入与初始状态
        MaterialReactionEditorDocument document = new();
        document.Materials.Add(new MaterialEditorRow
        {
            Name = "lava",
            Type = "Liquid",
            FlowRate = 8,
            BaseColor = 0xFF223344,
            MaxIntegrity = 0,
            RenderStyle = "Hazard",
            LegendCategory = "Hazard",
            OutlineColorBGRA = 0xFF010101,
            Alpha = 200,
            FlowTintBGRA = 0xFF998877,
            DisplayName = "Lava",
            LegendVisible = true,
        });
        document.Materials.Add(new MaterialEditorRow
        {
            Name = "hidden_stone",
            Type = "Solid",
            FlowRate = 99,
            BaseColor = 0xFF556677,
            MaxIntegrity = 120,
            DebrisCount = 3,
            MineYield = 1,
            RenderStyle = "Destructible",
            LegendCategory = "Terrain",
            OutlineColorBGRA = 0xFF111213,
            Alpha = 255,
            FlowTintBGRA = 0xFF445566,
            DisplayName = "Hidden Stone",
            LegendVisible = false,
        });

        MaterialLegendPreview preview = new();
        preview.Rebuild(document);
        MaterialLegendPreviewEntry[] entries = preview.Entries.ToArray();

        // Assert：验证预期结果
        Assert.Equal(2, entries.Length);
        Assert.Equal(MaterialLegendCategory.Terrain, entries[0].LegendCategory);
        Assert.Equal("hidden_stone", entries[0].Name);
        Assert.Equal("Hidden Stone", entries[0].DisplayName);
        Assert.Equal(MaterialRenderStyle.Destructible, entries[0].RenderStyle);
        Assert.Equal(0xFF556677u, entries[0].BaseColorBGRA);
        Assert.Equal(0xFF111213u, entries[0].OutlineColorBGRA);
        Assert.Equal((byte)255, entries[0].Alpha);
        Assert.Equal(0xFF445566u, entries[0].FlowTintBGRA);
        Assert.Equal(120, entries[0].MaxIntegrity);
        Assert.Equal(32, entries[0].FlowRate);
        Assert.Equal(3, entries[0].DebrisCount);
        Assert.Equal(1, entries[0].MineYield);
        Assert.False(entries[0].LegendVisible);
        Assert.Equal(MaterialLegendCategory.Hazard, entries[1].LegendCategory);
        Assert.Equal("lava", entries[1].Name);
    }

    /// <summary>
    /// 验证编辑器预览与玩家 HUD 为独立 UI，不通过 Demo PlayableHud 复用实现。
    /// </summary>
    [Fact]
    public void MaterialLegendPreviewIsEditorOnlyAndDoesNotReusePlayableHud()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string panelSource = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "MaterialReactionEditorPanel.cs"));
        string editorSource = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "MaterialLegendPreview.cs"));
        string demoHudSource = File.ReadAllText(Path.Combine(root, "demo", "PixelEngine.Demo", "PlayableHud.cs"));

        // Assert：验证预期结果
        Assert.Contains("flow rate", panelSource, StringComparison.Ordinal);
        Assert.Contains("max integrity", panelSource, StringComparison.Ordinal);
        Assert.Contains("rubble target", panelSource, StringComparison.Ordinal);
        Assert.Contains("render style", panelSource, StringComparison.Ordinal);
        Assert.Contains("legend category", panelSource, StringComparison.Ordinal);
        Assert.Contains("MaterialLegendPreview", editorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayableHud", editorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterialLegendPreview", demoHudSource, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
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
            File.WriteAllText(temp.MaterialsPath, /*lang=json,strict*/ """{ "materials": [ { "name": "empty", "type": "Empty", "heatCapacity": 1 } ] }""");
            File.WriteAllText(temp.ReactionsPath, /*lang=json,strict*/ """{ "reactions": [] }""");
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

    private sealed class RecordingAssetReloadSink : IMaterialAssetReloadSink
    {
        public IReadOnlyList<MaterialAssetReloadRequest> Requests { get; private set; } = [];

        public void ReloadMaterialAssets(IReadOnlyList<MaterialAssetReloadRequest> requests)
        {
            Requests = requests;
        }
    }
}
