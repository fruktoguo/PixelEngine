using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL 错误检查辅助。Release 中可按调用点抽样使用，避免热路径逐调用查询。
/// </summary>
public static class GlErrorChecker
{
    /// <summary>
    /// 检查当前 OpenGL 错误状态。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="operation">被检查的操作名称。</param>
    /// <exception cref="InvalidOperationException">检测到 OpenGL 错误时抛出。</exception>
    public static void ThrowIfError(GL gl, string operation)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        GLEnum error = gl.GetError();
        if (error != GLEnum.NoError)
        {
            throw new InvalidOperationException($"{operation} 触发 OpenGL 错误: {error}。");
        }
    }
}
