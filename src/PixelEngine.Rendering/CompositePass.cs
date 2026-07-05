using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 世界纹理、emissive 与可见性遮罩的 additive composite pass。
/// </summary>
public sealed class CompositePass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _worldLocation;
    private readonly int _emissiveLocation;
    private readonly int _visibilityLocation;
    private bool _disposed;

    /// <summary>
    /// 创建 composite pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public CompositePass(GL gl, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _program = ShaderProgram.Create(
            gl,
            LightingShaderSources.FullscreenVertex(profile),
            LightingShaderSources.CompositeFragment(profile));
        _worldLocation = _program.GetUniformLocation("uWorldTexture");
        _emissiveLocation = _program.GetUniformLocation("uEmissiveTexture");
        _visibilityLocation = _program.GetUniformLocation("uVisibilityTexture");
    }

    /// <summary>
    /// 绘制 composite。输出 framebuffer 与 viewport 由调用者在进入 pass 前绑定。
    /// </summary>
    /// <param name="world">世界纹理。</param>
    /// <param name="emissive">emissive additive buffer。</param>
    /// <param name="visibility">可见性遮罩，红通道 0..1。</param>
    /// <param name="quad">全屏三角形。</param>
    public void Render(WorldTexture world, EmissiveBuffer emissive, LightMaskTexture visibility, FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(emissive);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Begin(emissive, visibility);
        world.Bind(0);
        quad.Draw();
    }

    /// <summary>
    /// 绘制 scene + emissive + visibility composite。输出 framebuffer 与 viewport 由调用者在进入 pass 前绑定。
    /// </summary>
    /// <param name="scene">world blit 与 overlay 后的 scene。</param>
    /// <param name="emissive">emissive additive buffer。</param>
    /// <param name="visibility">可见性遮罩，红通道 0..1。</param>
    /// <param name="quad">全屏三角形。</param>
    public void Render(ColorRenderTarget scene, EmissiveBuffer emissive, LightMaskTexture visibility, FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(emissive);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Begin(emissive, visibility);
        scene.BindTexture(0);
        quad.Draw();
    }

    private void Begin(EmissiveBuffer emissive, LightMaskTexture visibility)
    {
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        _program.Use();
        emissive.BindTexture(1);
        visibility.Bind(2);
        _gl.Uniform1(_worldLocation, 0);
        _gl.Uniform1(_emissiveLocation, 1);
        _gl.Uniform1(_visibilityLocation, 2);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _program.Dispose();
        _disposed = true;
    }
}
