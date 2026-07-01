namespace PixelEngine.Physics;

/// <summary>
/// 上一次 inverse-sampling stamp 写入的 world cell 与对应 body-local 像素。
/// </summary>
/// <param name="WorldX">世界 cell X。</param>
/// <param name="WorldY">世界 cell Y。</param>
/// <param name="Stamp">body-local 刚体像素信息。</param>
public readonly record struct RigidStampedCell(int WorldX, int WorldY, RigidStamp Stamp);
