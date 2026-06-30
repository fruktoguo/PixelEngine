using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// 世界级存档 manifest，保存 chunk 之外的全局态与 chunk 索引。
/// </summary>
public sealed class WorldManifest
{
    private readonly byte[] _playerStateBlob;
    private readonly FreeParticleSnapshot[] _freeParticles;
    private readonly RigidBodySnapshot[] _rigidBodies;
    private readonly ChunkCoord[] _chunkIndex;

    /// <summary>
    /// 创建 world manifest。
    /// </summary>
    public WorldManifest(
        int formatVersion,
        ulong worldSeed,
        long gameTimeTicks,
        ReadOnlySpan<byte> playerStateBlob,
        MaterialNameTable materialNames,
        ReadOnlySpan<FreeParticleSnapshot> freeParticles,
        ReadOnlySpan<RigidBodySnapshot> rigidBodies,
        ReadOnlySpan<ChunkCoord> chunkIndex)
    {
        ArgumentNullException.ThrowIfNull(materialNames);
        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion), "manifest 格式版本必须为正。");
        }

        if (gameTimeTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameTimeTicks), "游戏时间不能为负。");
        }

        for (int i = 0; i < rigidBodies.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(rigidBodies[i]);
        }

        FormatVersion = formatVersion;
        WorldSeed = worldSeed;
        GameTimeTicks = gameTimeTicks;
        MaterialNames = materialNames;
        _playerStateBlob = playerStateBlob.ToArray();
        _freeParticles = freeParticles.ToArray();
        _rigidBodies = rigidBodies.ToArray();
        _chunkIndex = chunkIndex.ToArray();
    }

    /// <summary>
    /// 当前 manifest 格式版本。
    /// </summary>
    public int FormatVersion { get; }

    /// <summary>
    /// 世界种子。
    /// </summary>
    public ulong WorldSeed { get; }

    /// <summary>
    /// 当前游戏时间 tick。
    /// </summary>
    public long GameTimeTicks { get; }

    /// <summary>
    /// 由宿主提供的不透明玩家状态。
    /// </summary>
    public ReadOnlyMemory<byte> PlayerStateBlob => _playerStateBlob;

    /// <summary>
    /// 存档时 runtime material id 到稳定 name 的映射表。
    /// </summary>
    public MaterialNameTable MaterialNames { get; }

    /// <summary>
    /// 在飞自由粒子快照。
    /// </summary>
    public ReadOnlyMemory<FreeParticleSnapshot> FreeParticles => _freeParticles;

    /// <summary>
    /// 刚体快照。
    /// </summary>
    public ReadOnlyMemory<RigidBodySnapshot> RigidBodies => _rigidBodies;

    /// <summary>
    /// manifest 覆盖的 chunk 坐标索引。
    /// </summary>
    public ReadOnlyMemory<ChunkCoord> ChunkIndex => _chunkIndex;
}
