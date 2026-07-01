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

                ushort material = mask.MaterialAt(localX, localY);
                if (material == 0)
                {
                    continue;
                }

                RigidStamp stamp = new(body.BodyKey, localX, localY, material);
                grid.StampRigidOwnedCell(wx, wy, material);
                registry.Register(wx, wy, in stamp);
                stamps.Add(new RigidStampedCell(wx, wy, stamp));
                stamped++;
            }
        }

        return stamped;
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
