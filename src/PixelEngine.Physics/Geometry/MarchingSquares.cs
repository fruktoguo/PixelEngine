using System.Buffers;
using System.Numerics;

namespace PixelEngine.Physics.Geometry;

/// <summary>
/// 从二值像素 mask 生成像素边界闭合折线。
/// </summary>
public static class MarchingSquares
{
    /// <summary>
    /// 供刚体破坏 worker 独占的轮廓追踪 scratch；同一实例不可跨并发调用共享。
    /// </summary>
    internal sealed class TraceScratch
    {
        private const int InitialEdgeCapacity = 2048;

        internal readonly List<BoundaryEdge> _edges = new(InitialEdgeCapacity);
        internal readonly Dictionary<EdgeKey, EdgeIndexSet> _edgesByStart = new(InitialEdgeCapacity);
        internal bool[] _used = new bool[InitialEdgeCapacity];
        internal Vector2[] _contour = [];
        internal Vector2[] _simplified = [];
        internal ContourRange[] _ranges = [];

        internal void EnsureGeometryCapacity(int width, int height)
        {
            int maximumContourPointCount = GetMaximumContourPointCount(width, height);
            if (_contour.Length < maximumContourPointCount)
            {
                _contour = new Vector2[maximumContourPointCount];
                _simplified = new Vector2[maximumContourPointCount];
            }

            int maximumContourRangeCount = GetMaximumContourRangeCount(width, height);
            if (_ranges.Length < maximumContourRangeCount)
            {
                _ranges = new ContourRange[maximumContourRangeCount];
            }
        }

        internal void EnsureUsedCapacity(int required)
        {
            if (required <= _used.Length)
            {
                return;
            }

            Array.Resize(ref _used, Math.Max(required, _used.Length * 2));
        }

        internal void PrepareContainers()
        {
            _edges.Clear();
            _edgesByStart.Clear();
        }

        internal void PrepareUsed(int edgeCount)
        {
            EnsureUsedCapacity(edgeCount);
            _used.AsSpan(0, edgeCount).Clear();
        }
    }

    /// <summary>
    /// 使用调用方独占 scratch 追踪固体区域的外边界，供物理破坏 worker 复用容器容量。
    /// </summary>
    internal static int TraceOuterContour(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<Vector2> destination,
        TraceScratch scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        scratch.EnsureGeometryCapacity(width, height);
        int count = TraceContoursWithScratch(solidMask, width, height, destination, scratch._ranges, scratch);
        if (count == 0)
        {
            return 0;
        }

        ContourRange outer = scratch._ranges[0];
        if (outer.Start != 0)
        {
            destination.Slice(outer.Start, outer.Count).CopyTo(destination);
        }

        return outer.Count;
    }
    /// <summary>
    /// 追踪固体区域的外边界，输出 CCW 闭合折线。
    /// </summary>
    /// <param name="solidMask">二值固体 mask，非 0 表示固体。</param>
    /// <param name="width">mask 宽度。</param>
    /// <param name="height">mask 高度。</param>
    /// <param name="destination">输出点缓冲。</param>
    /// <returns>写入点数；包含重复的闭合终点。</returns>
    public static int TraceOuterContour(ReadOnlySpan<byte> solidMask, int width, int height, Span<Vector2> destination)
    {
        ContourRange[] ranges = ArrayPool<ContourRange>.Shared.Rent(GetMaximumContourRangeCount(width, height));
        try
        {
            int count = TraceContours(solidMask, width, height, destination, ranges);
            if (count == 0)
            {
                return 0;
            }

            ContourRange outer = ranges[0];
            if (outer.Start != 0)
            {
                destination.Slice(outer.Start, outer.Count).CopyTo(destination);
            }

            return outer.Count;
        }
        finally
        {
            ArrayPool<ContourRange>.Shared.Return(ranges);
        }
    }

