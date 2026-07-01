using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PixelEngine.Physics.Geometry;

/// <summary>
/// Douglas-Peucker 折线简化器，用于像素边界轮廓进入凸分解前的顶点降采样。
/// </summary>
public static class DouglasPeucker
{
    private const int StackallocSegmentCapacity = 128;

    /// <summary>
    /// 使用 Douglas-Peucker 算法简化开放折线或闭合轮廓，并返回新的点列表。
    /// </summary>
    /// <param name="points">输入点序列。闭合轮廓可带首尾重复点，也可不带。</param>
    /// <param name="epsilon">最大允许偏差，单位与输入点一致；0 表示只移除严格共线冗余点。</param>
    /// <param name="closed">为 <see langword="true"/> 时按闭合轮廓处理。</param>
    /// <returns>简化后的点序列；若输入闭合且首尾重复，输出也保持首尾重复。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="epsilon"/> 小于 0、为 NaN 或无穷大。</exception>
    public static List<Vector2> Simplify(ReadOnlySpan<Vector2> points, float epsilon, bool closed = false)
    {
        ValidateEpsilon(epsilon);

        int maxOutputCount = GetMaximumOutputCount(points, closed);
        List<Vector2> result = new(maxOutputCount);

        if (maxOutputCount == 0)
        {
            return result;
        }

        Vector2[] output = ArrayPool<Vector2>.Shared.Rent(maxOutputCount);
        try
        {
            int written = Simplify(points, output.AsSpan(0, maxOutputCount), epsilon, closed);
            for (int i = 0; i < written; i++)
            {
                result.Add(output[i]);
            }

            return result;
        }
        finally
        {
            ArrayPool<Vector2>.Shared.Return(output);
        }
    }

    /// <summary>
    /// 使用 Douglas-Peucker 算法简化开放折线或闭合轮廓，并写入调用方提供的缓冲区。
    /// </summary>
    /// <param name="points">输入点序列。闭合轮廓可带首尾重复点，也可不带。</param>
    /// <param name="destination">输出缓冲区。长度至少应为 <see cref="GetMaximumOutputCount"/> 的返回值。</param>
    /// <param name="epsilon">最大允许偏差，单位与输入点一致；0 表示只移除严格共线冗余点。</param>
    /// <param name="closed">为 <see langword="true"/> 时按闭合轮廓处理。</param>
    /// <returns>写入 <paramref name="destination"/> 的点数。</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> 容量不足。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="epsilon"/> 小于 0、为 NaN 或无穷大。</exception>
    public static int Simplify(ReadOnlySpan<Vector2> points, Span<Vector2> destination, float epsilon, bool closed = false)
    {
        ValidateEpsilon(epsilon);

        int maxOutputCount = GetMaximumOutputCount(points, closed);
        return destination.Length < maxOutputCount
            ? throw new ArgumentException("输出缓冲区容量不足。", nameof(destination))
            : maxOutputCount == 0
            ? 0
            : !closed
                ? SimplifyOpen(points, destination, epsilon)
                : SimplifyClosed(points, destination, epsilon);
    }

    /// <summary>
    /// 计算简化结果的最大可能输出点数，用于调用方预分配输出缓冲区。
    /// </summary>
    /// <param name="points">输入点序列。</param>
    /// <param name="closed">是否按闭合轮廓处理。</param>
    /// <returns>输出点数上界。</returns>
    public static int GetMaximumOutputCount(ReadOnlySpan<Vector2> points, bool closed = false)
    {
        if (points.IsEmpty)
        {
            return 0;
        }

        if (!closed)
        {
            return points.Length;
        }

        bool repeatsFirst = points.Length > 1 && points[0] == points[^1];
        int uniqueCount = repeatsFirst ? points.Length - 1 : points.Length;
        return uniqueCount <= 0 ? 0 : uniqueCount + (repeatsFirst ? 1 : 0);
    }

    private static int SimplifyOpen(ReadOnlySpan<Vector2> points, Span<Vector2> destination, float epsilon)
    {
        if (points.Length <= 2)
        {
            points.CopyTo(destination);
            return points.Length;
        }

        bool[]? rentedKeep = null;
        Segment[]? rentedStack = null;
        Span<bool> keep = points.Length <= 256
            ? stackalloc bool[points.Length]
            : (rentedKeep = ArrayPool<bool>.Shared.Rent(points.Length)).AsSpan(0, points.Length);
        Span<Segment> stack = points.Length <= StackallocSegmentCapacity
            ? stackalloc Segment[StackallocSegmentCapacity]
            : (rentedStack = ArrayPool<Segment>.Shared.Rent(points.Length)).AsSpan(0, points.Length);

        try
        {
            keep.Clear();
            MarkOpen(points, 0, points.Length - 1, epsilon * epsilon, keep, stack);
            return WriteKept(points, keep, destination);
        }
        finally
        {
            if (rentedKeep is not null)
            {
                ArrayPool<bool>.Shared.Return(rentedKeep);
            }

            if (rentedStack is not null)
            {
                ArrayPool<Segment>.Shared.Return(rentedStack);
            }
        }
    }

