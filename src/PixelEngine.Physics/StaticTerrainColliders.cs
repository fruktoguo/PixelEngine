using System.Buffers;
using System.Numerics;
using PixelEngine.Core;
using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 为活跃刚体邻近 dirty chunk 生成用后即弃的 Box2D 静态地形 chain。
/// </summary>
public sealed unsafe class StaticTerrainColliders : IDisposable
{
    private readonly B2WorldId _worldId;
    private readonly int _expandedChunkRadius;
    private readonly Dictionary<ChunkCoord, TerrainCollider> _colliders = [];
    private bool _disposed;

    /// <summary>
    /// 创建静态地形 collider 管理器。
    /// </summary>
    /// <param name="worldId">Box2D world。</param>
    /// <param name="expandedChunkRadius">围绕活跃刚体 AABB 膨胀的 chunk 数。</param>
    public StaticTerrainColliders(B2WorldId worldId, int expandedChunkRadius = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expandedChunkRadius);
        _worldId = worldId;
        _expandedChunkRadius = expandedChunkRadius;
    }

    /// <summary>
    /// 当前驻留的地形 collider chunk 数。
    /// </summary>
    public int ColliderChunkCount => _colliders.Count;

    /// <summary>
    /// 最近一次 Update 新建或重建的 chunk collider 数。
    /// </summary>
    public int LastRebuiltChunkCount { get; private set; }

    /// <summary>
    /// 最近一次 Update 销毁的 chunk collider 数。
    /// </summary>
    public int LastDestroyedChunkCount { get; private set; }

    /// <summary>
    /// 更新活跃刚体邻近区域内的静态地形 chain，范围外 collider 立即销毁。
    /// </summary>
    /// <param name="chunks">驻留 chunk 源。</param>
    /// <param name="physicsWorld">刚体世界。</param>
    public void Update(IChunkSource chunks, PhysicsWorld physicsWorld)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(physicsWorld);

        LastRebuiltChunkCount = 0;
        LastDestroyedChunkCount = 0;

        HashSet<ChunkCoord> wanted = BuildWantedChunkSet(physicsWorld);
        DestroyOutOfRange(wanted);
        foreach (ChunkCoord coord in wanted)
        {
            if (!chunks.TryGetChunk(coord, out Chunk chunk))
            {
                continue;
            }

            EnsureCollider(chunk);
        }
    }

    /// <summary>
    /// 销毁全部静态地形 collider。
    /// </summary>
    public void Clear()
    {
        foreach (TerrainCollider collider in _colliders.Values)
        {
            DestroyCollider(collider);
        }

        LastDestroyedChunkCount += _colliders.Count;
        _colliders.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    private HashSet<ChunkCoord> BuildWantedChunkSet(PhysicsWorld physicsWorld)
    {
        HashSet<ChunkCoord> wanted = [];
        for (int i = 0; i < physicsWorld.BodySlotCount; i++)
        {
            if (!physicsWorld.TryGetBody(i, out PixelRigidBody? body) ||
                Box2D.b2Body_IsAwake(body.BodyId) == 0)
            {
                continue;
            }

            RectI bounds = ComputeWorldBounds(body.Mask, body.PreviousTransform);
            int minCx = CellAddressing.ChunkOf(bounds.MinX) - _expandedChunkRadius;
            int minCy = CellAddressing.ChunkOf(bounds.MinY) - _expandedChunkRadius;
            int maxCx = CellAddressing.ChunkOf(bounds.MaxX - 1) + _expandedChunkRadius;
            int maxCy = CellAddressing.ChunkOf(bounds.MaxY - 1) + _expandedChunkRadius;
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    _ = wanted.Add(new ChunkCoord(cx, cy));
                }
            }
        }

        return wanted;
    }

    private void DestroyOutOfRange(HashSet<ChunkCoord> wanted)
    {
        List<ChunkCoord>? remove = null;
        foreach (ChunkCoord coord in _colliders.Keys)
        {
            if (wanted.Contains(coord))
            {
                continue;
            }

            remove ??= [];
            remove.Add(coord);
        }

        if (remove is null)
        {
            return;
        }

        for (int i = 0; i < remove.Count; i++)
        {
            TerrainCollider collider = _colliders[remove[i]];
            DestroyCollider(collider);
            _ = _colliders.Remove(remove[i]);
            LastDestroyedChunkCount++;
        }
    }

    private void EnsureCollider(Chunk chunk)
    {
        ulong hash = ComputeTerrainHash(chunk);
        if (hash == 0)
        {
            if (_colliders.Remove(chunk.Coord, out TerrainCollider? existing))
            {
                DestroyCollider(existing);
                LastDestroyedChunkCount++;
            }

            return;
        }

        if (_colliders.TryGetValue(chunk.Coord, out TerrainCollider? collider) && collider.ContentHash == hash)
        {
            return;
        }

        if (collider is not null)
        {
            DestroyCollider(collider);
            LastDestroyedChunkCount++;
        }

        TerrainCollider rebuilt = BuildCollider(chunk, hash);
        if (rebuilt.ChainIds.Length == 0)
        {
            DestroyCollider(rebuilt);
            _ = _colliders.Remove(chunk.Coord);
            return;
        }

        _colliders[chunk.Coord] = rebuilt;
        LastRebuiltChunkCount++;
    }

    private TerrainCollider BuildCollider(Chunk chunk, ulong hash)
    {
        byte[] mask = ArrayPool<byte>.Shared.Rent(EngineConstants.ChunkArea);
        try
        {
            BuildTerrainMask(chunk, mask.AsSpan(0, EngineConstants.ChunkArea));
            B2BodyDef bodyDef = Box2D.b2DefaultBodyDef();
            bodyDef.Type = B2BodyType.StaticBody;
            B2BodyId bodyId = Box2D.b2CreateBody(_worldId, in bodyDef);
            List<B2ChainId> chains = [];
            CreateContourChains(chunk, bodyId, mask.AsSpan(0, EngineConstants.ChunkArea), chains);
            if (chains.Count == 0)
            {
                CreateTilemapFallbackChains(chunk, bodyId, chains);
            }

            return new TerrainCollider(bodyId, [.. chains], hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mask);
        }
    }

    private static void BuildTerrainMask(Chunk chunk, Span<byte> mask)
    {
        for (int i = 0; i < EngineConstants.ChunkArea; i++)
        {
            mask[i] = chunk.Material[i] != 0 && !CellFlags.Has(chunk.Flags[i], CellFlags.RigidOwned) ? (byte)1 : (byte)0;
        }
    }

    private static void CreateContourChains(Chunk chunk, B2BodyId bodyId, ReadOnlySpan<byte> mask, List<B2ChainId> chains)
    {
        int maxPointCount = MarchingSquares.GetMaximumContourPointCount(EngineConstants.ChunkSize, EngineConstants.ChunkSize);
        Vector2[] points = ArrayPool<Vector2>.Shared.Rent(maxPointCount);
        Vector2[] simplified = ArrayPool<Vector2>.Shared.Rent(maxPointCount);
        ContourRange[] ranges = ArrayPool<ContourRange>.Shared.Rent(maxPointCount / 4);
        try
        {
            int rangeCount = MarchingSquares.TraceContours(mask, EngineConstants.ChunkSize, EngineConstants.ChunkSize, points, ranges);
            int baseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
            int baseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
            for (int i = 0; i < rangeCount; i++)
            {
                ContourRange range = ranges[i];
                int count = DouglasPeucker.Simplify(points.AsSpan(range.Start, range.Count), simplified, epsilon: 0.5f, closed: true);
                int uniqueCount = count > 1 && simplified[0] == simplified[count - 1] ? count - 1 : count;
                if (uniqueCount < 4)
                {
                    continue;
                }

                B2ChainId chainId = CreateChain(bodyId, simplified.AsSpan(0, uniqueCount), baseX, baseY);
                chains.Add(chainId);
            }
        }
        finally
        {
            ArrayPool<Vector2>.Shared.Return(points);
            ArrayPool<Vector2>.Shared.Return(simplified);
            ArrayPool<ContourRange>.Shared.Return(ranges);
        }
    }

    private static void CreateTilemapFallbackChains(Chunk chunk, B2BodyId bodyId, List<B2ChainId> chains)
    {
        RectI[] rects = ArrayPool<RectI>.Shared.Rent(EngineConstants.ChunkArea);
        Vector2[] points = ArrayPool<Vector2>.Shared.Rent(4);
        try
        {
            int count = TilemapCollider.BuildRowRunRects(chunk, rects);
            for (int i = 0; i < count; i++)
            {
                RectI rect = rects[i];
                points[0] = new Vector2(rect.MinX, rect.MinY);
                points[1] = new Vector2(rect.MaxX, rect.MinY);
                points[2] = new Vector2(rect.MaxX, rect.MaxY);
                points[3] = new Vector2(rect.MinX, rect.MaxY);
                chains.Add(CreateChain(bodyId, points.AsSpan(0, 4), worldBaseX: 0, worldBaseY: 0));
            }
        }
        finally
        {
            ArrayPool<RectI>.Shared.Return(rects);
            ArrayPool<Vector2>.Shared.Return(points);
        }
    }

    private static B2ChainId CreateChain(B2BodyId bodyId, ReadOnlySpan<Vector2> localOrWorldPoints, int worldBaseX, int worldBaseY)
    {
        B2Vec2[] rented = ArrayPool<B2Vec2>.Shared.Rent(localOrWorldPoints.Length);
        try
        {
            for (int i = 0; i < localOrWorldPoints.Length; i++)
            {
                rented[i] = new B2Vec2
                {
                    X = PhysicsScale.PixelToPhysics(worldBaseX + localOrWorldPoints[i].X),
                    Y = PhysicsScale.PixelToPhysics(worldBaseY + localOrWorldPoints[i].Y),
                };
            }

            fixed (B2Vec2* pointPtr = rented)
            {
                B2ChainDef chainDef = Box2D.b2DefaultChainDef();
                chainDef.Points = pointPtr;
                chainDef.Count = localOrWorldPoints.Length;
                chainDef.IsLoop = 1;
                return Box2D.b2CreateChain(bodyId, in chainDef);
            }
        }
        finally
        {
            ArrayPool<B2Vec2>.Shared.Return(rented);
        }
    }

    private static void DestroyCollider(TerrainCollider collider)
    {
        for (int i = 0; i < collider.ChainIds.Length; i++)
        {
            Box2D.b2DestroyChain(collider.ChainIds[i]);
        }

        Box2D.b2DestroyBody(collider.BodyId);
    }

    private static ulong ComputeTerrainHash(Chunk chunk)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        bool anySolid = false;
        for (int i = 0; i < EngineConstants.ChunkArea; i++)
        {
            ushort material = chunk.Material[i];
            byte flags = chunk.Flags[i];
            bool solid = material != 0 && !CellFlags.Has(flags, CellFlags.RigidOwned);
            anySolid |= solid;
            hash ^= solid ? material : 0UL;
            hash *= prime;
        }

        return anySolid ? hash : 0;
    }

    private static RectI ComputeWorldBounds(BodyLocalMask mask, in Transform2D transform)
    {
        Vector2 origin = mask.LocalOrigin;
        Vector2 p0 = transform.TransformPoint(new Vector2(0f, 0f) - origin);
        Vector2 p1 = transform.TransformPoint(new Vector2(mask.Width, 0f) - origin);
        Vector2 p2 = transform.TransformPoint(new Vector2(mask.Width, mask.Height) - origin);
        Vector2 p3 = transform.TransformPoint(new Vector2(0f, mask.Height) - origin);

        float minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
        float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
        float maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
        float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

        return RectI.FromBounds(
            (int)MathF.Floor(minX),
            (int)MathF.Floor(minY),
            (int)MathF.Ceiling(maxX),
            (int)MathF.Ceiling(maxY));
    }

    private sealed class TerrainCollider(B2BodyId bodyId, B2ChainId[] chainIds, ulong contentHash)
    {
        public B2BodyId BodyId { get; } = bodyId;

        public B2ChainId[] ChainIds { get; } = chainIds;

        public ulong ContentHash { get; } = contentHash;
    }
}
