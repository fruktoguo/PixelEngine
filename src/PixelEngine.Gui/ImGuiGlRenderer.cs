using Hexa.NET.ImGui;
using PixelEngine.Rendering;
using Silk.NET.OpenGL;
using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 使用引擎当前 Silk.NET <see cref="GL" /> context 渲染 Dear ImGui draw data。
/// 同一实现覆盖 desktop GL 3.3 与 ANGLE/OpenGL ES 3.0，避免 native backend 自带 loader
/// 在 EGL context 中错误转向 WGL。设计依据：plan/19 Windows 窗口捕获兼容性。
/// </summary>
public sealed unsafe class ImGuiGlRenderer
{
    private readonly GL _gl;
    private readonly bool _isGles;
    private readonly bool _supportsVertexOffset;
    private ShaderProgram? _program;
    private GlBuffer? _vertexBuffer;
    private GlBuffer? _indexBuffer;
    private uint _vertexArray;
    private int _textureLocation;
    private int _projectionLocation;
    private bool _initialized;

    /// <summary>
    /// 创建复用指定窗口 GL context 的 ImGui renderer。
    /// </summary>
    /// <param name="window">当前渲染窗口。</param>
    public ImGuiGlRenderer(RenderWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _gl = window.Gl;
        _isGles = window.Capabilities.IsGles;
        _supportsVertexOffset = !_isGles && IsAtLeast(window.Capabilities, 3, 2);
    }

