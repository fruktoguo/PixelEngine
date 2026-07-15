using PixelEngine.Serialization;
using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 在 world 安全点冻结的完整深快照。快照不再引用 live chunk、温度场、粒子或刚体，
/// 因而可交给后台线程编码或由后续安全点一次性应用。
/// </summary>
public sealed class WorldSaveSnapshot
{
    private readonly WorldSnapshotChunk[] _chunks;

    internal WorldSaveSnapshot(
        WorldManifest manifest,
        WorldSnapshotChunk[] chunks,
        byte currentParity,
        long materialFallbackHitCount)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        if (chunks.Length != manifest.ChunkIndex.Length)
        {
            throw new ArgumentException("快照 chunk 数与 manifest 索引不一致。", nameof(chunks));
        }

        if (materialFallbackHitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(materialFallbackHitCount));
        }

        CurrentParity = (byte)(currentParity & CellFlags.Parity);
        MaterialFallbackHitCount = materialFallbackHitCount;
    }

    /// <summary>世界随机种子。</summary>
    public ulong WorldSeed => Manifest.WorldSeed;

    /// <summary>快照对应的游戏 tick。</summary>
    public long GameTimeTicks => Manifest.GameTimeTicks;

    /// <summary>快照中的 resident chunk 数量。</summary>
    public int ChunkCount => _chunks.Length;

    /// <summary>快照覆盖的 resident chunk 坐标。</summary>
    public ReadOnlyMemory<ChunkCoord> ChunkCoordinates => Manifest.ChunkIndex;

    /// <summary>快照对应的 CA parity 位。</summary>
    public byte CurrentParity { get; }

    /// <summary>后台读档材质重映射产生的 fallback 命中数。</summary>
    public long MaterialFallbackHitCount { get; }

    /// <summary>
    /// 判断两份快照是否表示相同的可观察 world 状态。
    /// 材质 fallback 诊断计数与 chunk LRU 最近触碰帧不属于持久 world 内容，比较时忽略；
    /// cell/温度、dirty/KeepAlive、parity、驻留状态、粒子、刚体与全局时间线均参与比较。
    /// </summary>
    /// <param name="other">另一份完整快照。</param>
    /// <returns>两份快照的 world 语义完全相同时返回 <see langword="true" />。</returns>
    public bool ContentEquals(WorldSaveSnapshot? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || CurrentParity != other.CurrentParity ||
            !ManifestEquals(Manifest, other.Manifest) || _chunks.Length != other._chunks.Length)
        {
            return false;
        }

        bool sameOrder = true;
        for (int i = 0; i < _chunks.Length; i++)
        {
            if (_chunks[i].Coord != other._chunks[i].Coord)
            {
                sameOrder = false;
                break;
            }
        }

        if (sameOrder)
        {
            for (int i = 0; i < _chunks.Length; i++)
            {
                if (!ChunkEquals(_chunks[i], other._chunks[i]))
                {
                    return false;
                }
            }

            return true;
        }

        Dictionary<ChunkCoord, WorldSnapshotChunk> otherByCoord = new(other._chunks.Length);
        for (int i = 0; i < other._chunks.Length; i++)
        {
            if (!otherByCoord.TryAdd(other._chunks[i].Coord, other._chunks[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < _chunks.Length; i++)
        {
            if (!otherByCoord.TryGetValue(_chunks[i].Coord, out WorldSnapshotChunk? candidate) ||
                !ChunkEquals(_chunks[i], candidate))
            {
                return false;
            }
        }

        return true;
    }

    internal WorldManifest Manifest { get; }

    internal ReadOnlySpan<WorldSnapshotChunk> Chunks
        => _chunks;

    private static bool ManifestEquals(WorldManifest left, WorldManifest right)
    {
        if (left.FormatVersion != right.FormatVersion ||
            left.WorldSeed != right.WorldSeed ||
            left.GameTimeTicks != right.GameTimeTicks ||
            !left.PlayerStateBlob.Span.SequenceEqual(right.PlayerStateBlob.Span) ||
            !left.FreeParticles.Span.SequenceEqual(right.FreeParticles.Span) ||
            !MaterialNamesEqual(left.MaterialNames, right.MaterialNames))
        {
            return false;
        }

        ReadOnlySpan<RigidBodySnapshot> leftBodies = left.RigidBodies.Span;
        ReadOnlySpan<RigidBodySnapshot> rightBodies = right.RigidBodies.Span;
        if (leftBodies.Length != rightBodies.Length)
        {
            return false;
        }

        for (int i = 0; i < leftBodies.Length; i++)
        {
            if (!RigidBodyEquals(leftBodies[i], rightBodies[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MaterialNamesEqual(MaterialNameTable left, MaterialNameTable right)
    {
        ReadOnlySpan<(ushort Id, string Name)> leftEntries = left.Entries;
        ReadOnlySpan<(ushort Id, string Name)> rightEntries = right.Entries;
        if (leftEntries.Length != rightEntries.Length)
        {
            return false;
        }

        for (int i = 0; i < leftEntries.Length; i++)
        {
            if (leftEntries[i].Id != rightEntries[i].Id ||
                !string.Equals(leftEntries[i].Name, rightEntries[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RigidBodyEquals(RigidBodySnapshot left, RigidBodySnapshot right)
    {
        return left.Id == right.Id &&
            left.Width == right.Width &&
            left.Height == right.Height &&
            left.BodyLocalMask.Span.SequenceEqual(right.BodyLocalMask.Span) &&
            left.Material.Span.SequenceEqual(right.Material.Span) &&
            left.PosX.Equals(right.PosX) &&
            left.PosY.Equals(right.PosY) &&
            left.RotCos.Equals(right.RotCos) &&
            left.RotSin.Equals(right.RotSin) &&
            left.LinVelX.Equals(right.LinVelX) &&
            left.LinVelY.Equals(right.LinVelY) &&
            left.AngVel.Equals(right.AngVel) &&
            left.LocalOriginX.Equals(right.LocalOriginX) &&
            left.LocalOriginY.Equals(right.LocalOriginY);
    }

    private static bool ChunkEquals(WorldSnapshotChunk left, WorldSnapshotChunk right)
    {
        return left.Coord == right.Coord &&
            left.Material.AsSpan().SequenceEqual(right.Material) &&
            left.Flags.AsSpan().SequenceEqual(right.Flags) &&
            left.Lifetime.AsSpan().SequenceEqual(right.Lifetime) &&
            left.Damage.AsSpan().SequenceEqual(right.Damage) &&
            left.CurrentDirty == right.CurrentDirty &&
            left.WorkingDirty == right.WorkingDirty &&
            left.IncomingDirty.AsSpan().SequenceEqual(right.IncomingDirty) &&
            left.Parity == right.Parity &&
            left.Temperature.AsSpan().SequenceEqual(right.Temperature) &&
            ResidencyEquals(left.Residency, right.Residency);
    }

    private static bool ResidencyEquals(ChunkResidencyInfo left, ChunkResidencyInfo right)
    {
        return left.State == right.State &&
            left.ResidentBytes == right.ResidentBytes &&
            left.DirtySinceLoad == right.DirtySinceLoad;
    }
}

internal sealed record WorldSnapshotChunk(
    ChunkCoord Coord,
    ushort[] Material,
    byte[] Flags,
    byte[] Lifetime,
    byte[] Damage,
    DirtyRect CurrentDirty,
    DirtyRect WorkingDirty,
    DirtyRect[] IncomingDirty,
    byte Parity,
    Half[] Temperature,
    ChunkResidencyInfo Residency);
