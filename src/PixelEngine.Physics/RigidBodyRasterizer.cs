using System.Buffers;
using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 负责刚体像素在权威网格中的 erase 与 inverse-sampling stamp。
/// </summary>
public static class RigidBodyRasterizer
{
    /// <summary>
    /// 相位 8b：按上一帧真实 stamp cell 列表擦除刚体像素。
    /// </summary>
    /// <param name="body">刚体包装。</param>
    /// <param name="grid">权威 cell 网格。</param>
    /// <param name="registry">上一帧 world cell 到 body-local 像素的 registry。</param>
    /// <returns>实际清空的 cell 数量。</returns>
    public static int EraseAtCurrentTransform(PixelRigidBody body, CellGrid grid, RigidStampRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(registry);

        int erased = 0;
        List<RigidStampedCell> previous = body.PreviousStamps;
        for (int i = 0; i < previous.Count; i++)
        {
            RigidStampedCell cell = previous[i];
            if (!registry.TryGet(cell.WorldX, cell.WorldY, out RigidStamp stamp) ||
                stamp.BodyKey != body.BodyKey ||
                stamp.LocalX != cell.Stamp.LocalX ||
                stamp.LocalY != cell.Stamp.LocalY)
            {
                continue;
            }

            if (grid.TryClearRigidOwnedCell(cell.WorldX, cell.WorldY))
            {
                erased++;
            }
        }

        previous.Clear();
        return erased;
    }

    /// <summary>
    /// 相位 8e：对 transform 后的 body-local mask 做 inverse sampling，并写回权威网格。
    /// </summary>
    /// <param name="body">刚体包装。</param>
    /// <param name="transform">像素坐标系中的当前 transform。</param>
    /// <param name="grid">权威 cell 网格。</param>
    /// <param name="registry">本帧重建的 world cell 到 body-local 像素 registry。</param>
    /// <returns>写入的刚体 cell 数量。</returns>
    public static int StampInverseSampling(
        PixelRigidBody body,
        in Transform2D transform,
        CellGrid grid,
        RigidStampRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(registry);

        BodyLocalMask mask = body.Mask;
        RectI bounds = ComputeWorldBounds(mask, in transform);
        List<RigidStampedCell> stamps = body.PreviousStamps;
        stamps.Clear();
        int area = mask.Width * mask.Height;
        byte[]? rentedHits = null;
        Span<byte> localHits = area <= 4096
            ? stackalloc byte[area]
            : (rentedHits = ArrayPool<byte>.Shared.Rent(area)).AsSpan(0, area);
        localHits.Clear();

        try
        {
            int stamped = 0;
            for (int wy = bounds.MinY; wy < bounds.MaxY; wy++)
            {
                for (int wx = bounds.MinX; wx < bounds.MaxX; wx++)
                {
                    Vector2 local = transform.InverseTransformPoint(new Vector2(wx + 0.5f, wy + 0.5f)) + mask.LocalOrigin;
                    int localX = (int)MathF.Floor(local.X);
                    int localY = (int)MathF.Floor(local.Y);
                    if (!mask.IsSolid(localX, localY))
                    {
                        continue;
                    }

                    stamped += StampCell(body, grid, registry, stamps, localHits, wx, wy, localX, localY, allowOccupiedBySelf: false);
                }
            }

            stamped += StampMissingLocalPixels(body, in transform, grid, registry, stamps, localHits);
            return stamped;
        }
        finally
        {
            if (rentedHits is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedHits);
            }
        }
    }

    private static int StampMissingLocalPixels(
        PixelRigidBody body,
        in Transform2D transform,
        CellGrid grid,
        RigidStampRegistry registry,
        List<RigidStampedCell> stamps,
        Span<byte> localHits)
    {
        BodyLocalMask mask = body.Mask;
        int stamped = 0;
        Vector2 origin = mask.LocalOrigin;
        for (int localY = 0; localY < mask.Height; localY++)
        {
            for (int localX = 0; localX < mask.Width; localX++)
            {
                int localIndex = (localY * mask.Width) + localX;
                if (localHits[localIndex] != 0 || !mask.IsSolid(localX, localY) || mask.MaterialAt(localX, localY) == 0)
                {
                    continue;
                }

                Vector2 world = transform.TransformPoint(new Vector2(localX + 0.5f, localY + 0.5f) - origin);
                int baseX = (int)MathF.Floor(world.X);
                int baseY = (int)MathF.Floor(world.Y);
                if (TryStampForwardCandidate(body, in transform, grid, registry, stamps, localHits, baseX, baseY, localX, localY))
                {
                    stamped++;
                }
            }
        }

        return stamped;
    }

    private static bool TryStampForwardCandidate(
        PixelRigidBody body,
        in Transform2D transform,
        CellGrid grid,
        RigidStampRegistry registry,
        List<RigidStampedCell> stamps,
        Span<byte> localHits,
        int baseX,
        int baseY,
        int localX,
        int localY)
    {
        for (int radius = 0; radius <= 1; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    int wx = baseX + dx;
                    int wy = baseY + dy;
                    Vector2 sampled = transform.InverseTransformPoint(new Vector2(wx + 0.5f, wy + 0.5f)) + body.Mask.LocalOrigin;
                    if ((int)MathF.Floor(sampled.X) != localX || (int)MathF.Floor(sampled.Y) != localY)
                    {
                        continue;
                    }

                    if (StampCell(body, grid, registry, stamps, localHits, wx, wy, localX, localY, allowOccupiedBySelf: false) != 0)
                    {
                        return true;
                    }
                }
            }
        }

        return StampCell(body, grid, registry, stamps, localHits, baseX, baseY, localX, localY, allowOccupiedBySelf: false) != 0;
    }

    private static int StampCell(
        PixelRigidBody body,
        CellGrid grid,
        RigidStampRegistry registry,
        List<RigidStampedCell> stamps,
        Span<byte> localHits,
        int worldX,
        int worldY,
        int localX,
        int localY,
        bool allowOccupiedBySelf)
    {
        if (!allowOccupiedBySelf &&
            registry.TryGet(worldX, worldY, out RigidStamp existing) &&
            existing.BodyKey == body.BodyKey)
        {
            return 0;
        }

        ushort material = body.Mask.MaterialAt(localX, localY);
        if (material == 0)
        {
            return 0;
        }

        RigidStamp stamp = new(body.BodyKey, localX, localY, material);
        grid.StampRigidOwnedCell(worldX, worldY, material);
        registry.Register(worldX, worldY, in stamp);
        stamps.Add(new RigidStampedCell(worldX, worldY, stamp));
        localHits[(localY * body.Mask.Width) + localX] = 1;
        return 1;
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
            (int)MathF.Floor(minX) - 1,
            (int)MathF.Floor(minY) - 1,
            (int)MathF.Ceiling(maxX) + 1,
            (int)MathF.Ceiling(maxY) + 1);
    }
}