    /// <summary>
    /// 为当前 ImGui context 创建 GPU 对象并声明 renderer 能力。
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            throw new InvalidOperationException("ImGui GL renderer 已经初始化。");
        }

        GlslProfile profile = _isGles ? GlslProfile.Gles300 : GlslProfile.DesktopGl330;
        _program = ShaderProgram.Create(_gl, VertexShader(profile), FragmentShader(profile));
        _textureLocation = _program.GetUniformLocation("Texture");
        _projectionLocation = _program.GetUniformLocation("ProjMtx");
        _vertexBuffer = new GlBuffer(_gl, BufferTargetARB.ArrayBuffer);
        _indexBuffer = new GlBuffer(_gl, BufferTargetARB.ElementArrayBuffer);
        _vertexArray = _gl.GenVertexArray();

        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        if (_supportsVertexOffset)
        {
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        }

        _gl.GetInteger(GLEnum.MaxTextureSize, out int maxTextureSize);
        ImGuiPlatformIOPtr platformIO = ImGui.GetPlatformIO();
        platformIO.RendererTextureMaxWidth = maxTextureSize;
        platformIO.RendererTextureMaxHeight = maxTextureSize;
        _initialized = true;
    }

    /// <summary>
    /// 校验 renderer 已就绪。GPU 对象在初始化时一次创建，稳态帧不重建。
    /// </summary>
    public void NewFrame()
    {
        ThrowIfNotInitialized();
    }

    /// <summary>
    /// 处理 ImGui 1.92 动态纹理请求并渲染 draw data；返回前恢复调用者 GL 状态。
    /// </summary>
    /// <param name="drawData">当前 context 的 ImGui draw data。</param>
    public void Render(ImDrawDataPtr drawData)
    {
        ThrowIfNotInitialized();
        if (drawData.IsNull)
        {
            return;
        }

        int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return;
        }

        UiGlStateSnapshot state = UiGlStateSnapshot.Capture(_gl);
        try
        {
            ProcessTextureRequests(drawData.Handle->Textures, forceDestroy: false);
            SetupRenderState(drawData, framebufferWidth, framebufferHeight);
            RenderCommandLists(drawData, framebufferWidth, framebufferHeight);
        }
        finally
        {
            state.Restore(_gl);
        }
    }

    /// <summary>
    /// 在 ImGui context 销毁前释放动态纹理与 renderer 自有 GPU 对象。
    /// </summary>
    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        UiGlStateSnapshot state = UiGlStateSnapshot.Capture(_gl);
        try
        {
            ImGuiPlatformIOPtr platformIO = ImGui.GetPlatformIO();
            fixed (ImVector<ImTextureDataPtr>* textures = &platformIO.Textures)
            {
                ProcessTextureRequests(textures, forceDestroy: true);
            }
        }
        finally
        {
            state.Restore(_gl);
        }

        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags &= ~(ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures);
        ImGuiPlatformIOPtr resetPlatformIO = ImGui.GetPlatformIO();
        resetPlatformIO.RendererTextureMaxWidth = 0;
        resetPlatformIO.RendererTextureMaxHeight = 0;

        if (_vertexArray != 0)
        {
            _gl.DeleteVertexArray(_vertexArray);
            _vertexArray = 0;
        }

        _indexBuffer?.Dispose();
        _indexBuffer = null;
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _program?.Dispose();
        _program = null;
        _textureLocation = -1;
        _projectionLocation = -1;
        _initialized = false;
    }

    private void RenderCommandLists(ImDrawDataPtr drawData, int framebufferWidth, int framebufferHeight)
    {
        Vector2 clipOffset = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;
        ImVector<ImDrawListPtr> commandLists = drawData.CmdLists;
        for (int listIndex = 0; listIndex < commandLists.Size; listIndex++)
        {
            ImDrawList* drawList = commandLists.Data[listIndex].Handle;
            if (drawList is null)
            {
                continue;
            }

            _vertexBuffer!.Bind();
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(drawList->VtxBuffer.Size * sizeof(ImDrawVert)),
                drawList->VtxBuffer.Data,
                BufferUsageARB.StreamDraw);
            _indexBuffer!.Bind();
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(drawList->IdxBuffer.Size * sizeof(ushort)),
                drawList->IdxBuffer.Data,
                BufferUsageARB.StreamDraw);

            ImVector<ImDrawCmd> commands = drawList->CmdBuffer;
            for (int commandIndex = 0; commandIndex < commands.Size; commandIndex++)
            {
                ImDrawCmd* command = commands.Data + commandIndex;
                if (command->UserCallback is not null)
                {
                    if ((nint)command->UserCallback == ImGui.ImDrawCallbackResetRenderState)
                    {
                        SetupRenderState(drawData, framebufferWidth, framebufferHeight);
                    }
                    else
                    {
                        ((delegate* unmanaged[Cdecl]<ImDrawList*, ImDrawCmd*, void>)command->UserCallback)(drawList, command);
                    }

                    continue;
                }

                Vector4 clipRect = command->ClipRect;
                float clipMinX = (clipRect.X - clipOffset.X) * clipScale.X;
                float clipMinY = (clipRect.Y - clipOffset.Y) * clipScale.Y;
                float clipMaxX = (clipRect.Z - clipOffset.X) * clipScale.X;
                float clipMaxY = (clipRect.W - clipOffset.Y) * clipScale.Y;
                if (clipMaxX <= clipMinX || clipMaxY <= clipMinY)
                {
                    continue;
                }

                _gl.Scissor(
                    (int)clipMinX,
                    (int)(framebufferHeight - clipMaxY),
                    (uint)(clipMaxX - clipMinX),
                    (uint)(clipMaxY - clipMinY));
                _gl.BindTexture(TextureTarget.Texture2D, (uint)command->GetTexID().Handle);
                void* indexOffset = (void*)(nuint)(command->IdxOffset * sizeof(ushort));
                if (_supportsVertexOffset)
                {
                    _gl.DrawElementsBaseVertex(
                        PrimitiveType.Triangles,
                        command->ElemCount,
                        DrawElementsType.UnsignedShort,
                        indexOffset,
                        (int)command->VtxOffset);
                }
                else
                {
                    if (command->VtxOffset != 0)
                    {
                        throw new InvalidOperationException("当前 GLES renderer 未声明 VtxOffset，但 draw command 包含非零 VtxOffset。");
                    }

                    _gl.DrawElements(
                        PrimitiveType.Triangles,
                        command->ElemCount,
                        DrawElementsType.UnsignedShort,
                        indexOffset);
                }
            }
        }
    }

    private void SetupRenderState(ImDrawDataPtr drawData, int framebufferWidth, int framebufferHeight)
    {
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);

        float left = drawData.DisplayPos.X;
        float right = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float top = drawData.DisplayPos.Y;
        float bottom = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        Span<float> projection =
        [
            2f / (right - left), 0f, 0f, 0f,
            0f, 2f / (top - bottom), 0f, 0f,
            0f, 0f, -1f, 0f,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0f, 1f,
        ];

        _program!.Use();
        _gl.Uniform1(_textureLocation, 0);
        fixed (float* projectionPointer = projection)
        {
            _gl.UniformMatrix4(_projectionLocation, 1, false, projectionPointer);
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindSampler(0, 0);
        _gl.BindVertexArray(_vertexArray);
        _vertexBuffer!.Bind();
        _indexBuffer!.Bind();
        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), null);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);
    }

    private void ProcessTextureRequests(ImVector<ImTextureDataPtr>* textures, bool forceDestroy)
    {
        if (textures is null)
        {
            return;
        }

        ImTextureDataPtr* items = textures->Data;
        for (int i = 0; i < textures->Size; i++)
        {
            ImTextureDataPtr texture = items[i];
            if (texture.IsNull)
            {
                continue;
            }

            if (forceDestroy)
            {
                DestroyTexture(texture);
            }
            else if (texture.Status != ImTextureStatus.Ok)
            {
                UpdateTexture(texture);
            }
        }
    }

    private void UpdateTexture(ImTextureDataPtr texture)
    {
        if (texture.Status is ImTextureStatus.WantCreate or ImTextureStatus.WantUpdates)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            _gl.PixelStore(GLEnum.UnpackRowLength, 0);
            _gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
            _gl.PixelStore(GLEnum.UnpackSkipRows, 0);
        }

        if (texture.Status == ImTextureStatus.WantCreate)
        {
            if (!texture.TexID.IsNull || texture.Format != ImTextureFormat.Rgba32)
            {
                throw new InvalidOperationException("ImGui texture create 请求必须使用空 TexID 与 RGBA32 格式。");
            }

            uint handle = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, handle);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)texture.Width,
                (uint)texture.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                texture.Pixels);
            texture.SetTexID((ImTextureID)(ulong)handle);
            texture.SetStatus(ImTextureStatus.Ok);
            return;
        }

        if (texture.Status == ImTextureStatus.WantUpdates)
        {
            _gl.BindTexture(TextureTarget.Texture2D, (uint)texture.TexID.Handle);
            ImVector<ImTextureRect> updates = texture.Updates;
            for (int updateIndex = 0; updateIndex < updates.Size; updateIndex++)
            {
                ImTextureRect update = updates.Data[updateIndex];
                for (int row = 0; row < update.H; row++)
                {
                    _gl.TexSubImage2D(
                        TextureTarget.Texture2D,
                        0,
                        update.X,
                        update.Y + row,
                        update.W,
                        1,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        texture.GetPixelsAt(update.X, update.Y + row));
                }
            }

            texture.SetStatus(ImTextureStatus.Ok);
            return;
        }

        if (texture.Status == ImTextureStatus.WantDestroy && texture.UnusedFrames > 0)
        {
            DestroyTexture(texture);
        }
    }

    private void DestroyTexture(ImTextureDataPtr texture)
    {
        uint handle = (uint)texture.TexID.Handle;
        if (handle != 0)
        {
            _gl.DeleteTexture(handle);
        }

        texture.BackendUserData = null;
        texture.SetTexID(ImTextureID.Null);
        texture.SetStatus(ImTextureStatus.Destroyed);
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("ImGui GL renderer 尚未初始化。");
        }
    }

    private static bool IsAtLeast(GlCapabilities capabilities, int major, int minor)
    {
        return capabilities.MajorVersion > major ||
            (capabilities.MajorVersion == major && capabilities.MinorVersion >= minor);
    }

    private static string VertexShader(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 UV;
layout(location = 2) in vec4 Color;

uniform mat4 ProjMtx;
out vec2 Frag_UV;
out vec4 Frag_Color;

void main()
{
    Frag_UV = UV;
    Frag_Color = Color;
    gl_Position = ProjMtx * vec4(Position.xy, 0.0, 1.0);
}
""";
    }

    private static string FragmentShader(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 Frag_UV;
in vec4 Frag_Color;
uniform sampler2D Texture;
layout(location = 0) out vec4 Out_Color;

void main()
{
    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
}
""";
    }
}
