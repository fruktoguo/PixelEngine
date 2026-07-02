namespace PixelEngine.Scripting;

/// <summary>
/// 脚本实体的通用 2D Transform；用于相机跟随、编辑器层级显示与脚本间共享位置。
/// </summary>
public sealed class Transform : IComponent
{
    /// <summary>
    /// 世界中心 X 坐标，单位 cell。
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// 世界中心 Y 坐标，单位 cell。
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// 旋转角，单位弧度。
    /// </summary>
    public float RotationRadians { get; set; }

    /// <summary>
    /// X 方向缩放。
    /// </summary>
    public float ScaleX { get; set; } = 1f;

    /// <summary>
    /// Y 方向缩放。
    /// </summary>
    public float ScaleY { get; set; } = 1f;

    /// <summary>
    /// 设置世界中心坐标。
    /// </summary>
    public void SetPosition(float x, float y)
    {
        if (!float.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "Transform X 必须为有限值。");
        }

        if (!float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Transform Y 必须为有限值。");
        }

        X = x;
        Y = y;
    }
}