    /// <summary>
    /// 追踪固体区域全部边界，输出 CCW 外轮廓和零个或多个 CW 内孔。
    /// </summary>
    /// <param name="solidMask">二值固体 mask，非 0 表示固体。</param>
    /// <param name="width">mask 宽度。</param>
    /// <param name="height">mask 高度。</param>
    /// <param name="destination">输出点缓冲。</param>
    /// <param name="ranges">输出 contour 范围缓冲。</param>
    /// <returns>写入的 contour 数。</returns>
    public static int TraceContours(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<Vector2> destination,
        Span<ContourRange> ranges)
    {
        ValidateArguments(solidMask, width, height, destination);
        if (ranges.IsEmpty)
        {
            throw new ArgumentException("ranges 不能为空。", nameof(ranges));
        }

        // 从固体 mask 提取边界有向边，再串联为闭合 contour。
        List<BoundaryEdge> edges = BuildBoundaryEdges(solidMask, width, height);
        if (edges.Count == 0)
        {
            return 0;
        }

        Dictionary<EdgeKey, List<int>> edgesByStart = BuildStartIndex(edges);
        bool[] used = new bool[edges.Count];
        int written = 0;
        int rangeCount = 0;
        int remainingEdges = edges.Count;

        while (remainingEdges > 0)
        {
            if (rangeCount >= ranges.Length)
            {
                throw new ArgumentException("ranges 缓冲不足。", nameof(ranges));
            }

            int startEdgeIndex = FindStartEdge(edges, used);
            BoundaryEdge startEdge = edges[startEdgeIndex];
            EdgeKey start = startEdge.Start;
            EdgeKey current = start;
            int currentEdgeIndex = startEdgeIndex;
            int startOffset = written;

            while (true)
            {
                if (written >= destination.Length)
                {
                    throw new ArgumentException("destination 缓冲不足。", nameof(destination));
                }

                destination[written++] = current.ToVector2();
                if (used[currentEdgeIndex])
                {
                    throw new InvalidOperationException("边界链重复访问。");
                }

                used[currentEdgeIndex] = true;
                remainingEdges--;
                BoundaryEdge currentEdge = edges[currentEdgeIndex];
                EdgeKey next = currentEdge.End;
                current = next;
                if (current.Equals(start))
                {
                    if (written >= destination.Length)
                    {
                        throw new ArgumentException("destination 缓冲不足。", nameof(destination));
                    }

                    destination[written++] = start.ToVector2();
                    break;
                }

                currentEdgeIndex = FindNextEdge(edges, edgesByStart, used, currentEdge);
            }

            // 有符号面积区分外轮廓（CCW）与内孔（CW）。
            Span<Vector2> contour = destination[startOffset..written];
            bool isHole = SignedArea(contour) > 0f;
            ranges[rangeCount] = new ContourRange(startOffset, contour.Length, isHole);
            rangeCount++;
        }

        for (int i = 0; i < rangeCount; i++)
        {
            ContourRange range = ranges[i];
            NormalizeWinding(destination.Slice(range.Start, range.Count), wantCounterClockwise: !range.IsHole);
        }

        int firstOuterIndex = FindFirstOuterRange(ranges, rangeCount);
        if (firstOuterIndex > 0)
        {
            (ranges[0], ranges[firstOuterIndex]) = (ranges[firstOuterIndex], ranges[0]);
        }

        return rangeCount;
    }

    private static int TraceContoursWithScratch(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<Vector2> destination,
        Span<ContourRange> ranges,
        TraceScratch scratch)
    {
        ValidateArguments(solidMask, width, height, destination);
        if (ranges.IsEmpty)
        {
            throw new ArgumentException("ranges 不能为空。", nameof(ranges));
        }

        scratch.PrepareContainers();
        BuildBoundaryEdges(solidMask, width, height, scratch._edges);
        if (scratch._edges.Count == 0)
        {
            return 0;
        }

        BuildStartIndex(scratch._edges, scratch._edgesByStart);
        scratch.PrepareUsed(scratch._edges.Count);
        int written = 0;
        int rangeCount = 0;
        int remainingEdges = scratch._edges.Count;

        while (remainingEdges > 0)
        {
            if (rangeCount >= ranges.Length)
            {
                throw new ArgumentException("ranges 缓冲不足。", nameof(ranges));
            }

            int startEdgeIndex = FindStartEdge(scratch._edges, scratch._used);
            BoundaryEdge startEdge = scratch._edges[startEdgeIndex];
            EdgeKey start = startEdge.Start;
            EdgeKey current = start;
            int currentEdgeIndex = startEdgeIndex;
            int startOffset = written;

            while (true)
            {
                if (written >= destination.Length)
                {
                    throw new ArgumentException("destination 缓冲不足。", nameof(destination));
                }

                destination[written++] = current.ToVector2();
                if (scratch._used[currentEdgeIndex])
                {
                    throw new InvalidOperationException("边界链重复访问。");
                }

                scratch._used[currentEdgeIndex] = true;
                remainingEdges--;
                BoundaryEdge currentEdge = scratch._edges[currentEdgeIndex];
                EdgeKey next = currentEdge.End;
                current = next;
                if (current.Equals(start))
                {
                    if (written >= destination.Length)
                    {
                        throw new ArgumentException("destination 缓冲不足。", nameof(destination));
                    }

                    destination[written++] = start.ToVector2();
                    break;
                }

                currentEdgeIndex = FindNextEdge(scratch._edges, scratch._edgesByStart, scratch._used, currentEdge);
            }

            Span<Vector2> contour = destination[startOffset..written];
            bool isHole = SignedArea(contour) > 0f;
            ranges[rangeCount++] = new ContourRange(startOffset, contour.Length, isHole);
        }

        for (int i = 0; i < rangeCount; i++)
        {
            ContourRange range = ranges[i];
            NormalizeWinding(destination.Slice(range.Start, range.Count), wantCounterClockwise: !range.IsHole);
        }

        int firstOuterIndex = FindFirstOuterRange(ranges, rangeCount);
        if (firstOuterIndex > 0)
        {
            (ranges[0], ranges[firstOuterIndex]) = (ranges[firstOuterIndex], ranges[0]);
        }

        return rangeCount;
    }

