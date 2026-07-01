using System.Buffers.Binary;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Core.Time;
using PixelEngine.Audio;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// ScriptSimulationContext facade 的真实后端验收。
/// </summary>
public sealed class ScriptSimulationContextTests
{
    /// <summary>
    /// 验证材质、cell 与固体采样 facade 直接读取真实 Simulation 后端。
    /// </summary>
    [Fact]
    public void FacadesReadMaterialCellsAndSolidsFromSimulationBackends()
    {
        Fixture fixture = Fixture.Create();
        fixture.Grid.SetMaterial(4, 4, 2);
        fixture.Grid.FlagsAt(4, 4) = 7;
        fixture.Grid.LifetimeAt(4, 4) = 9;

        Assert.True(fixture.Context.Materials.TryResolve("stone", out MaterialId stone));
        Assert.Equal((ushort)2, stone.Value);
        MaterialInfo info = fixture.Context.Materials.GetInfo(stone);
        Assert.Equal("stone", info.Name);
        Assert.True(info.IsSolid);
        Assert.Equal(stone, fixture.Context.Cells.GetMaterial(4, 4));
        Assert.Equal(new CellView(stone, 7, 9), fixture.Context.Cells.Sample(4, 4));
        Assert.True(fixture.Context.Cells.IsSolid(4, 4));
        Assert.True(fixture.Context.Solids.SampleSolidAabb(3.5f, 3.5f, 2, 2));

        Assert.True(fixture.Context.Solids.Raycast(0, 4, 1, 0, 8, out RaycastHit hit));
        Assert.Equal(4, hit.X);
        Assert.Equal(4, hit.Y);
        Assert.Equal(stone, hit.Material);
    }

    /// <summary>
    /// 验证脚本 cell 写命令延迟到 flush 后写入 working dirty，供下一次 CA 可见。
    /// </summary>
    [Fact]
    public void CellCommandsFlushIntoWorkingDirtyWithoutImmediateMutation()
    {
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Cells.SetCell(2, 2, sand);
        Assert.Equal((ushort)0, fixture.Grid.GetMaterial(2, 2));
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);

        int flushed = fixture.Context.FlushCellCommands();

        Assert.Equal(1, flushed);
        Assert.Equal(sand.Value, fixture.Grid.GetMaterial(2, 2));
        Assert.True(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.False(fixture.Chunk.WorkingDirty.IsEmpty);

        fixture.Kernel.SwapDirtyRects();
        Assert.False(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);
    }

    /// <summary>
    /// 验证脚本粒子命令延迟到粒子 flush 后进入真实 ParticleSystem。
    /// </summary>
    [Fact]
    public void ParticleCommandsFlushIntoParticleSystem()
    {
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Particles.Spawn(new ParticleSpawnDesc(1, 2, 3, 4, sand, 5));
        fixture.Context.Particles.Burst(8, 9, sand, count: 3, speed: 6);
        Assert.Equal(0, fixture.Particles.ActiveCount);

        int flushed = fixture.Context.FlushParticleCommands();

        Assert.Equal(2, flushed);
        Assert.Equal(4, fixture.Particles.ActiveCount);
        Particle first = fixture.Particles.ActiveReadOnly[0];
        Assert.Equal(1, first.X);
        Assert.Equal(2, first.Y);
        Assert.Equal(3, first.Vx);
        Assert.Equal(4, first.Vy);
        Assert.Equal(sand.Value, first.Material);
        Assert.Equal(5, first.Life);
    }

    /// <summary>
    /// 验证脚本角色控制器 facade 使用真实像素碰撞后端，并在同一调用返回移动结果。
    /// </summary>
    [Fact]
    public void CharacterFacadeMovesAgainstSolidPixelsAndReturnsCollisionState()
    {
        Fixture fixture = Fixture.Create();
        FillRect(fixture.Chunk, minX: 0, minY: 10, maxX: 32, maxY: 11, material: 2);
        CharacterHandle handle = fixture.Context.Character.Create(4, 0, 4, 4);

        CharacterState state = fixture.Context.Character.Move(handle, 0, 20);

        Assert.True(state.OnGround);
        Assert.False(state.OnCeiling);
        Assert.False(state.OnWall);
        Assert.Equal(4f, state.X);
        Assert.Equal(6f, state.Y);
        Assert.Equal(4f, state.Width);
        Assert.Equal(4f, state.Height);
        Assert.Equal(20f, state.RequestedDeltaY);
        Assert.Equal(6f, state.AppliedDeltaY);
        Assert.Equal(0f, state.GroundNormalX, precision: 5);
        Assert.Equal(-1f, state.GroundNormalY, precision: 5);
        Assert.Equal(state, fixture.Context.Character.GetState(handle));

        CharacterState teleported = fixture.Context.Character.SetPosition(handle, 12, 1);

        Assert.Equal(12f, teleported.X);
        Assert.Equal(1f, teleported.Y);
        Assert.Equal(0f, teleported.AppliedDeltaX);
        Assert.Equal(0f, teleported.AppliedDeltaY);
        Assert.Equal(teleported, fixture.Context.Character.GetState(handle));
    }