    private static int SimplifyClosed(ReadOnlySpan<Vector2> points, Span<Vector2> destination, float epsilon)
    {
        bool repeatsFirst = points.Length > 1 && points[0] == points[^1];
        int uniqueCount = repeatsFirst ? points.Length - 1 : points.Length;
        if (uniqueCount <= 0)
        {
            return 0;
        }

        if (uniqueCount <= 3)
        {
            points[..uniqueCount].CopyTo(destination);
            if (repeatsFirst)
            {
                destination[uniqueCount] = points[0];
                return uniqueCount + 1;
            }

            return uniqueCount;
        }

        bool[]? rentedKeep = null;
        Segment[]? rentedStack = null;
        Span<bool> keep = uniqueCount <= 256
            ? stackalloc bool[uniqueCount]
            : (rentedKeep = ArrayPool<bool>.Shared.Rent(uniqueCount)).AsSpan(0, uniqueCount);
        Span<Segment> stack = uniqueCount <= StackallocSegmentCapacity
            ? stackalloc Segment[StackallocSegmentCapacity]
            : (rentedStack = ArrayPool<Segment>.Shared.Rent(uniqueCount)).AsSpan(0, uniqueCount);

        try
        {
            keep.Clear();
            int first = 0;
            int second = FindFarthestFromFirst(points[..uniqueCount]);

            // 闭合轮廓没有天然终点；保留输入首点作 seam，再以远端点将环拆成两条开放弧线处理。
            float epsilonSquared = epsilon * epsilon;
            MarkOpen(points, first, second, epsilonSquared, keep, stack);
            MarkOpen(points, second, first + uniqueCount, epsilonSquared, keep, stack);

            int written = 0;
            for (int index = 0; index < uniqueCount; index++)
            {
                if (keep[index])
                {
                    destination[written++] = points[index];
                }
            }

            if (repeatsFirst)
            {
                destination[written++] = destination[0];
            }

            return written;
        }
        finally
        {
            if (rentedKeep is not null)
            {
                ArrayPool<bool>.Shared.Return(rentedKeep);
            }

            if (rentedStack is not null)
            {
                ArrayPool<Segment>.Shared.Return(rentedStack);
            }
        }
    }

    private static void MarkOpen(
        ReadOnlySpan<Vector2> points,
        int start,
        int end,
        float epsilonSquared,
        Span<bool> keep,
        Span<Segment> stack)
    {
        int top = 0;
        keep[start % keep.Length] = true;
        keep[end % keep.Length] = true;
        stack[top++] = new Segment(start, end);

        while (top > 0)
        {
            Segment segment = stack[--top];
            int bestIndex = -1;
            float bestDistanceSquared = epsilonSquared;

            Vector2 a = points[segment.Start % keep.Length];
            Vector2 b = points[segment.End % keep.Length];

            for (int i = segment.Start + 1; i < segment.End; i++)
            {
                int index = i % keep.Length;
                float distanceSquared = DistanceToSegmentSquared(points[index], a, b);
                if (distanceSquared > bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                continue;
            }

            keep[bestIndex % keep.Length] = true;
            stack[top++] = new Segment(segment.Start, bestIndex);
            stack[top++] = new Segment(bestIndex, segment.End);
        }
    }

    private static int WriteKept(ReadOnlySpan<Vector2> points, ReadOnlySpan<bool> keep, Span<Vector2> destination)
    {
        int written = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
            {
                destination[written++] = points[i];
            }
        }

        return written;
    }

    private static int FindFarthestFromFirst(ReadOnlySpan<Vector2> points)
    {
        int bestIndex = 1;
        Vector2 first = points[0];
        float bestDistanceSquared = Vector2.DistanceSquared(first, points[1]);

        for (int i = 2; i < points.Length; i++)
        {
            float distanceSquared = Vector2.DistanceSquared(first, points[i]);
            if (distanceSquared > bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            return Vector2.DistanceSquared(point, a);
        }

        float t = Vector2.Dot(point - a, ab) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        Vector2 projection = a + (ab * t);
        return Vector2.DistanceSquared(point, projection);
    }

    private static void ValidateEpsilon(float epsilon)
    {
        if (epsilon < 0f || float.IsNaN(epsilon) || float.IsInfinity(epsilon))
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "epsilon 必须是有限且非负的数值。");
        }
    }

    private readonly struct Segment(int start, int end)
    {
        public int Start { get; } = start;

        public int End { get; } = end;
    }
}
