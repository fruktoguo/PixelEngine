namespace PixelEngine.Serialization;

/// <summary>
/// 刚体的存档 DTO。body-local mask 是不可变权威形状源，读档时由 physics 子系统重建运行时刚体。
/// </summary>
public sealed class RigidBodySnapshot
{
    private readonly byte[] _bodyLocalMask;
    private readonly ushort[] _material;

    /// <summary>
    /// 创建刚体存档快照。
    /// </summary>
    public RigidBodySnapshot(
        int id,
        int width,
        int height,
        ReadOnlySpan<byte> bodyLocalMask,
        ReadOnlySpan<ushort> material,
        float posX,
        float posY,
        float rotCos,
        float rotSin,
        float linVelX,
        float linVelY,
        float angVel)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "刚体 mask 宽度必须为正。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "刚体 mask 高度必须为正。");
        }

        int area = checked(width * height);
        if (bodyLocalMask.Length != area)
        {
            throw new ArgumentException("body-local mask 长度必须等于 width * height。", nameof(bodyLocalMask));
        }

        if (material.Length != area)
        {
            throw new ArgumentException("刚体 material 长度必须等于 width * height。", nameof(material));
        }

        Id = id;
        Width = width;
        Height = height;
        _bodyLocalMask = bodyLocalMask.ToArray();
        _material = material.ToArray();
        PosX = posX;
        PosY = posY;
        RotCos = rotCos;
        RotSin = rotSin;
        LinVelX = linVelX;
        LinVelY = linVelY;
        AngVel = angVel;
    }

    /// <summary>
    /// 稳定刚体 id。
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// body-local mask 宽度，单位 cell。
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// body-local mask 高度，单位 cell。
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// body-local 不可变形状 mask。
    /// </summary>
    public ReadOnlyMemory<byte> BodyLocalMask => _bodyLocalMask;

    /// <summary>
    /// body-local 每像素材质 id。
    /// </summary>
    public ReadOnlyMemory<ushort> Material => _material;

    /// <summary>
    /// 世界位置 X，单位 cell。
    /// </summary>
    public float PosX { get; }

    /// <summary>
    /// 世界位置 Y，单位 cell。
    /// </summary>
    public float PosY { get; }

    /// <summary>
    /// 当前旋转的 cos 分量。
    /// </summary>
    public float RotCos { get; }

    /// <summary>
    /// 当前旋转的 sin 分量。
    /// </summary>
    public float RotSin { get; }

    /// <summary>
    /// 线速度 X，单位 cell/tick。
    /// </summary>
    public float LinVelX { get; }

    /// <summary>
    /// 线速度 Y，单位 cell/tick。
    /// </summary>
    public float LinVelY { get; }

    /// <summary>
    /// 角速度。
    /// </summary>
    public float AngVel { get; }
}
