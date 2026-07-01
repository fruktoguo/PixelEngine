namespace PixelEngine.Rendering.Compute;

/// <summary>
/// GPU compute 后端类型。
/// </summary>
public enum ComputeBackendKind
{
    /// <summary>
    /// 空后端，所有 compute 工作回退到 plan/08 的 fragment/CPU 路径。
    /// </summary>
    Null = 0,

    /// <summary>
    /// Silk.NET OpenGL 4.3 compute shader 后端。
    /// </summary>
    GlCompute = 1,

    /// <summary>
    /// Windows/DX12 ComputeSharp 可选后端。
    /// </summary>
    ComputeSharp = 2,
}
