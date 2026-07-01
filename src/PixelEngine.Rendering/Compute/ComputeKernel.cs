namespace PixelEngine.Rendering.Compute;

/// <summary>
/// 已加载 compute kernel 的轻量句柄。
/// </summary>
/// <param name="Name">诊断名称。</param>
/// <param name="Handle">后端原生 program/kernel 句柄。</param>
public readonly record struct ComputeKernel(string Name, uint Handle);
