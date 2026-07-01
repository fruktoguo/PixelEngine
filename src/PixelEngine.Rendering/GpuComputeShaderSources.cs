namespace PixelEngine.Rendering;

/// <summary>
/// plan/09 §4.3/§4.4 首批 GPU compute pass 的 GLSL 4.30 源码入口。
/// </summary>
/// <remarks>
/// 这些源码只覆盖 bloom 与光照合成的渲染侧 compute pass。CPU sim 仍是权威数据源，
/// compute pass 不执行权威模拟，也不要求 GPU 到 CPU 的读回。
/// </remarks>
public static class GpuComputeShaderSources
{
    /// <summary>
    /// bloom bright-pass compute pass 名称。
    /// </summary>
    public const string BloomBrightPassName = "bloom_brightpass";

    /// <summary>
    /// bloom downsample compute pass 名称。
    /// </summary>
    public const string BloomDownsampleName = "bloom_downsample";

    /// <summary>
    /// dual-Kawase bloom down compute pass 名称。
    /// </summary>
    public const string BloomDualKawaseDownName = "bloom_dualkawase_down";

    /// <summary>
    /// dual-Kawase bloom up compute pass 名称。
    /// </summary>
    public const string BloomDualKawaseUpName = "bloom_dualkawase_up";

    /// <summary>
    /// bloom upsample composite compute pass 名称。
    /// </summary>
    public const string BloomUpsampleCompositeName = "bloom_upsample_composite";

    /// <summary>
    /// light composite compute pass 名称。
    /// </summary>
    public const string LightCompositeName = "light_composite";

    /// <summary>
    /// 已注册的 compute pass 名称集合。
    /// </summary>
    public static IReadOnlyList<string> PassNames { get; } =
    [
        BloomBrightPassName,
        BloomDownsampleName,
        BloomDualKawaseDownName,
        BloomDualKawaseUpName,
        BloomUpsampleCompositeName,
        LightCompositeName,
    ];

    /// <summary>
    /// bloom bright-pass compute shader，输出超过阈值的高亮颜色。
    /// </summary>
    public const string BloomBrightPass = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSourceTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform float uThreshold;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec4 color = texture(uSourceTexture, uv);
    float luminance = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    float factor = smoothstep(uThreshold, uThreshold + 0.2, luminance);
    imageStore(uOutputImage, pixel, vec4(color.rgb * factor, color.a));
}
""";

    /// <summary>
    /// bloom downsample compute shader，按 2×2 footprint 生成下一层 mip-like 纹理。
    /// </summary>
    public const string BloomDownsample = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSourceTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform vec2 uSourceTexelSize;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec2 x = vec2(uSourceTexelSize.x, 0.0);
    vec2 y = vec2(0.0, uSourceTexelSize.y);
    vec3 color = texture(uSourceTexture, uv).rgb * 0.125;
    color += texture(uSourceTexture, uv - x).rgb * 0.0625;
    color += texture(uSourceTexture, uv + x).rgb * 0.0625;
    color += texture(uSourceTexture, uv - y).rgb * 0.0625;
    color += texture(uSourceTexture, uv + y).rgb * 0.0625;
    color += texture(uSourceTexture, uv - x - y).rgb * 0.125;
    color += texture(uSourceTexture, uv + x - y).rgb * 0.125;
    color += texture(uSourceTexture, uv - x + y).rgb * 0.125;
    color += texture(uSourceTexture, uv + x + y).rgb * 0.125;
    color += texture(uSourceTexture, uv - (x * 2.0)).rgb * 0.03125;
    color += texture(uSourceTexture, uv + (x * 2.0)).rgb * 0.03125;
    color += texture(uSourceTexture, uv - (y * 2.0)).rgb * 0.03125;
    color += texture(uSourceTexture, uv + (y * 2.0)).rgb * 0.03125;
    imageStore(uOutputImage, pixel, vec4(color, 1.0));
}
""";

    /// <summary>
    /// dual-Kawase bloom down compute shader，使用后端绑定点与 uniform 驱动采样半径。
    /// </summary>
    public const string BloomDualKawaseDown = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSourceTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform vec2 uTexelSize;
