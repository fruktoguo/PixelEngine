namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL 资源 live-count 快照，用于关闭阶段 native leak 证据采集。
/// </summary>
/// <param name="Textures">仍处于 live 状态的 texture object 数量。</param>
/// <param name="Buffers">仍处于 live 状态的 buffer object 数量。</param>
/// <param name="Framebuffers">仍处于 live 状态的 framebuffer object 数量。</param>
/// <param name="ShaderPrograms">仍处于 live 状态的 graphics shader program 数量。</param>
/// <param name="ComputePrograms">仍处于 live 状态的 compute shader program 数量。</param>
/// <param name="Shaders">仍处于 live 状态的 shader object 数量。</param>
/// <param name="VertexArrays">仍处于 live 状态的 vertex array object 数量。</param>
/// <param name="TimerQueries">仍处于 live 状态的 timer query object 数量。</param>
public readonly record struct GlResourceCounts(
    int Textures,
    int Buffers,
    int Framebuffers,
    int ShaderPrograms,
    int ComputePrograms,
    int Shaders,
    int VertexArrays,
    int TimerQueries)
{
    /// <summary>
    /// 所有已跟踪 GL 资源 live-count 之和。
    /// </summary>
    public int Total =>
        Textures +
        Buffers +
        Framebuffers +
        ShaderPrograms +
        ComputePrograms +
        Shaders +
        VertexArrays +
        TimerQueries;

    /// <summary>
    /// 返回指定资源种类的 live-count。
    /// </summary>
    /// <param name="kind">资源种类。</param>
    /// <returns>该种类仍处于 live 状态的数量。</returns>
    public int GetCount(GlResourceKind kind)
    {
        return kind switch
        {
            GlResourceKind.Texture => Textures,
            GlResourceKind.Buffer => Buffers,
            GlResourceKind.Framebuffer => Framebuffers,
            GlResourceKind.ShaderProgram => ShaderPrograms,
            GlResourceKind.ComputeProgram => ComputePrograms,
            GlResourceKind.Shader => Shaders,
            GlResourceKind.VertexArray => VertexArrays,
            GlResourceKind.TimerQuery => TimerQueries,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "未知 GL 资源种类。"),
        };
    }
}
