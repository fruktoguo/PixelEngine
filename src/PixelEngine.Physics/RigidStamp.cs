namespace PixelEngine.Physics;

/// <summary>
/// 刚体像素在权威网格中的 stamp 映射。
/// </summary>
/// <param name="BodyKey">刚体 key。</param>
/// <param name="LocalX">body-local X。</param>
/// <param name="LocalY">body-local Y。</param>
/// <param name="Material">材质 id。</param>
public readonly record struct RigidStamp(int BodyKey, int LocalX, int LocalY, ushort Material);
