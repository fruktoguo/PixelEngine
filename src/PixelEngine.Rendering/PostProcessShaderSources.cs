namespace PixelEngine.Rendering;

/// <summary>
/// Bloom、dither、gamma 与 CRT 后处理 shader 源码生成器。
/// </summary>
public static class PostProcessShaderSources
{
    /// <summary>
    /// bright-pass fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string BrightPassFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform float uThreshold;

void main()
{
    vec4 color = texture(uSourceTexture, vUv);
    float luminance = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    float factor = smoothstep(uThreshold, uThreshold + 0.2, luminance);
    fragColor = vec4(color.rgb * factor, color.a);
}
""";
    }

    /// <summary>
    /// dual-Kawase downsample fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string KawaseDownFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform vec2 uTexelSize;
uniform float uOffset;

void main()
{
    vec2 offset = uTexelSize * uOffset;
    vec3 color = texture(uSourceTexture, vUv).rgb * 4.0;
    color += texture(uSourceTexture, vUv + vec2(-offset.x, -offset.y)).rgb;
    color += texture(uSourceTexture, vUv + vec2( offset.x, -offset.y)).rgb;
    color += texture(uSourceTexture, vUv + vec2(-offset.x,  offset.y)).rgb;
    color += texture(uSourceTexture, vUv + vec2( offset.x,  offset.y)).rgb;
    fragColor = vec4(color * 0.125, 1.0);
}
""";
    }

    /// <summary>
    /// dual-Kawase upsample/additive fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string KawaseUpFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform vec2 uTexelSize;
uniform float uOffset;
uniform float uIntensity;

void main()
{
    vec2 offset = uTexelSize * uOffset;
    vec3 color = texture(uSourceTexture, vUv + vec2(-offset.x, 0.0)).rgb;
    color += texture(uSourceTexture, vUv + vec2( offset.x, 0.0)).rgb;
    color += texture(uSourceTexture, vUv + vec2(0.0, -offset.y)).rgb;
    color += texture(uSourceTexture, vUv + vec2(0.0,  offset.y)).rgb;
    fragColor = vec4(color * 0.25 * uIntensity, 1.0);
}
""";
    }

    /// <summary>
    /// separable Gaussian blur fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string GaussianBlurFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform vec2 uDirection;
uniform vec2 uTexelSize;

void main()
{
    vec2 stepUv = uDirection * uTexelSize;
    vec3 color = texture(uSourceTexture, vUv).rgb * 0.227027;
    color += texture(uSourceTexture, vUv + stepUv * 1.384615).rgb * 0.316216;
    color += texture(uSourceTexture, vUv - stepUv * 1.384615).rgb * 0.316216;
    color += texture(uSourceTexture, vUv + stepUv * 3.230769).rgb * 0.070270;
    color += texture(uSourceTexture, vUv - stepUv * 3.230769).rgb * 0.070270;
    fragColor = vec4(color, 1.0);
}
""";
    }

    /// <summary>
    /// bloom additive composite fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string BloomCompositeFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vUv;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uSceneTexture;
uniform sampler2D uBloomTexture;
uniform float uIntensity;

void main()
{
    vec4 scene = texture(uSceneTexture, vUv);
    vec3 bloom = texture(uBloomTexture, vUv).rgb * uIntensity;
    fragColor = vec4(clamp(scene.rgb + bloom, 0.0, 1.0), scene.a);
}
""";
    }

    /// <summary>
    /// 有序 Bayer dither fragment shader，在 gamma 前使用。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string DitherFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform float uStrength;

float Bayer4(vec2 pixel)
{
    int x = int(mod(pixel.x, 4.0));
    int y = int(mod(pixel.y, 4.0));
    int index = x + y * 4;
    float values[16] = float[16](
        0.0,  8.0,  2.0, 10.0,
       12.0,  4.0, 14.0,  6.0,
        3.0, 11.0,  1.0,  9.0,
       15.0,  7.0, 13.0,  5.0);
    return (values[index] / 16.0) - 0.5;
}

void main()
{
    vec4 color = texture(uSourceTexture, vUv);
    float noise = Bayer4(gl_FragCoord.xy) * uStrength;
    fragColor = vec4(clamp(color.rgb + noise, 0.0, 1.0), color.a);
}
""";
    }

    /// <summary>
    /// 线性到 sRGB gamma 校正 fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string GammaFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform float uGamma;

void main()
{
    vec4 color = texture(uSourceTexture, vUv);
    vec3 corrected = pow(max(color.rgb, vec3(0.0)), vec3(1.0 / max(uGamma, 0.0001)));
    fragColor = vec4(corrected, color.a);
}
""";
    }

    /// <summary>
    /// 可选 CRT scanline fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string CrtFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + SamplerFragmentHeader() + """
uniform float uScanlineStrength;
uniform float uCurvature;

void main()
{
    vec2 centered = (vUv * 2.0) - 1.0;
    vec2 curved = centered * (1.0 + uCurvature * dot(centered, centered));
    vec2 uv = (curved * 0.5) + 0.5;
    vec4 color = texture(uSourceTexture, uv);
    float scanline = 1.0 - (uScanlineStrength * step(0.5, fract(gl_FragCoord.y * 0.5)));
    float mask = float(all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0))));
    fragColor = vec4(color.rgb * scanline * mask, color.a * mask);
}
""";
    }

    private static string SamplerFragmentHeader()
    {
        return """
in vec2 vUv;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uSourceTexture;

""";
    }
}
