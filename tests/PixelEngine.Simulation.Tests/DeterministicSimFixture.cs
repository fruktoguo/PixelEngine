using System.Text;
using PixelEngine.Core;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// CA 确定性回归测试基座：固定 3x3 世界、固定 seed、单线程推进和规范化快照。
/// </summary>
internal sealed class DeterministicSimFixture
{
    public const ushort Empty = 0;
    public const ushort Solid = 1;
    public const ushort Sand = 2;
    public const ushort Water = 3;
    public const ushort Lava = 4;
    public const ushort Rock = 5;
    public const ushort Steam = 6;
    public const ushort Ice = 7;

    public DeterministicSimFixture()
    {
        Source = TestChunkSource.CreateDense(-1, -1, 1, 1);
        Materials = CreateMaterials();
    }

    public TestChunkSource Source { get; }

    public MaterialTable Materials { get; }

    public Chunk Center => Source.GetRequired(new ChunkCoord(0, 0));

    public Chunk East => Source.GetRequired(new ChunkCoord(1, 0));

    public SimulationKernel CreateKernel(IReactionExecutor? reactions = null)
    {
        return new SimulationKernel(Source, new MaterialPropsTable(Materials.Hot), worldSeed: 0xC0FFEEUL, reactionExecutor: reactions)
        {
            ForceSingleThread = true,
        };
    }

    public ReactionEngine CreateLavaWaterReactionEngine(byte probability = byte.MaxValue)
    {
        Reaction[] packed =
        [
            new Reaction
            {
                InputA = Lava,
                InputB = Water,
                OutputA = Rock,
                OutputB = Steam,
                Probability = probability,
            },
        ];
        MaterialDef[] definitions = CreateMaterialDefs(reactionCountForLava: 1);
        return new ReactionEngine(new MaterialTable(definitions), new ReactionTable(packed, definitions));
    }

    public string ExportNormalizedSnapshot()
    {
        StringBuilder builder = new();
        _ = builder.AppendLine("snapshot:v1");
        _ = builder.AppendLine("chunks:-1,-1..1,1");
        _ = builder.AppendLine("cells:");
        int cellCount = 0;
        foreach (Chunk chunk in Source.ResidentChunks.ToArray().OrderBy(static chunk => chunk.Coord.Y).ThenBy(static chunk => chunk.Coord.X))
        {
            int baseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
            int baseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
            for (int ly = 0; ly < EngineConstants.ChunkSize; ly++)
            {
                for (int lx = 0; lx < EngineConstants.ChunkSize; lx++)
                {
                    int local = CellAddressing.LocalIndexFromLocal(lx, ly);
                    ushort material = chunk.Material[local];
                    byte persistentFlags = (byte)(chunk.Flags[local] & CellFlags.Burning);
                    if (material == Empty && persistentFlags == 0)
                    {
                        continue;
                    }

                    string name = material == Empty ? "empty" : Materials.GetName(material);
                    _ = builder
                        .Append(baseX + lx)
                        .Append(',')
                        .Append(baseY + ly)
                        .Append(':')
                        .Append(name)
                        .Append(":flags=")
                        .Append(persistentFlags.ToString("X2"))
                        .AppendLine();
                    cellCount++;
                }
            }
        }

        _ = builder.Append("nonEmpty:").Append(cellCount);
        return builder.ToString();
    }

    public static string ReadGolden(string fileName)
    {
        string path = Path.Combine(FindRepositoryRoot(), "tests", "PixelEngine.Simulation.Tests", "__golden__", fileName);
        return Normalize(File.ReadAllText(path));
    }

    public static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }

    public static MaterialTable CreateMaterials()
    {
        return new MaterialTable(CreateMaterialDefs(reactionCountForLava: 0));
    }

    public static MaterialDef[] CreateMaterialDefs(byte reactionCountForLava)
    {
        MaterialDef[] definitions =
        [
            Def(Empty, "empty", CellType.Empty, density: 0),
            Def(Solid, "solid", CellType.Solid, density: 255),
            Def(Sand, "sand", CellType.Powder, density: 120),
            Def(Water, "water", CellType.Liquid, density: 60, dispersion: 3),
            Def(Lava, "lava", CellType.Liquid, density: 180, dispersion: 1) with
            {
                ReactionStart = 0,
                ReactionCount = reactionCountForLava,
            },
            Def(Rock, "rock", CellType.Solid, density: 255),
            Def(Steam, "steam", CellType.Gas, density: 1, dispersion: 2),
            Def(Ice, "ice", CellType.Solid, density: 240) with
            {
                MeltPoint = 10,
                MeltTarget = Water,
            },
        ];
        return definitions;
    }

    public void Set(Chunk chunk, int lx, int ly, ushort material, byte flags = 0)
    {
        int local = CellAddressing.LocalIndexFromLocal(lx, ly);
        chunk.Material[local] = material;
        chunk.Flags[local] = flags;
    }

    private static MaterialDef Def(ushort id, string name, CellType type, byte density, byte dispersion = 0)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = density,
            Dispersion = dispersion,
            HeatCapacity = 1,
            TextureId = -1,
        };
    }

    private static string FindRepositoryRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "PixelEngine.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }

    internal sealed class TestChunkSource : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord;
        private readonly Chunk[] _resident;

        private TestChunkSource(Chunk[] chunks)
        {
            _resident = chunks;
            _byCoord = new Dictionary<ChunkCoord, Chunk>(chunks.Length);
            foreach (Chunk chunk in chunks)
            {
                _byCoord.Add(chunk.Coord, chunk);
            }
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

        public static TestChunkSource CreateDense(int minX, int minY, int maxX, int maxY)
        {
            Chunk[] chunks = new Chunk[(maxX - minX + 1) * (maxY - minY + 1)];
            int index = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    chunks[index++] = new Chunk(new ChunkCoord(x, y));
                }
            }

            return new TestChunkSource(chunks);
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _byCoord.TryGetValue(coord, out chunk!);
        }

        public Chunk GetRequired(ChunkCoord coord)
        {
            return _byCoord[coord];
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (!TryGetChunk(new ChunkCoord(center.X - 1, center.Y - 1), out Chunk slot0) ||
                !TryGetChunk(new ChunkCoord(center.X, center.Y - 1), out Chunk slot1) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y - 1), out Chunk slot2) ||
                !TryGetChunk(new ChunkCoord(center.X - 1, center.Y), out Chunk slot3) ||
                !TryGetChunk(center, out Chunk slot4) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y), out Chunk slot5) ||
                !TryGetChunk(new ChunkCoord(center.X - 1, center.Y + 1), out Chunk slot6) ||
                !TryGetChunk(new ChunkCoord(center.X, center.Y + 1), out Chunk slot7) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y + 1), out Chunk slot8))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(slot0, slot1, slot2, slot3, slot4, slot5, slot6, slot7, slot8);
            return true;
        }
    }
}
