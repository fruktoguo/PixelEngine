using System.Buffers;
using System.Numerics;

namespace PixelEngine.Physics.Geometry;

/// <summary>
/// 将简单多边形拆分为 Box2D 可消费的 ≤8 顶点凸片。
/// </summary>
public static class ConvexDecomposer
{
    /// <summary>
    /// 分解简单多边形。
    /// </summary>
    /// <param name="polygon">输入多边形，可闭合可不闭合。</param>
    /// <param name="destination">输出凸片缓冲。</param>
    /// <returns>输出凸片数量。</returns>
    public static int Decompose(ReadOnlySpan<Vector2> polygon, Span<ConvexPolygon> destination)
    {
        int count = NormalizedCount(polygon);
        if (count < 3)
        {
            return 0;
        }

        if (destination.IsEmpty)
        {
            throw new ArgumentException("destination 不能为空。", nameof(destination));
        }

        Vector2[] rentedVertices = ArrayPool<Vector2>.Shared.Rent(count);
        int[] rentedIndices = ArrayPool<int>.Shared.Rent(count);
        try
        {
            Span<Vector2> vertices = rentedVertices.AsSpan(0, count);
            polygon[..count].CopyTo(vertices);
            EnsureCounterClockwise(vertices);

            if (count <= 8 && IsConvex(vertices))
            {
                destination[0] = ConvexPolygon.From(vertices);
                return 1;
            }

            int triangleCount = Triangulate(vertices, rentedIndices.AsSpan(0, count), destination);
            return MergeAdjacentConvexPieces(destination, triangleCount);
        }
        finally
        {
            ArrayPool<Vector2>.Shared.Return(rentedVertices);
            ArrayPool<int>.Shared.Return(rentedIndices);
        }
    }

    /// <summary>
    /// 判断凸片是否为凸多边形。
    /// </summary>
    /// <param name="polygon">凸片。</param>
    /// <returns>若凸则为 true。</returns>
    public static bool IsConvex(ReadOnlySpan<Vector2> polygon)
    {
        int count = NormalizedCount(polygon);
        if (count < 3)
        {
            return false;
        }

        float sign = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            Vector2 c = polygon[(i + 2) % count];
            float cross = Cross(b - a, c - b);
            if (Math.Abs(cross) <= float.Epsilon)
            {
                continue;
            }

            if (sign == 0f)
            {
                sign = MathF.CopySign(1f, cross);
                continue;
            }

            if (MathF.CopySign(1f, cross) != sign)
            {
                return false;
            }
        }

