namespace PixelEngine.Physics;

/// <summary>
/// 一次刚体破坏重建的结果计数。
/// </summary>
/// <param name="DamagedBodies">包含有效 damage 的刚体数量。</param>
/// <param name="DestroyedBodies">销毁的旧刚体数量。</param>
/// <param name="CreatedBodies">创建的新刚体数量。</param>
/// <param name="FragmentPixels">转为碎片粒子的像素数量。</param>
/// <param name="SkippedSleepingBodies">因 sleeping 而跳过的刚体数量。</param>
public readonly record struct RigidDestructionResult(
    int DamagedBodies,
    int DestroyedBodies,
    int CreatedBodies,
    int FragmentPixels,
    int SkippedSleepingBodies);
