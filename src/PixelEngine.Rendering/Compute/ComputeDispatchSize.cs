namespace PixelEngine.Rendering.Compute;

/// <summary>
/// GPU compute dispatch 的 work group 数。
/// </summary>
/// <param name="GroupsX">X 方向 work group 数。</param>
/// <param name="GroupsY">Y 方向 work group 数。</param>
/// <param name="GroupsZ">Z 方向 work group 数。</param>
public readonly record struct ComputeDispatchSize(uint GroupsX, uint GroupsY, uint GroupsZ);
