namespace PixelEngine.Simulation;

/// <summary>
/// 64x64 chunk 的整数坐标，用作世界驻留 hash-map 的稳定键。
/// </summary>
/// <remarks>
/// 创建 chunk 坐标。
/// </remarks>
public readonly struct ChunkCoord(int x, int y) : IEquatable<ChunkCoord>
{

    /// <summary>
    /// chunk 的 X 坐标。
    /// </summary>
    public int X { get; } = x;

    /// <summary>
    /// chunk 的 Y 坐标。
    /// </summary>
    public int Y { get; } = y;

    /// <inheritdoc />
    public bool Equals(ChunkCoord other)
    {
        return X == other.X && Y == other.Y;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ChunkCoord other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"({X},{Y})";
    }

    /// <summary>
    /// 判断两个 chunk 坐标是否相等。
    /// </summary>
    public static bool operator ==(ChunkCoord left, ChunkCoord right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个 chunk 坐标是否不相等。
    /// </summary>
    public static bool operator !=(ChunkCoord left, ChunkCoord right)
    {
        return !left.Equals(right);
    }
}