    private static int FindFirstOuterRange(Span<ContourRange> ranges, int rangeCount)
    {
        for (int i = 0; i < rangeCount; i++)
        {
            if (!ranges[i].IsHole)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 获取最坏情况下的外轮廓点数上界。
    /// </summary>
    /// <param name="width">mask 宽度。</param>
    /// <param name="height">mask 高度。</param>
    /// <returns>点数上界。</returns>
    public static int GetMaximumContourPointCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return checked((width * height * 4) + 1);
    }

    /// <summary>
    /// 获取最坏情况下的轮廓条数上界。
    /// </summary>
    /// <param name="width">mask 宽度。</param>
    /// <param name="height">mask 高度。</param>
    /// <returns>轮廓条数上界。</returns>
    public static int GetMaximumContourRangeCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return checked(width * height);
    }

    private static List<BoundaryEdge> BuildBoundaryEdges(ReadOnlySpan<byte> solidMask, int width, int height)
    {
        List<BoundaryEdge> edges = new(width * height * 2);
        BuildBoundaryEdges(solidMask, width, height, edges);
        return edges;
    }

    private static void BuildBoundaryEdges(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        List<BoundaryEdge> edges)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (solidMask[index] == 0)
                {
                    continue;
                }

                if (y == 0 || solidMask[index - width] == 0)
                {
                    AddEdge(edges, x + 1, y, x, y);
                }

                if (x == 0 || solidMask[index - 1] == 0)
                {
                    AddEdge(edges, x, y, x, y + 1);
                }

                if (y == height - 1 || solidMask[index + width] == 0)
                {
                    AddEdge(edges, x, y + 1, x + 1, y + 1);
                }