        return true;
    }

    private static int Triangulate(ReadOnlySpan<Vector2> vertices, Span<int> indices, Span<ConvexPolygon> destination)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            indices[i] = i;
        }

        int indexCount = vertices.Length;
        int written = 0;
        int guard = vertices.Length * vertices.Length;
        Span<Vector2> triangle = stackalloc Vector2[3];

        while (indexCount > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < indexCount; i++)
            {
                int prevSlot = (i + indexCount - 1) % indexCount;
                int nextSlot = (i + 1) % indexCount;
                int prev = indices[prevSlot];
                int current = indices[i];
                int next = indices[nextSlot];

                if (!IsEar(vertices, indices[..indexCount], prev, current, next))
                {
                    continue;
                }

                if (written >= destination.Length)
                {
                    throw new ArgumentException("destination 缓冲不足。", nameof(destination));
                }

                triangle[0] = vertices[prev];
                triangle[1] = vertices[current];
                triangle[2] = vertices[next];
                destination[written++] = ConvexPolygon.From(triangle);
                indices[(i + 1)..indexCount].CopyTo(indices[i..(indexCount - 1)]);
                indexCount--;
                clipped = true;
                break;
            }

            if (!clipped)
            {
                break;
            }
        }

        if (indexCount == 3)
        {
            if (written >= destination.Length)
            {
                throw new ArgumentException("destination 缓冲不足。", nameof(destination));
            }

            triangle[0] = vertices[indices[0]];
            triangle[1] = vertices[indices[1]];
            triangle[2] = vertices[indices[2]];
            destination[written++] = ConvexPolygon.From(triangle);
        }

        return written;
    }

    private static int MergeAdjacentConvexPieces(Span<ConvexPolygon> pieces, int count)
    {
        Span<Vector2> merged = stackalloc Vector2[8];
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < count && !changed; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    int mergedCount = TryMerge(pieces[i], pieces[j], merged);
                    if (mergedCount == 0)
                    {
                        continue;
                    }

                    pieces[i] = ConvexPolygon.From(merged[..mergedCount]);
                    pieces[j] = pieces[count - 1];
                    count--;
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        return count;
    }

    private static int TryMerge(ConvexPolygon left, ConvexPolygon right, Span<Vector2> destination)
    {
        Span<Vector2> unique = stackalloc Vector2[16];
        int uniqueCount = CopyUnique(left, unique, 0);
        uniqueCount = CopyUnique(right, unique, uniqueCount);
        if (uniqueCount > 8 || SharedVertexCount(left, right) != 2)
        {
            return 0;
        }

        int hullCount = BuildConvexHull(unique[..uniqueCount], destination);
        if (hullCount is < 3 or > 8)
        {
            return 0;
        }

        float sourceArea = Math.Abs(Area(left)) + Math.Abs(Area(right));
        float hullArea = Math.Abs(SignedArea(destination[..hullCount]));
        float tolerance = MathF.Max(1e-4f, sourceArea * 1e-4f);
        return Math.Abs(hullArea - sourceArea) <= tolerance ? hullCount : 0;
    }

    private static int CopyUnique(ConvexPolygon polygon, Span<Vector2> destination, int count)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 vertex = polygon[i];
            if (Contains(destination[..count], vertex))
            {
                continue;
            }

            destination[count++] = vertex;
        }

        return count;
    }

    private static int SharedVertexCount(ConvexPolygon left, ConvexPolygon right)
    {
        int shared = 0;
        for (int i = 0; i < left.Count; i++)
        {
            for (int j = 0; j < right.Count; j++)
            {
                if (left[i] == right[j])
                {
                    shared++;
                }
            }
        }

        return shared;
    }

    private static int BuildConvexHull(ReadOnlySpan<Vector2> points, Span<Vector2> destination)
    {
        Vector2[] rented = ArrayPool<Vector2>.Shared.Rent(points.Length);
        try
        {
            Span<Vector2> sorted = rented.AsSpan(0, points.Length);
            points.CopyTo(sorted);
            sorted.Sort(static (a, b) =>
            {
                int x = a.X.CompareTo(b.X);
                return x != 0 ? x : a.Y.CompareTo(b.Y);
            });

            int count = 0;
            for (int i = 0; i < sorted.Length; i++)
            {
                while (count >= 2 && Cross(destination[count - 1] - destination[count - 2], sorted[i] - destination[count - 1]) <= 0f)
                {
                    count--;
                }

                destination[count++] = sorted[i];
            }

            int lowerCount = count;
            for (int i = sorted.Length - 2; i >= 0; i--)
            {
                while (count > lowerCount && Cross(destination[count - 1] - destination[count - 2], sorted[i] - destination[count - 1]) <= 0f)
                {
                    count--;
                }

                destination[count++] = sorted[i];
            }

            return count > 1 ? count - 1 : count;
        }
        finally
        {
            ArrayPool<Vector2>.Shared.Return(rented);
        }
    }

    private static bool Contains(ReadOnlySpan<Vector2> points, Vector2 value)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static float Area(ConvexPolygon polygon)
    {
        Span<Vector2> vertices = stackalloc Vector2[8];
        int count = polygon.CopyTo(vertices);
        return SignedArea(vertices[..count]);
    }

    private static bool IsEar(ReadOnlySpan<Vector2> vertices, ReadOnlySpan<int> indices, int prev, int current, int next)
    {
        Vector2 a = vertices[prev];
        Vector2 b = vertices[current];
        Vector2 c = vertices[next];
        if (Cross(b - a, c - b) <= 0f)
        {
            return false;
        }

        for (int i = 0; i < indices.Length; i++)
        {
            int candidate = indices[i];
            if (candidate == prev || candidate == current || candidate == next)
            {
                continue;
            }

            if (PointInTriangle(vertices[candidate], a, b, c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float ab = Cross(b - a, p - a);
        float bc = Cross(c - b, p - b);
        float ca = Cross(a - c, p - c);
        return ab >= 0f && bc >= 0f && ca >= 0f;
    }

    private static void EnsureCounterClockwise(Span<Vector2> polygon)
    {
        if (SignedArea(polygon) < 0f)
        {
            polygon.Reverse();
        }
    }

    private static int NormalizedCount(ReadOnlySpan<Vector2> polygon)
    {
        return polygon.Length > 1 && polygon[0] == polygon[^1]
            ? polygon.Length - 1
            : polygon.Length;
    }

    private static float SignedArea(ReadOnlySpan<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return (a.X * b.Y) - (a.Y * b.X);
    }
}