    /// <summary>
    /// 验证脚本时间 facade 从真实 FrameClock 读取固定步长、帧号与本帧 sim 决策。
    /// </summary>
    [Fact]
    public void ScriptFrameTimeReadsFromFrameClock()
    {
        FrameClock clock = new(PixelEngine.Core.EngineConstants.SimHzDownscaled);
        ScriptFrameTime time = new(clock);

        _ = clock.BeginFrame(0);

        Assert.Equal(1, time.FrameCount);
        Assert.Equal((float)clock.Dt, time.FixedStep);
        Assert.Equal((float)clock.Dt, time.DeltaTime);
        Assert.True(time.SimSteppedThisFrame);

        _ = clock.BeginFrame(clock.Dt * 2);

        Assert.Equal(2, time.FrameCount);
        Assert.False(time.SimSteppedThisFrame);
        Assert.True(time.TimeScale < 1f);
    }

    /// <summary>
    /// 验证脚本上下文可注入相机与输入后端，供 Behaviour 通过统一入口访问。
    /// </summary>
    [Fact]
    public void ScriptContextExposesInjectedCameraAndInputBackends()
    {
        Fixture fixture = Fixture.Create(
            camera: new ScriptCameraApi(100, 50, centerX: 10, centerY: 20),
            input: new ScriptInputApi(),
            lighting: new ScriptLightingApi());
        ((ScriptInputApi)fixture.Context.Input).Update([Key.Space], [MouseButton.Middle], 1, 2, 3);
        fixture.Context.Lighting.RevealAround(10, 20, 8);

        Assert.Equal(10f, fixture.Context.Camera.CenterX);
        Assert.True(fixture.Context.Input.WasPressed(Key.Space));
        Assert.True(fixture.Context.Input.WasMousePressed(MouseButton.Middle));
        Assert.Equal(3f, fixture.Context.Input.MouseWheelY);
        Assert.Equal(1, fixture.Context.Lighting.RevealCount);
    }

    /// <summary>
    /// 验证脚本音频 facade 只播放已加载 cue，并把位置/音量交给真实 AudioSystem。
    /// </summary>
    [Fact]
    public async Task ScriptAudioApiPlaysLoadedCueThroughAudioSystem()
    {
        byte[] wav = CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]);
        using NullAudioBackend backend = new();
        AudioClipCache cache = new(backend, new MemoryAssetStore(wav), new WavDecoder());
        _ = await cache.LoadAsync("sfx/hit.wav");
        using AudioSystem audio = new();
        audio.Initialize(new AudioSettings { MaxVoices = 1, PixelsPerMeter = 16f, SfxVolume = 0.5f }, backend);
        ScriptAudioApi api = new(audio, cache);

        api.PlayAt("sfx/hit.wav", 32, 16, volume: 0.25f);

        Assert.Equal(1, backend.PlayCalls);
        Assert.Equal(new System.Numerics.Vector3(2f, 1f, 0f), backend.GetSourcePosition(1));
        Assert.Equal(0.125f, backend.GetSourceGain(1), precision: 5);
        _ = Assert.Throws<InvalidOperationException>(() => api.PlayAt("sfx/missing.wav", 0, 0));
        audio.Shutdown();
    }

    private sealed class Fixture
    {
        private Fixture(
            Chunk chunk,
            CellGrid grid,
            SimulationKernel kernel,
            ParticleSystem particles,
            ScriptSimulationContext context)
        {
            Chunk = chunk;
            Grid = grid;
            Kernel = kernel;
            Particles = particles;
            Context = context;
        }

        public Chunk Chunk { get; }

        public CellGrid Grid { get; }

        public SimulationKernel Kernel { get; }

        public ParticleSystem Particles { get; }

        public ScriptSimulationContext Context { get; }

        public static Fixture Create(ICameraApi? camera = null, IInputApi? input = null, ILightingApi? lighting = null)
        {
            MaterialTable materials = Materials(
                ("empty", CellType.Empty),
                ("sand", CellType.Powder),
                ("stone", CellType.Solid));
            Chunk chunk = new(new ChunkCoord(0, 0));
            TestChunkSource chunks = new(chunk);
            MaterialPropsTable props = new(materials.Hot);
            CellGrid grid = new(chunks, props);
            SimulationKernel kernel = new(chunks, props);
            ParticleSystem particles = new(capacity: 16);
            ScriptSimulationContext context = new(new ScriptScene(), grid, kernel, particles, materials, camera: camera, input: input, lighting: lighting);
            return new Fixture(chunk, grid, kernel, particles, context);
        }
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
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static byte[] CreateWav(short channels, short bitsPerSample, int sampleRate, ReadOnlySpan<byte> pcm)
    {
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        byte[] wav = new byte[44 + pcm.Length];
        WriteAscii(wav, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + pcm.Length);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        WriteAscii(wav, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static void FillRect(Chunk chunk, int minX, int minY, int maxX, int maxY, ushort material)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                chunk.Material[CellAddressing.LocalIndexFromLocal(x, y)] = material;
            }
        }
    }

    private static void WriteAscii(byte[] destination, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            destination[offset + i] = (byte)text[i];
        }
    }

    private sealed class MemoryAssetStore(byte[] bytes) : IAudioAssetStore
    {
        public ValueTask<byte[]> LoadBytesAsync(string assetId, CancellationToken cancellationToken = default)
        {
            _ = assetId;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(bytes);
        }
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Coord == coord)
                {
                    chunk = chunks[i];
                    return true;
                }
            }

            chunk = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (!TryGetChunk(center, out Chunk chunk))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk);
            return true;
        }
    }
}
