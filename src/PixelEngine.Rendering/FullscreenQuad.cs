using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 全屏三角形绘制原语，供 blit 与 post pass 复用。
/// </summary>
public sealed unsafe class FullscreenQuad : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vbo;
    private bool _disposed;

    /// <summary>
    /// 创建全屏三角形 VAO/VBO。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    public FullscreenQuad(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        Vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        GlResourceTracker.TrackCreated(GlResourceKind.VertexArray, Vao);
        GlResourceTracker.TrackCreated(GlResourceKind.Buffer, _vbo);

        ReadOnlySpan<float> vertices =
        [
            -1f, -1f,
             3f, -1f,
            -1f,  3f,
        ];

        gl.BindVertexArray(Vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* data = vertices)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), null);
    }

    /// <summary>
    /// OpenGL vertex array object 句柄。
    /// </summary>
    public uint Vao { get; }

    /// <summary>
    /// 绘制全屏三角形。
    /// </summary>
    public void Draw()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.BindVertexArray(Vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteBuffer(_vbo);
        GlResourceTracker.TrackDeleted(GlResourceKind.Buffer, _vbo);
        _gl.DeleteVertexArray(Vao);
        GlResourceTracker.TrackDeleted(GlResourceKind.VertexArray, Vao);
        _disposed = true;
    }
}
