using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// UI layer 前后的 OpenGL 状态快照，避免多个 UI 后端互相泄漏状态。
/// </summary>
internal readonly struct UiGlStateSnapshot
{
    private readonly int _framebuffer;
    private readonly int _program;
    private readonly int _vertexArray;
    private readonly int _arrayBuffer;
    private readonly int _elementArrayBuffer;
    private readonly int _activeTexture;
    private readonly int _texture2D;
    private readonly int _blendSrcRgb;
    private readonly int _blendDstRgb;
    private readonly int _blendSrcAlpha;
    private readonly int _blendDstAlpha;
    private readonly bool _blend;
    private readonly bool _depth;
    private readonly bool _cull;
    private readonly bool _scissor;
    private readonly int _viewportX;
    private readonly int _viewportY;
    private readonly int _viewportWidth;
    private readonly int _viewportHeight;
    private readonly int _scissorX;
    private readonly int _scissorY;
    private readonly int _scissorWidth;
    private readonly int _scissorHeight;

    private UiGlStateSnapshot(
        int framebuffer,
        int program,
        int vertexArray,
        int arrayBuffer,
        int elementArrayBuffer,
        int activeTexture,
        int texture2D,
        int blendSrcRgb,
        int blendDstRgb,
        int blendSrcAlpha,
        int blendDstAlpha,
        bool blend,
        bool depth,
        bool cull,
        bool scissor,
        ReadOnlySpan<int> viewport,
        ReadOnlySpan<int> scissorBox)
    {
        _framebuffer = framebuffer;
        _program = program;
        _vertexArray = vertexArray;
        _arrayBuffer = arrayBuffer;
        _elementArrayBuffer = elementArrayBuffer;
        _activeTexture = activeTexture;
        _texture2D = texture2D;
        _blendSrcRgb = blendSrcRgb;
        _blendDstRgb = blendDstRgb;
        _blendSrcAlpha = blendSrcAlpha;
        _blendDstAlpha = blendDstAlpha;
        _blend = blend;
        _depth = depth;
        _cull = cull;
        _scissor = scissor;
        _viewportX = viewport[0];
        _viewportY = viewport[1];
        _viewportWidth = viewport[2];
        _viewportHeight = viewport[3];
        _scissorX = scissorBox[0];
        _scissorY = scissorBox[1];
        _scissorWidth = scissorBox[2];
        _scissorHeight = scissorBox[3];
    }

    public static UiGlStateSnapshot Capture(GL gl)
    {
        Span<int> viewport = stackalloc int[4];
        Span<int> scissorBox = stackalloc int[4];
        gl.GetInteger(GLEnum.Viewport, viewport);
        gl.GetInteger(GLEnum.ScissorBox, scissorBox);
        gl.GetInteger(GLEnum.FramebufferBinding, out int framebuffer);
        gl.GetInteger(GLEnum.CurrentProgram, out int program);
        gl.GetInteger(GLEnum.VertexArrayBinding, out int vertexArray);
        gl.GetInteger(GLEnum.ArrayBufferBinding, out int arrayBuffer);
        gl.GetInteger(GLEnum.ElementArrayBufferBinding, out int elementArrayBuffer);
        gl.GetInteger(GLEnum.ActiveTexture, out int activeTexture);
        gl.GetInteger(GLEnum.TextureBinding2D, out int texture2D);
        gl.GetInteger(GLEnum.BlendSrcRgb, out int blendSrcRgb);
        gl.GetInteger(GLEnum.BlendDstRgb, out int blendDstRgb);
        gl.GetInteger(GLEnum.BlendSrcAlpha, out int blendSrcAlpha);
        gl.GetInteger(GLEnum.BlendDstAlpha, out int blendDstAlpha);
        return new UiGlStateSnapshot(
            framebuffer,
            program,
            vertexArray,
            arrayBuffer,
            elementArrayBuffer,
            activeTexture,
            texture2D,
            blendSrcRgb,
            blendDstRgb,
            blendSrcAlpha,
            blendDstAlpha,
            gl.IsEnabled(EnableCap.Blend),
            gl.IsEnabled(EnableCap.DepthTest),
            gl.IsEnabled(EnableCap.CullFace),
            gl.IsEnabled(EnableCap.ScissorTest),
            viewport,
            scissorBox);
    }

    public void Restore(GL gl)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)_framebuffer);
        gl.Viewport(_viewportX, _viewportY, (uint)_viewportWidth, (uint)_viewportHeight);
        gl.Scissor(_scissorX, _scissorY, (uint)_scissorWidth, (uint)_scissorHeight);
        Set(gl, EnableCap.Blend, _blend);
        Set(gl, EnableCap.DepthTest, _depth);
        Set(gl, EnableCap.CullFace, _cull);
        Set(gl, EnableCap.ScissorTest, _scissor);
        gl.BlendFuncSeparate(
            (BlendingFactor)_blendSrcRgb,
            (BlendingFactor)_blendDstRgb,
            (BlendingFactor)_blendSrcAlpha,
            (BlendingFactor)_blendDstAlpha);
        gl.UseProgram((uint)_program);
        gl.BindVertexArray((uint)_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_arrayBuffer);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, (uint)_elementArrayBuffer);
        gl.ActiveTexture((TextureUnit)_activeTexture);
        gl.BindTexture(TextureTarget.Texture2D, (uint)_texture2D);
    }

    private static void Set(GL gl, EnableCap cap, bool enabled)
    {
        if (enabled)
        {
            gl.Enable(cap);
        }
        else
        {
            gl.Disable(cap);
        }
    }
}