                if (x == width - 1 || solidMask[index + 1] == 0)
                {
                    AddEdge(edges, x + 1, y + 1, x + 1, y);
                }
            }
        }
    }

    private static void AddEdge(List<BoundaryEdge> edges, int startX, int startY, int endX, int endY)
    {
        edges.Add(new BoundaryEdge(new EdgeKey(startX, startY), new EdgeKey(endX, endY)));
    }

    private static Dictionary<EdgeKey, List<int>> BuildStartIndex(List<BoundaryEdge> edges)
    {
        Dictionary<EdgeKey, List<int>> edgesByStart = new(edges.Count);
        for (int i = 0; i < edges.Count; i++)
        {
            EdgeKey start = edges[i].Start;
            if (!edgesByStart.TryGetValue(start, out List<int>? indices))
            {
                indices = [];
                edgesByStart.Add(start, indices);
            }

            indices.Add(i);
        }

        return edgesByStart;
    }

    private static void BuildStartIndex(List<BoundaryEdge> edges, Dictionary<EdgeKey, EdgeIndexSet> destination)
    {
        destination.Clear();
        for (int i = 0; i < edges.Count; i++)
        {
            EdgeKey start = edges[i].Start;
            if (!destination.TryGetValue(start, out EdgeIndexSet indices))
            {
                indices = default;
            }

            indices.Add(i);
            destination[start] = indices;
        }
    }

    private static int FindStartEdge(List<BoundaryEdge> edges, bool[] used)
    {
        int bestIndex = -1;
        BoundaryEdge best = default;
        bool hasBest = false;
        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            BoundaryEdge edge = edges[i];
            if (!hasBest ||
                edge.Start.Y < best.Start.Y ||
                (edge.Start.Y == best.Start.Y && edge.Start.X < best.Start.X) ||
                (edge.Start.Equals(best.Start) && DirectionOrder(edge) < DirectionOrder(best)))
            {
                best = edge;
                bestIndex = i;
                hasBest = true;
            }
        }

        return bestIndex >= 0 ? bestIndex : throw new InvalidOperationException("边界边集合为空。");
    }

    private static int FindNextEdge(
        List<BoundaryEdge> edges,
        Dictionary<EdgeKey, List<int>> edgesByStart,
        bool[] used,
        BoundaryEdge current)
    {
        if (!edgesByStart.TryGetValue(current.End, out List<int>? candidates))
        {
            throw new InvalidOperationException("边界链断裂。");
        }

        int bestIndex = -1;
        int bestRank = int.MaxValue;
        int currentDirection = DirectionOrder(current);
        for (int i = 0; i < candidates.Count; i++)
        {
            int edgeIndex = candidates[i];
            if (used[edgeIndex])
            {
                continue;
            }

            int candidateDirection = DirectionOrder(edges[edgeIndex]);
            int rank = TurnRank(currentDirection, candidateDirection);
            if (rank < bestRank)
            {
                bestRank = rank;
                bestIndex = edgeIndex;
            }
        }

        return bestIndex >= 0 ? bestIndex : throw new InvalidOperationException("边界链断裂。");
    }

    private static int FindNextEdge(
        List<BoundaryEdge> edges,
        Dictionary<EdgeKey, EdgeIndexSet> edgesByStart,
        bool[] used,
        BoundaryEdge current)
    {
        if (!edgesByStart.TryGetValue(current.End, out EdgeIndexSet candidates))
        {
            throw new InvalidOperationException("边界链断裂。");
        }

        int bestIndex = -1;
        int bestRank = int.MaxValue;
        int currentDirection = DirectionOrder(current);
        for (int i = 0; i < candidates.Count; i++)
        {
            int edgeIndex = candidates[i];
            if (used[edgeIndex])
            {
                continue;
            }

            int candidateDirection = DirectionOrder(edges[edgeIndex]);
            int rank = TurnRank(currentDirection, candidateDirection);
            if (rank < bestRank)
            {
                bestRank = rank;
                bestIndex = edgeIndex;
            }
        }

        return bestIndex >= 0 ? bestIndex : throw new InvalidOperationException("边界链断裂。");
    }

    private static int TurnRank(int currentDirection, int candidateDirection)
    {
        int delta = (candidateDirection - currentDirection + 4) & 3;
        return delta switch
        {
            3 => 0,
            0 => 1,
            1 => 2,
            _ => 3,
        };
    }

    private static int DirectionOrder(BoundaryEdge edge)
    {
        int dx = edge.End.X - edge.Start.X;
        int dy = edge.End.Y - edge.Start.Y;
        return dx > 0 ? 0 :
            dy > 0 ? 1 :
            dx < 0 ? 2 :
            dy < 0 ? 3 :
            throw new InvalidOperationException("零长度边界边。");
    }

    private static void NormalizeWinding(Span<Vector2> contour, bool wantCounterClockwise)
    {
        float signedArea = SignedArea(contour);
        bool isCounterClockwise = signedArea > 0f;
        if (isCounterClockwise != wantCounterClockwise)
        {
            contour.Reverse();
        }
    }

    private static float SignedArea(ReadOnlySpan<Vector2> contour)
    {
        float area = 0f;
        for (int i = 0; i < contour.Length - 1; i++)
        {
            Vector2 a = contour[i];
            Vector2 b = contour[i + 1];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area * 0.5f;
    }

    private static void ValidateArguments(ReadOnlySpan<byte> solidMask, int width, int height, Span<Vector2> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int area = checked(width * height);
        if (solidMask.Length < area)
        {
            throw new ArgumentException("solidMask 长度不足。", nameof(solidMask));
        }

        if (destination.IsEmpty)
        {
            throw new ArgumentException("destination 不能为空。", nameof(destination));
        }
    }

    internal struct EdgeIndexSet
    {
        private int _first;
        private int _second;
        private int _third;
        private int _fourth;

        public int Count { get; private set; }

        public readonly int this[int index] => index switch
        {
            0 when Count > 0 => _first,
            1 when Count > 1 => _second,
            2 when Count > 2 => _third,
            3 when Count > 3 => _fourth,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public void Add(int edgeIndex)
        {
            switch (Count)
            {
                case 0:
                    _first = edgeIndex;
                    break;
                case 1:
                    _second = edgeIndex;
                    break;
                case 2:
                    _third = edgeIndex;
                    break;
                case 3:
                    _fourth = edgeIndex;
                    break;
                default:
                    throw new InvalidOperationException("同一边界顶点的出边超过 4 条。");
            }

            Count++;
        }
    }

    internal readonly record struct EdgeKey(int X, int Y)
    {
        public Vector2 ToVector2()
        {
            return new Vector2(X, Y);
        }

    }

    internal readonly record struct BoundaryEdge(EdgeKey Start, EdgeKey End);
}
