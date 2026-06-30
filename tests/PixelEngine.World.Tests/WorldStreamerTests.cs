using System.Buffers;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// WorldStreamer 与 WorldManager 相位分离测试。
/// </summary>
public sealed class WorldStreamerTests
{
    /// <summary>
    /// 验证 SubmitPlan 在相位 2 摘下待卸载 chunk，后台 I/O 后才移除记账。
    /// </summary>
    [Fact]
    public void SubmitPlanDetachesUnloadWithoutDoingIoAndApplyPreparedFinalizes()
    {
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        MaterialRemap remap = IdentityRemap();
        WorldStreamer streamer = new(chunks, residency, budget, temperature, store, remap);
        ChunkCoord coord = new(2, 3);
        Chunk chunk = new(coord);
        chunk.Material[0] = 1;
        chunks.Add(chunk);
        residency.Set(coord, new ChunkResidencyInfo(ChunkResidencyState.Cached, 1, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: true));
        budget.Add(ChunkMemoryBudget.EstimatedResidentChunkBytes);
        temperature.AddHeat((coord.X << 6) + 4, (coord.Y << 6) + 4, 12.5f);
        ResidencyPlan plan = new([], [coord], [new ResidencyStateChange(coord, ChunkResidencyState.Detached)]);

        streamer.SubmitPlan(plan);

        Assert.False(chunks.Contains(coord));
        Assert.True(residency.TryGetInfo(coord, out ChunkResidencyInfo detached));
        Assert.Equal(ChunkResidencyState.Detached, detached.State);
        Assert.False(store.Exists(coord));
        Assert.Equal(1, streamer.PendingRequestCount);

        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.True(store.Exists(coord));
        Assert.Equal(1, streamer.ApplyPrepared(frame: 9));

        Assert.False(residency.TryGetInfo(coord, out _));
        Assert.Equal(0, budget.ResidentBytes);
    }

    /// <summary>
    /// 验证后台装载从 chunk store 读取、重映射 material，并在相位 2 插回 live map。
    /// </summary>
    [Fact]
    public void ProcessIoLoadsChunkRemapsMaterialAndApplyPreparedAddsResident()
    {
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        ChunkCoord coord = new(-1, 0);
        WriteStoredChunk(store, coord, savedMaterial: 1, temp: (Half)22f);
        MaterialTable currentMaterials = Materials(("empty", CellType.Empty), ("water", CellType.Liquid), ("sand", CellType.Powder));
        MaterialRemap remap = MaterialRemap.Build(new MaterialNameTable([(0, "empty"), (1, "sand")]), currentMaterials, fallbackId: 0);
        WorldStreamer streamer = new(chunks, residency, budget, temperature, store, remap)
        {
            CurrentParityBit = CellFlags.Parity,
        };
        ResidencyPlan plan = new([coord], [], []);

        streamer.SubmitPlan(plan);
        Assert.False(chunks.Contains(coord));
        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.Equal(1, streamer.ApplyPrepared(frame: 5));

        Assert.True(chunks.TryGetChunk(coord, out Chunk loaded));
        Assert.Equal(2, loaded.Material[0]);
        Assert.Equal(DirtyRect.Full, loaded.CurrentDirty);
        Assert.Equal(22, temperature.GetTemperature(coord.X << 6, coord.Y << 6));
        Assert.True(residency.TryGetInfo(coord, out ChunkResidencyInfo info));
        Assert.Equal(ChunkResidencyState.Cached, info.State);
        Assert.Equal(ChunkMemoryBudget.EstimatedResidentChunkBytes, budget.ResidentBytes);
    }

    /// <summary>
    /// 验证 WorldManager façade 会按相机 active/border 计算提交装载请求。
    /// </summary>
    [Fact]
    public void WorldManagerApplyResidencySubmitsLoadsForBorderArea()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        WorldManager manager = new(
            new WorldCamera(32, 32, viewportCellsX: 64, viewportCellsY: 64),
            new TemperatureField(),
            Materials(("empty", CellType.Empty)),
            world.Path,
            fallbackMaterialId: 0,
            new WorldStreamingConfig
            {
                ActivationMarginChunks = 0,
                BorderRingWidth = 1,
                MaxStreamOpsPerFrame = 16,
            });

        manager.ApplyResidency(frame: 1);

        Assert.Equal(9, manager.Streamer.PendingRequestCount);
        Assert.Equal(9, manager.Residency.Count);
    }

    private static void WriteStoredChunk(MemoryChunkStore store, ChunkCoord coord, ushort savedMaterial, Half temp)
    {
        Chunk chunk = new(coord);
        chunk.Material[0] = savedMaterial;
        Half[] temperature = new Half[TemperatureField.BlockArea];
        temperature[0] = temp;
        ArrayBufferWriter<byte> writer = new();
        new ChunkCodec().Encode(new ChunkSnapshot(coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature), writer);
        store.Write(coord, writer.WrittenSpan);
    }

    private static MaterialRemap IdentityRemap()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        MaterialNameTable names = new(materials.BuildIdNameTable());
        return MaterialRemap.Build(names, materials, fallbackId: 0);
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static ChunkMemoryBudget Budget()
    {
        return new ChunkMemoryBudget(
            ChunkMemoryBudget.EstimatedResidentChunkBytes * 8L,
            ChunkMemoryBudget.EstimatedResidentChunkBytes * 4L);
    }

    private sealed class MemoryChunkStore : IChunkStore
    {
        private readonly Dictionary<ChunkCoord, byte[]> _blobs = [];

        public bool TryRead(ChunkCoord coord, IBufferWriter<byte> destination)
        {
            if (!_blobs.TryGetValue(coord, out byte[]? blob))
            {
                return false;
            }

            Span<byte> span = destination.GetSpan(blob.Length);
            blob.CopyTo(span);
            destination.Advance(blob.Length);
            return true;
        }

        public void Write(ChunkCoord coord, ReadOnlySpan<byte> blob)
        {
            _blobs[coord] = blob.ToArray();
        }

        public bool Exists(ChunkCoord coord)
        {
            return _blobs.ContainsKey(coord);
        }

        public void Delete(ChunkCoord coord)
        {
            _ = _blobs.Remove(coord);
        }
    }

    private sealed class TempWorldDirectory : IDisposable
    {
        private TempWorldDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorldDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PixelEngine.WorldStreamerTests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(path);
            return new TempWorldDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
