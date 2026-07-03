using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 屏幕空间 overlay 渲染器，供 Demo/Editor 提交角色、刚体或编辑器选择高亮。
/// </summary>
public sealed unsafe class OverlayRenderer : IDisposable
{
    private const int FloatsPerVertex = 9;
    private const int MaxVerticesPerCommand = 24;
    private const int PositionOffset = 0;
    private const int TexCoordOffset = 2;
    private const int ColorOffset = 4;
    private const int UseTextureOffset = 8;

    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly GlBuffer _vertexBuffer;
    private readonly float[] _vertices;
    private readonly int _viewportLocation;
    private readonly int _spriteTextureLocation;
    private readonly uint _vao;
    private bool _disposed;

    /// <summary>
    /// 创建 overlay 渲染器并一次性分配 CPU 顶点数组与 OpenGL 顶点缓冲。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    /// <param name="maxCommandCount">单次 Render 可接受的最大命令数。</param>
    public OverlayRenderer(GL gl, GlslProfile profile, int maxCommandCount = 1024)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (maxCommandCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCommandCount), "Overlay 最大命令数必须为正数。");
        }

        _gl = gl;
        MaxCommandCount = maxCommandCount;
        _vertices = new float[maxCommandCount * MaxVerticesPerCommand * FloatsPerVertex];
        _program = ShaderProgram.Create(gl, OverlayShaderSources.Vertex(profile), OverlayShaderSources.Fragment(profile));
        _viewportLocation = _program.GetUniformLocation("uViewportSize");
        _spriteTextureLocation = _program.GetUniformLocation("uSpriteTexture");
        _vao = gl.GenVertexArray();
        GlResourceTracker.TrackCreated(GlResourceKind.VertexArray, _vao);
        _vertexBuffer = new GlBuffer(gl, BufferTargetARB.ArrayBuffer);

        gl.BindVertexArray(_vao);
        _vertexBuffer.Allocate((nuint)(_vertices.Length * sizeof(float)), BufferUsageARB.DynamicDraw);

        const uint stride = FloatsPerVertex * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)(PositionOffset * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(TexCoordOffset * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(ColorOffset * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(UseTextureOffset * sizeof(float)));
    }

    /// <summary>
    /// 单次 Render 可接受的最大命令数。
    /// </summary>
    public int MaxCommandCount { get; }

    /// <summary>
    /// 将 overlay 命令绘制到指定颜色目标。命令坐标以该目标左上角为原点，单位为 viewport pixel。
    /// </summary>
    /// <param name="commands">只读 overlay 命令列表。</param>
    /// <param name="destination">输出颜色目标。</param>
    public void Render(ReadOnlySpan<OverlayCommand> commands, ColorRenderTarget destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        destination.BindFramebuffer();
        RenderCore(commands, destination.Width, destination.Height, bindDefaultFramebuffer: false);
    }

    /// <summary>
    /// 将 overlay 命令绘制到当前已绑定 framebuffer。命令坐标以 viewport 左上角为原点，单位为屏幕像素。
    /// </summary>
    /// <param name="commands">只读 overlay 命令列表。</param>
    /// <param name="viewportWidth">当前 framebuffer viewport 宽度，单位为像素。</param>
    /// <param name="viewportHeight">当前 framebuffer viewport 高度，单位为像素。</param>
    public void Render(ReadOnlySpan<OverlayCommand> commands, int viewportWidth, int viewportHeight)
    {
        PresentationViewport viewport = PresentationViewport.Fit(viewportWidth, viewportHeight, viewportWidth, viewportHeight);
        RenderCore(commands, viewport, bindDefaultFramebuffer: true);
    }

    /// <summary>
    /// 将内部画布坐标系的 overlay 命令绘制到默认 framebuffer 的等比呈现区域。
    /// </summary>
    /// <param name="commands">只读 overlay 命令列表。</param>
    /// <param name="viewport">内部画布在默认 framebuffer 中的呈现区域。</param>
    public void Render(ReadOnlySpan<OverlayCommand> commands, PresentationViewport viewport)
    {
        RenderCore(commands, viewport, bindDefaultFramebuffer: true);
    }

    private void RenderCore(ReadOnlySpan<OverlayCommand> commands, int viewportWidth, int viewportHeight, bool bindDefaultFramebuffer)
    {
        PresentationViewport viewport = PresentationViewport.Fit(viewportWidth, viewportHeight, viewportWidth, viewportHeight);
        RenderCore(commands, viewport, bindDefaultFramebuffer);
    }

    private void RenderCore(ReadOnlySpan<OverlayCommand> commands, PresentationViewport viewport, bool bindDefaultFramebuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (commands.Length > MaxCommandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(commands), "Overlay 命令数超过构造时声明的容量。");
        }

        for (int i = 0; i < commands.Length; i++)
        {
            commands[i].Validate();
        }

        if (bindDefaultFramebuffer)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        _gl.Viewport(viewport.X, viewport.Y, (uint)viewport.Width, (uint)viewport.Height);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        if (commands.IsEmpty)
        {
            _gl.Disable(EnableCap.Blend);
            return;
        }

        _program.Use();
        float viewportWidthValue = viewport.SourceWidth;
        float viewportHeightValue = viewport.SourceHeight;
        _gl.Uniform2(_viewportLocation, viewportWidthValue, viewportHeightValue);
        _gl.Uniform1(_spriteTextureLocation, 0);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.BindVertexArray(_vao);

        int vertexCount = 0;
        bool batchUsesTexture = false;
        uint batchTexture = 0;
        for (int i = 0; i < commands.Length; i++)
        {
            OverlayCommand command = commands[i];
            bool commandUsesTexture = command.PrimitiveType == OverlayPrimitiveType.Sprite;
            uint commandTexture = commandUsesTexture ? command.Sprite.TextureHandle : 0;
            if (vertexCount > 0 && (batchUsesTexture != commandUsesTexture || batchTexture != commandTexture))
            {
                Flush(vertexCount, batchUsesTexture, batchTexture);
                vertexCount = 0;
            }

            if (vertexCount == 0)
            {
                batchUsesTexture = commandUsesTexture;
                batchTexture = commandTexture;
            }

            AppendCommand(command, ref vertexCount);
            if (vertexCount + MaxVerticesPerCommand > _vertices.Length / FloatsPerVertex)
            {
                Flush(vertexCount, batchUsesTexture, batchTexture);
                vertexCount = 0;
            }
        }

        if (vertexCount > 0)
        {
            Flush(vertexCount, batchUsesTexture, batchTexture);
        }

        _gl.Disable(EnableCap.Blend);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _vertexBuffer.Dispose();
        _gl.DeleteVertexArray(_vao);
        GlResourceTracker.TrackDeleted(GlResourceKind.VertexArray, _vao);
        _program.Dispose();
        _disposed = true;
    }

    private void Flush(int vertexCount, bool usesTexture, uint textureHandle)
    {
        if (usesTexture)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, textureHandle);
        }

        _vertexBuffer.Bind();
        fixed (float* data = _vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexCount * FloatsPerVertex * sizeof(float)), data);
        }

        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
    }

    private void AppendCommand(in OverlayCommand command, ref int vertexCount)
    {
        switch (command.PrimitiveType)
        {
            case OverlayPrimitiveType.SolidRectangle:
                AppendRectangle(command.ViewportX, command.ViewportY, command.Width, command.Height, command.ColorBgra, 0f, 0f, 0f, 0f, false, ref vertexCount);
                return;
            case OverlayPrimitiveType.OutlineRectangle:
                AppendOutline(command.ViewportX, command.ViewportY, command.Width, command.Height, command.OutlineThickness, command.ColorBgra, ref vertexCount);
                return;
            case OverlayPrimitiveType.Sprite:
                AppendRectangle(
                    command.ViewportX,
                    command.ViewportY,
                    command.Width,
                    command.Height,
                    command.ColorBgra,
                    command.Sprite.U0,
                    command.Sprite.V0,
                    command.Sprite.U1,
                    command.Sprite.V1,
                    true,
                    ref vertexCount);
                return;
            case OverlayPrimitiveType.Line:
                AppendLine(command.ViewportX, command.ViewportY, command.LineEndX, command.LineEndY, command.OutlineThickness, command.ColorBgra, ref vertexCount);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), "未知 Overlay 原语类型。");
        }
    }

    private void AppendLine(float x0, float y0, float x1, float y1, float thickness, uint colorBgra, ref int vertexCount)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        float nx = -(dy / length) * thickness * 0.5f;
        float ny = dx / length * thickness * 0.5f;
        ColorToRgba(colorBgra, out float r, out float g, out float b, out float a);

        AppendVertex(x0 + nx, y0 + ny, 0f, 0f, r, g, b, a, 0f, ref vertexCount);
        AppendVertex(x1 + nx, y1 + ny, 0f, 0f, r, g, b, a, 0f, ref vertexCount);
        AppendVertex(x1 - nx, y1 - ny, 0f, 0f, r, g, b, a, 0f, ref vertexCount);
        AppendVertex(x0 + nx, y0 + ny, 0f, 0f, r, g, b, a, 0f, ref vertexCount);
        AppendVertex(x1 - nx, y1 - ny, 0f, 0f, r, g, b, a, 0f, ref vertexCount);
        AppendVertex(x0 - nx, y0 - ny, 0f, 0f, r, g, b, a, 0f, ref vertexCount);
    }

    private void AppendOutline(float x, float y, float width, float height, float thickness, uint colorBgra, ref int vertexCount)
    {
        float clampedThickness = MathF.Min(thickness, MathF.Min(width, height) * 0.5f);
        AppendRectangle(x, y, width, clampedThickness, colorBgra, 0f, 0f, 0f, 0f, false, ref vertexCount);
        AppendRectangle(x, y + height - clampedThickness, width, clampedThickness, colorBgra, 0f, 0f, 0f, 0f, false, ref vertexCount);

        float innerHeight = height - (clampedThickness * 2f);
        if (innerHeight > 0f)
        {
            AppendRectangle(x, y + clampedThickness, clampedThickness, innerHeight, colorBgra, 0f, 0f, 0f, 0f, false, ref vertexCount);
            AppendRectangle(x + width - clampedThickness, y + clampedThickness, clampedThickness, innerHeight, colorBgra, 0f, 0f, 0f, 0f, false, ref vertexCount);
        }
    }

    private void AppendRectangle(
        float x,
        float y,
        float width,
        float height,
        uint colorBgra,
        float u0,
        float v0,
        float u1,
        float v1,
        bool useTexture,
        ref int vertexCount)
    {
        float x1 = x + width;
        float y1 = y + height;
        ColorToRgba(colorBgra, out float r, out float g, out float b, out float a);
        float textureFlag = useTexture ? 1f : 0f;

        AppendVertex(x, y, u0, v0, r, g, b, a, textureFlag, ref vertexCount);
        AppendVertex(x1, y, u1, v0, r, g, b, a, textureFlag, ref vertexCount);
        AppendVertex(x1, y1, u1, v1, r, g, b, a, textureFlag, ref vertexCount);
        AppendVertex(x, y, u0, v0, r, g, b, a, textureFlag, ref vertexCount);
        AppendVertex(x1, y1, u1, v1, r, g, b, a, textureFlag, ref vertexCount);
        AppendVertex(x, y1, u0, v1, r, g, b, a, textureFlag, ref vertexCount);
    }

    private void AppendVertex(
        float x,
        float y,
        float u,
        float v,
        float r,
        float g,
        float b,
        float a,
        float useTexture,
        ref int vertexCount)
    {
        int offset = vertexCount * FloatsPerVertex;
        _vertices[offset] = x;
        _vertices[offset + 1] = y;
        _vertices[offset + 2] = u;
        _vertices[offset + 3] = v;
        _vertices[offset + 4] = r;
        _vertices[offset + 5] = g;
        _vertices[offset + 6] = b;
        _vertices[offset + 7] = a;
        _vertices[offset + 8] = useTexture;
        vertexCount++;
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
