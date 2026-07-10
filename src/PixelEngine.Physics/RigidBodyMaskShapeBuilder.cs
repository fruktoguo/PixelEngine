using System.Numerics;
using PixelEngine.Physics.Geometry;

namespace PixelEngine.Physics;

/// <summary>
/// 从 body-local 权威 mask 构建 Box2D 可消费的凸片集合。
/// </summary>
public static class RigidBodyMaskShapeBuilder
{
    /// <summary>
    /// 对 mask 外轮廓执行 Marching Squares、Douglas-Peucker 与凸分解，生成每片不超过 8 顶点的凸多边形。
    /// </summary>
    /// <param name="mask">body-local 不可变权威 mask。</param>
    /// <param name="pieces">输出凸片数组；仅前 <paramref name="pieceCount"/> 个有效。</param>
    /// <param name="pieceCount">有效凸片数量。</param>
    /// <returns>成功生成至少一个凸片时返回 true。</returns>
    public static bool TryBuildConvexPieces(
        BodyLocalMask mask,
        out ConvexPolygon[] pieces,
        out int pieceCount)
    {
        ArgumentNullException.ThrowIfNull(mask);
        return TryBuildConvexPieces(mask.SolidBits, mask.Width, mask.Height, mask.LocalOrigin, out pieces, out pieceCount);
    }

    internal static bool TryBuildConvexPieces(
        ReadOnlySpan<byte> solid,
        int width,
        int height,
        Vector2 localOrigin,
        out ConvexPolygon[] pieces,
        out int pieceCount)
    {
        ConvexPolygon[] output = new ConvexPolygon[Math.Max(8, width * height)];
        bool success = TryBuildConvexPieces(solid, width, height, localOrigin, output, out pieceCount);
        pieces = success ? output : [];
        return success;
    }

    internal static bool TryBuildConvexPieces(
        ReadOnlySpan<byte> solid,
        int width,
        int height,
        Vector2 localOrigin,
        Span<ConvexPolygon> output,
        out int pieceCount)
    {
        return TryBuildConvexPieces(
            solid,
            width,
            height,
            localOrigin,
            output,
            new MarchingSquares.TraceScratch(),
            out pieceCount);
    }

    internal static bool TryBuildConvexPieces(
        ReadOnlySpan<byte> solid,
        int width,
        int height,
        Vector2 localOrigin,
        Span<ConvexPolygon> output,
        MarchingSquares.TraceScratch traceScratch,
        out int pieceCount)
    {
        if (output.IsEmpty)
        {
            throw new ArgumentException("输出凸片缓冲不能为空。", nameof(output));
        }

        traceScratch.EnsureGeometryCapacity(width, height);
        Span<Vector2> contour = traceScratch._contour;
        Span<Vector2> simplified = traceScratch._simplified;
        int contourCount = MarchingSquares.TraceOuterContour(solid, width, height, contour, traceScratch);
        if (contourCount < 4)
        {
            pieceCount = 0;
            return false;
        }

        int simplifiedCount = DouglasPeucker.Simplify(contour[..contourCount], simplified, epsilon: 0f, closed: true);
        for (int i = 0; i < simplifiedCount; i++)
        {
            simplified[i] -= localOrigin;
        }

        pieceCount = ConvexDecomposer.Decompose(simplified[..simplifiedCount], output);
        return pieceCount > 0;
    }
}
