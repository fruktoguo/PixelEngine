using System.Numerics;

namespace PixelEngine.Physics;

/// <summary>
/// 刚体 body-local 空间的不可变权威像素形状。
/// </summary>
public sealed class BodyLocalMask
{
    private readonly byte[] _solidBits;
    private readonly ushort[] _materials;

    /// <summary>
    /// 创建不可变 body-local mask。
    /// </summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <param name="localOrigin">局部原点。</param>
    /// <param name="solidBits">固体 bit/byte，非 0 表示固体。</param>
    /// <param name="materials">每像素材质。</param>
    public BodyLocalMask(int width, int height, Vector2 localOrigin, ReadOnlySpan<byte> solidBits, ReadOnlySpan<ushort> materials)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int area = checked(width * height);
        if (solidBits.Length < area)
        {
            throw new ArgumentException("solidBits 长度不足。", nameof(solidBits));
        }

        if (materials.Length < area)
        {
            throw new ArgumentException("materials 长度不足。", nameof(materials));
        }

        Width = width;
        Height = height;
        LocalOrigin = localOrigin;
        _solidBits = GC.AllocateArray<byte>(area, pinned: true);
        _materials = GC.AllocateArray<ushort>(area, pinned: true);
        solidBits[..area].CopyTo(_solidBits);
        materials[..area].CopyTo(_materials);
        int solidCount = 0;
        for (int i = 0; i < area; i++)
        {
            if (_solidBits[i] != 0)
            {
                solidCount++;
            }
        }

        SolidPixelCount = solidCount;
    }

    /// <summary>宽度。</summary>
    public int Width { get; }

    /// <summary>高度。</summary>
    public int Height { get; }

    /// <summary>局部原点。</summary>
    public Vector2 LocalOrigin { get; }

    /// <summary>固体像素数量。</summary>
    public int SolidPixelCount { get; }

    /// <summary>固体 mask。</summary>
    public ReadOnlySpan<byte> SolidBits => _solidBits;

    /// <summary>材质数组。</summary>
    public ReadOnlySpan<ushort> Materials => _materials;

    /// <summary>
    /// 判断局部像素是否为固体。
    /// </summary>
    public bool IsSolid(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)Height
            && _solidBits[(y * Width) + x] != 0;
    }

    /// <summary>
    /// 获取局部像素材质。
    /// </summary>
    public ushort MaterialAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)x, (uint)Width, nameof(x));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)y, (uint)Height, nameof(y));
        return _materials[(y * Width) + x];
    }
}
