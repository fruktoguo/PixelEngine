using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// 根据 G1-G4 门控结果创建 compute 后端。
/// </summary>
public static class ComputeBackendFactory
{
    /// <summary>
    /// 创建门控选中的 compute 后端。GL 后端复用 plan/08 已创建的 OpenGL 上下文；不会创建新上下文。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="gate">compute 门控结果。</param>
    /// <returns>compute 后端。</returns>
    public static IComputeBackend Create(GL gl, ComputeCapabilityGate gate)
    {
        ArgumentNullException.ThrowIfNull(gl);
        return gate.SelectedBackend switch
        {
            ComputeBackendKind.GlCompute => new GLComputeBackend(gl, gate),
            ComputeBackendKind.ComputeSharp => new ComputeSharpBackend(),
            ComputeBackendKind.Null => new NullComputeBackend(),
            _ => throw new ArgumentOutOfRangeException(nameof(gate), "未知 compute 后端。"),
        };
    }
}
