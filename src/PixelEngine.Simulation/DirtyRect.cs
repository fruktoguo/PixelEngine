using System.Runtime.CompilerServices;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// chunk 本地坐标系下的闭区间 dirty rectangle。
/// </summary>
/// <remarks>
/// 创建 dirty rectangle。
/// </remarks>
public readonly struct DirtyRect(int minX, int minY, int maxX, int maxY) : IEquatable<DirtyRect>
{
    /// <summary>
    /// 空 dirty rectangle。
    /// </summary>
    public static DirtyRect Empty { get; } = new(0, 0, -1, -1);

    /// <summary>
    /// 覆盖整个 chunk 的 dirty rectangle。
    /// </summary>
    public static DirtyRect Full { get; } = new(0, 0, EngineConstants.ChunkSize - 1, EngineConstants.ChunkSize - 1);

    /// <summary>
    /// 最小本地 X 坐标。
    /// </summary>
    public int MinX { get; } = minX;

    /// <summary>
    /// 最小本地 Y 坐标。
    /// </summary>
    public int MinY { get; } = minY;

    /// <summary>
    /// 最大本地 X 坐标。
    /// </summary>
    public int MaxX { get; } = maxX;

    /// <summary>
    /// 最大本地 Y 坐标。
    /// </summary>
    public int MaxY { get; } = maxY;

    /// <summary>
    /// 是否为空 rectangle。
    /// </summary>
    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    /// <summary>
    /// 将一个本地 cell 坐标合并进 dirty rectangle，并按 padding 扩张后钳制到 chunk 内。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DirtyRect Union(int lx, int ly, int padding)
    {
        int minX = ClampLocal(lx - padding);
        int minY = ClampLocal(ly - padding);
        int maxX = ClampLocal(lx + padding);
        int maxY = ClampLocal(ly + padding);

        return IsEmpty
            ? new DirtyRect(minX, minY, maxX, maxY)
            : new DirtyRect(
            Math.Min(MinX, minX),
            Math.Min(MinY, minY),
            Math.Max(MaxX, maxX),
            Math.Max(MaxY, maxY));
    }

    /// <summary>
    /// 合并另一个 dirty rectangle。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DirtyRect Union(DirtyRect other)
    {
        return other.IsEmpty
            ? this
            : IsEmpty
            ? other
            : new DirtyRect(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY));
    }

    /// <inheritdoc />
    public bool Equals(DirtyRect other)
    {
        return MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is DirtyRect other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(MinX, MinY, MaxX, MaxY);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return IsEmpty ? "Empty" : $"[{MinX},{MinY}]..[{MaxX},{MaxY}]";
    }

    /// <summary>
    /// 判断两个 dirty rectangle 是否相等。
    /// </summary>
    public static bool operator ==(DirtyRect left, DirtyRect right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个 dirty rectangle 是否不相等。
    /// </summary>
    public static bool operator !=(DirtyRect left, DirtyRect right)
    {
        return !left.Equals(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampLocal(int value)
    {
        return Math.Clamp(value, 0, EngineConstants.ChunkSize - 1);
    }
}
