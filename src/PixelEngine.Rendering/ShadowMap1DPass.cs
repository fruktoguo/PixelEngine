using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 每光源 1D shadow map 硬阴影 pass。shadow map 存为 R8、宽度为 ray count、高度为 1。
/// </summary>
public sealed class ShadowMap1DPass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _occluderLocation;
    private readonly int _lightPositionLocation;
    private readonly int _lightRadiusLocation;
    private readonly int _thresholdLocation;
    private readonly int _rayCountLocation;
    private readonly int _stepCountLocation;
    private Framebuffer _framebuffer;
    private bool _disposed;

    /// <summary>
    /// 创建 1D shadow map pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    /// <param name="rayCount">极角采样条数。</param>
    public ShadowMap1DPass(GL gl, GlslProfile profile, int rayCount)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (rayCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rayCount), "shadow map ray count 必须为正数。");
        }

        _gl = gl;
        _program = ShaderProgram.Create(
            gl,
            LightingShaderSources.FullscreenVertex(profile),
            LightingShaderSources.Shadow1DFragment(profile));
        _occluderLocation = _program.GetUniformLocation("uOccluderTexture");
        _lightPositionLocation = _program.GetUniformLocation("uLightPosition");
        _lightRadiusLocation = _program.GetUniformLocation("uLightRadius");
        _thresholdLocation = _program.GetUniformLocation("uOccluderThreshold");
        _rayCountLocation = _program.GetUniformLocation("uRayCount");
        _stepCountLocation = _program.GetUniformLocation("uStepCount");
        ShadowMap = new LightMaskTexture(gl, rayCount, 1);
        _framebuffer = CreateFramebuffer(gl, ShadowMap);
    }

    /// <summary>
    /// shadow map 纹理。
    /// </summary>
    public LightMaskTexture ShadowMap { get; private set; }

    /// <summary>
    /// ray count。
    /// </summary>
    public int RayCount => ShadowMap.Width;

    /// <summary>
    /// 调整 shadow map ray count。
    /// </summary>
    /// <param name="rayCount">新的极角采样条数。</param>
    public void Resize(int rayCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (rayCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rayCount), "shadow map ray count 必须为正数。");
        }

        if (rayCount == RayCount)
        {
            return;
        }

        LightMaskTexture nextMap = new(_gl, rayCount, 1);
        Framebuffer nextFramebuffer = CreateFramebuffer(_gl, nextMap);
        _framebuffer.Dispose();
        ShadowMap.Dispose();
        ShadowMap = nextMap;
        _framebuffer = nextFramebuffer;
    }

    /// <summary>
    /// 在 GPU 上生成单个光源的 1D shadow map。调用者负责在后续 pass 设置目标 viewport。
    /// </summary>
    /// <param name="occluder">R8 遮挡图，红通道 0 表示透明、1 表示遮挡。</param>
    /// <param name="light">光源。</param>
    /// <param name="quad">全屏三角形。</param>
    /// <param name="stepCount">ray marching 步数；0 表示按光源半径向上取整。</param>
    /// <param name="occluderThreshold">遮挡阈值，0..1。</param>
    public void Render(
        LightMaskTexture occluder,
        LightSource light,
        FullscreenQuad quad,
        int stepCount = 0,
        float occluderThreshold = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(occluder);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        light.Validate();
        if (stepCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepCount), "shadow map 步数不能为负数。");
        }

        int effectiveStepCount = stepCount == 0 ? Math.Max(1, (int)MathF.Ceiling(light.Radius)) : stepCount;
        _framebuffer.Bind();
        _gl.Viewport(0, 0, (uint)RayCount, 1);
        _program.Use();
        occluder.Bind(0);
        _gl.Uniform1(_occluderLocation, 0);
        _gl.Uniform2(_lightPositionLocation, light.X, light.Y);
        _gl.Uniform1(_lightRadiusLocation, light.Radius);
        _gl.Uniform1(_thresholdLocation, Math.Clamp(occluderThreshold, 0f, 1f));
        _gl.Uniform1(_rayCountLocation, RayCount);
        _gl.Uniform1(_stepCountLocation, effectiveStepCount);
        quad.Draw();
    }

    /// <summary>
    /// CPU fallback/测试 oracle：计算每条 ray 的最近遮挡距离，单位为像素。
    /// </summary>
    /// <param name="occluder">遮挡图，0 透明、非零可遮挡。</param>
    /// <param name="width">遮挡图宽度。</param>
    /// <param name="height">遮挡图高度。</param>
    /// <param name="light">光源。</param>
    /// <param name="distances">输出距离，长度即 ray count。</param>
    /// <param name="threshold">遮挡阈值。</param>
    public static void ComputeCpu(
        ReadOnlySpan<byte> occluder,
        int width,
        int height,
        LightSource light,
        Span<float> distances,
        byte threshold = 128)
    {
        ValidateOccluder(occluder, width, height);
        light.Validate();
        if (distances.IsEmpty)
        {
            throw new ArgumentException("shadow map 输出距离不能为空。", nameof(distances));
        }

        int stepCount = Math.Max(1, (int)MathF.Ceiling(light.Radius));
        for (int ray = 0; ray < distances.Length; ray++)
        {
            float angle = (ray + 0.5f) * 2f * MathF.PI / distances.Length;
            float dx = MathF.Cos(angle);
            float dy = MathF.Sin(angle);
            float hitDistance = light.Radius;
            for (int step = 1; step <= stepCount; step++)
            {
                float distance = (float)step / stepCount * light.Radius;
                int x = (int)MathF.Floor(light.X + (dx * distance));
                int y = (int)MathF.Floor(light.Y + (dy * distance));
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                {
                    break;
                }

                if (occluder[(y * width) + x] >= threshold)
                {
                    hitDistance = distance;
                    break;
                }
            }

            distances[ray] = hitDistance;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _framebuffer.Dispose();
        ShadowMap.Dispose();
        _program.Dispose();
        _disposed = true;
    }

    private static Framebuffer CreateFramebuffer(GL gl, LightMaskTexture shadowMap)
    {
        Framebuffer framebuffer = new(gl);
        try
        {
            framebuffer.AttachColorTexture(shadowMap.Texture);
            return framebuffer;
        }
        catch
        {
            framebuffer.Dispose();
            throw;
        }
    }

    private static void ValidateOccluder(ReadOnlySpan<byte> occluder, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "遮挡图尺寸必须为正数。");
        }

        int expectedLength = checked(width * height);
        if (occluder.Length != expectedLength)
        {
            throw new ArgumentException("遮挡图长度必须等于宽高乘积。", nameof(occluder));
        }
    }
}