uniform float uOffset;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec2 offset = uTexelSize * uOffset;
    vec3 color = texture(uSourceTexture, uv).rgb * 4.0;
    color += texture(uSourceTexture, uv + vec2(-offset.x, -offset.y)).rgb;
    color += texture(uSourceTexture, uv + vec2(offset.x, -offset.y)).rgb;
    color += texture(uSourceTexture, uv + vec2(-offset.x, offset.y)).rgb;
    color += texture(uSourceTexture, uv + vec2(offset.x, offset.y)).rgb;
    imageStore(uOutputImage, pixel, vec4(color * 0.125, 1.0));
}
""";

    /// <summary>
    /// dual-Kawase bloom up compute shader，使用上采样半径与强度控制合成结果。
    /// </summary>
    public const string BloomDualKawaseUp = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSourceTexture;
layout(binding = 1) uniform sampler2D uBaseTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform vec2 uTexelSize;
uniform float uOffset;
uniform float uIntensity;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec2 offset = uTexelSize * uOffset;
    vec3 base = texture(uBaseTexture, uv).rgb;
    vec3 color = texture(uSourceTexture, uv + vec2(-offset.x, 0.0)).rgb;
    color += texture(uSourceTexture, uv + vec2(offset.x, 0.0)).rgb;
    color += texture(uSourceTexture, uv + vec2(0.0, -offset.y)).rgb;
    color += texture(uSourceTexture, uv + vec2(0.0, offset.y)).rgb;
    imageStore(uOutputImage, pixel, vec4(clamp(base + (color * 0.25 * uIntensity), 0.0, 1.0), 1.0));
}
""";

    /// <summary>
    /// bloom upsample composite compute shader，将 scene 与 bloom 纹理合成到输出 image。
    /// </summary>
    public const string BloomUpsampleComposite = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSceneTexture;
layout(binding = 1) uniform sampler2D uBloomTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform float uBloomIntensity;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec4 scene = texture(uSceneTexture, uv);
    vec3 bloom = texture(uBloomTexture, uv).rgb * uBloomIntensity;
    imageStore(uOutputImage, pixel, vec4(clamp(scene.rgb + bloom, 0.0, 1.0), scene.a));
}
""";

    /// <summary>
    /// light composite compute shader，将世界色、光照与自发光合成到输出 image。
    /// </summary>
    public const string LightComposite = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uWorldTexture;
layout(binding = 1) uniform sampler2D uLightTexture;
layout(binding = 2) uniform sampler2D uEmissiveTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform float uExposure;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec4 world = texture(uWorldTexture, uv);
    vec3 lighting = texture(uLightTexture, uv).rgb;
    vec3 emissive = texture(uEmissiveTexture, uv).rgb;
    vec3 color = (world.rgb * lighting * uExposure) + emissive;
    imageStore(uOutputImage, pixel, vec4(clamp(color, 0.0, 1.0), world.a));
}
""";

    /// <summary>
    /// 按 pass 名称获取 compute shader 源码。
    /// </summary>
    /// <param name="passName">compute pass 名称。</param>
    /// <returns>shader 源码。</returns>
    /// <exception cref="ArgumentException">pass 名称未注册。</exception>
    public static string GetSource(string passName)
    {
        return passName switch
        {
            BloomBrightPassName => BloomBrightPass,
            BloomDownsampleName => BloomDownsample,
            BloomDualKawaseDownName => BloomDualKawaseDown,
            BloomDualKawaseUpName => BloomDualKawaseUp,
            BloomUpsampleCompositeName => BloomUpsampleComposite,
            LightCompositeName => LightComposite,
            _ => throw new ArgumentException($"未注册的 GPU compute shader pass：{passName}", nameof(passName)),
        };
    }
}
