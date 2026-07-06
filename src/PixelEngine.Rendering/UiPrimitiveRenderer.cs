using System.Numerics;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 共享 UI 三角形批提交器。由 <see cref="UiPresentContext" /> 暴露给 UI 后端复用。
/// </summary>
internal sealed unsafe class UiPrimitiveRenderer : IDisposable
{
    private const int FloatsPerVertex = 8;
    private const int PositionOffset = 0;
    private const int TexCoordOffset = 2;
    private const int ColorOffset = 4;

    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly GlBuffer _vertexBuffer;
    private readonly GlBuffer _indexBuffer;
    private readonly float[] _vertices;
    private readonly ushort[] _indices;
    private readonly int _framebufferSizeLocation;
    private readonly int _transformLocation;
    private readonly int _textureLocation;
    private readonly int _useTextureLocation;
    private readonly uint _vao;
    private bool _disposed;

    public UiPrimitiveRenderer(GL gl, GlslProfile profile, int maxVertices = 65_536, int maxIndices = 131_072)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (maxVertices <= 0 || maxIndices <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVertices), "UI primitive renderer 容量必须为正数。");
        }

        _gl = gl;
        MaxVertices = maxVertices;
        MaxIndices = maxIndices;
        _vertices = new float[checked(maxVertices * FloatsPerVertex)];
        _indices = new ushort[maxIndices];
        _program = ShaderProgram.Create(gl, UiShaderSources.Vertex(profile), UiShaderSources.Fragment(profile));
        _framebufferSizeLocation = _program.GetUniformLocation("uFramebufferSize");
        _transformLocation = _program.GetUniformLocation("uTransform");
        _textureLocation = _program.GetUniformLocation("uTexture");
        _useTextureLocation = _program.GetUniformLocation("uUseTexture");
        _vao = gl.GenVertexArray();
        GlResourceTracker.TrackCreated(GlResourceKind.VertexArray, _vao);
        _vertexBuffer = new GlBuffer(gl, BufferTargetARB.ArrayBuffer);
        _indexBuffer = new GlBuffer(gl, BufferTargetARB.ElementArrayBuffer);

        gl.BindVertexArray(_vao);
        _vertexBuffer.Allocate((nuint)(_vertices.Length * sizeof(float)), BufferUsageARB.DynamicDraw);
        _indexBuffer.Allocate((nuint)(_indices.Length * sizeof(ushort)), BufferUsageARB.DynamicDraw);

        const uint stride = FloatsPerVertex * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)(PositionOffset * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(TexCoordOffset * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(ColorOffset * sizeof(float)));
    }

    /// <summary>
    /// 最大顶点数。
    /// </summary>
    public int MaxVertices { get; }

    /// <summary>
    /// 最大索引数。
    /// </summary>
    public int MaxIndices { get; }

    public void SubmitTriangles(
        ReadOnlySpan<UiVertex> vertices,
        ReadOnlySpan<ushort> indices,
        in UiDrawState draw,
        int framebufferWidth,
        int framebufferHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        draw.Validate();
        if (vertices.Length > MaxVertices)
        {
            throw new ArgumentOutOfRangeException(nameof(vertices), "UI 顶点数超过构造时声明的容量。");
        }

        if (indices.Length > MaxIndices)
        {
            throw new ArgumentOutOfRangeException(nameof(indices), "UI 索引数超过构造时声明的容量。");
        }

        if (indices.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            WriteVertex(vertices[i], i);
        }

        foreach (ushort index in indices)
        {
            if (index >= vertices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(indices), "UI 索引引用了不存在的顶点。");
            }
        }

        indices.CopyTo(_indices);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);
        ApplyScissor(draw.Scissor, framebufferHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _program.Use();
        float framebufferWidthValue = framebufferWidth;
        float framebufferHeightValue = framebufferHeight;
        _gl.Uniform2(_framebufferSizeLocation, framebufferWidthValue, framebufferHeightValue);
        SetTransform(draw.Transform);
        _gl.Uniform1(_textureLocation, 0);
        _gl.Uniform1(_useTextureLocation, draw.TextureHandle == 0 ? 0f : 1f);
        if (draw.TextureHandle != 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, draw.TextureHandle);
        }

        _gl.BindVertexArray(_vao);
        _vertexBuffer.Bind();
        fixed (float* vertexData = _vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertices.Length * FloatsPerVertex * sizeof(float)), vertexData);
        }

        _indexBuffer.Bind();
        fixed (ushort* indexData = _indices)
        {
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)(indices.Length * sizeof(ushort)), indexData);
        }

        _gl.DrawElements(PrimitiveType.Triangles, (uint)indices.Length, DrawElementsType.UnsignedShort, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _gl.DeleteVertexArray(_vao);
        GlResourceTracker.TrackDeleted(GlResourceKind.VertexArray, _vao);
        _program.Dispose();
        _disposed = true;
    }

    private void WriteVertex(UiVertex vertex, int index)
    {
        int offset = index * FloatsPerVertex;
        _vertices[offset] = vertex.X;
        _vertices[offset + 1] = vertex.Y;
        _vertices[offset + 2] = vertex.U;
        _vertices[offset + 3] = vertex.V;
        ColorToRgba(vertex.ColorBgra, out _vertices[offset + 4], out _vertices[offset + 5], out _vertices[offset + 6], out _vertices[offset + 7]);
    }

    private void ApplyScissor(UiScissorRect? scissor, int framebufferHeight)
    {
        if (scissor is not { } rect)
        {
            _gl.Disable(EnableCap.ScissorTest);
            return;
        }

        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(rect.X, framebufferHeight - rect.Y - rect.Height, (uint)rect.Width, (uint)rect.Height);
    }

    private void SetTransform(in Matrix3x2 transform)
    {
        Span<float> matrix =
        [
            transform.M11, transform.M12, 0f,
            transform.M21, transform.M22, 0f,
            transform.M31, transform.M32, 1f,
        ];
        fixed (float* values = matrix)
        {
            _gl.UniformMatrix3(_transformLocation, 1, false, values);
        }
    }

    private static void ColorToRgba(uint bgra, out float r, out float g, out float b, out float a)
    {
        const float scale = 1f / 255f;
        b = (byte)(bgra & 0xFF) * scale;
        g = (byte)((bgra >> 8) & 0xFF) * scale;
        r = (byte)((bgra >> 16) & 0xFF) * scale;
        a = (byte)((bgra >> 24) & 0xFF) * scale;
    }
}
