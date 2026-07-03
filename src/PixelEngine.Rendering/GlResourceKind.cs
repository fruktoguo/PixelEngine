namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL 资源计数器跟踪的 native object 种类。
/// </summary>
public enum GlResourceKind
{
    /// <summary>
    /// Texture object。
    /// </summary>
    Texture,

    /// <summary>
    /// Buffer object。
    /// </summary>
    Buffer,

    /// <summary>
    /// Framebuffer object。
    /// </summary>
    Framebuffer,

    /// <summary>
    /// Graphics shader program object。
    /// </summary>
    ShaderProgram,

    /// <summary>
    /// Compute shader program object。
    /// </summary>
    ComputeProgram,

    /// <summary>
    /// Shader object。
    /// </summary>
    Shader,

    /// <summary>
    /// Vertex array object。
    /// </summary>
    VertexArray,

    /// <summary>
    /// Timer query object。
    /// </summary>
    TimerQuery,
}
