using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 将视口大小的 authored sRGB 世界纹理解码后，以 nearest 采样 blit 到 linear 内部颜色目标。
/// </summary>
public sealed class WorldBlitPass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _worldLocation;
    private bool _disposed;

    /// <summary>
    /// 创建 world blit pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public WorldBlitPass(GL gl, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _program = ShaderProgram.Create(gl, LightingShaderSources.FullscreenVertex(profile), Fragment(profile));
        _worldLocation = _program.GetUniformLocation("uWorldTexture");
    }

    /// <summary>
    /// 绘制世界纹理。
    /// </summary>
    /// <param name="world">世界纹理。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="camera">只读相机快照；用于校验视口尺寸，本 pass 不持有相机权威。</param>
    /// <param name="quad">全屏三角形。</param>
    public void Render(WorldTexture world, ColorRenderTarget destination, CameraState camera, FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (camera.ViewportWidth != destination.Width || camera.ViewportHeight != destination.Height)
        {
            throw new ArgumentException("Camera viewport 必须与 world blit 输出尺寸一致。", nameof(camera));
        }

        // 世界纹理 nearest 采样到 scene FBO，Y 轴在 fragment 中翻转以匹配 CPU 上传朝向。
        destination.BindFramebuffer();
        _gl.Viewport(0, 0, (uint)destination.Width, (uint)destination.Height);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        _program.Use();
        world.Bind(0);
        _gl.Uniform1(_worldLocation, 0);
        quad.Draw();
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

    private static string Fragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vUv;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uWorldTexture;

vec3 AuthoredSrgbToLinear(vec3 color)
{
    return pow(max(color, vec3(0.0)), vec3(2.2));
}

void main()
{
    vec4 authoredColor = texture(uWorldTexture, vec2(vUv.x, 1.0 - vUv.y));
    fragColor = vec4(AuthoredSrgbToLinear(authoredColor.rgb), authoredColor.a);
}
""";
    }
}
