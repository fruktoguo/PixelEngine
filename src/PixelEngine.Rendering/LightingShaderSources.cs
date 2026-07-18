namespace PixelEngine.Rendering;

/// <summary>
/// 相位 10 光照/合成 shader 源码生成器。
/// </summary>
public static class LightingShaderSources
{
    /// <summary>
    /// 全屏三角形通用 vertex shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string FullscreenVertex(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
layout(location = 0) in vec2 aPosition;
out vec2 vUv;

void main()
{
    vUv = (aPosition * 0.5) + 0.5;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
""";
    }

    /// <summary>
    /// 世界纹理、emissive 与可见性遮罩的保色 composite fragment shader。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string CompositeFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vUv;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uWorldTexture;
uniform sampler2D uEmissiveTexture;
uniform sampler2D uVisibilityTexture;
uniform int uDecodeWorldSrgb;

vec3 AuthoredSrgbToLinear(vec3 color)
{
    return pow(max(color, vec3(0.0)), vec3(2.2));
}

void main()
{
    vec4 worldColor = texture(uWorldTexture, vUv);
    vec3 worldLinear = uDecodeWorldSrgb != 0
        ? AuthoredSrgbToLinear(worldColor.rgb)
        : worldColor.rgb;
    vec2 cpuUv = vec2(vUv.x, 1.0 - vUv.y);
    vec3 emissive = AuthoredSrgbToLinear(texture(uEmissiveTexture, cpuUv).rgb);
    float visibility = texture(uVisibilityTexture, cpuUv).r;
    vec3 litWorld = worldLinear * visibility;
    vec3 color = max(litWorld, emissive);
    fragColor = vec4(clamp(color, 0.0, 1.0), worldColor.a);
}
""";
    }

    /// <summary>
    /// 每光源 1D shadow map fragment shader。输出红通道为最近遮挡距离 / 半径。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string Shadow1DFragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vUv;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uOccluderTexture;
uniform vec2 uLightPosition;
uniform float uLightRadius;
uniform float uOccluderThreshold;
uniform int uRayCount;
uniform int uStepCount;

void main()
{
    float ray = floor(vUv.x * float(uRayCount));
    float angle = (ray + 0.5) * 6.28318530718 / float(uRayCount);
    vec2 direction = vec2(cos(angle), sin(angle));
    vec2 textureSizeValue = vec2(textureSize(uOccluderTexture, 0));
    float hitDistance = uLightRadius;

    for (int stepIndex = 1; stepIndex <= uStepCount; stepIndex++)
    {
        float distanceValue = (float(stepIndex) / float(uStepCount)) * uLightRadius;
        vec2 samplePixel = uLightPosition + (direction * distanceValue);
        if (samplePixel.x < 0.0 || samplePixel.y < 0.0 ||
            samplePixel.x >= textureSizeValue.x || samplePixel.y >= textureSizeValue.y)
        {
            break;
        }

        vec2 sampleUv = (samplePixel + vec2(0.5)) / textureSizeValue;
        float occluder = texture(uOccluderTexture, sampleUv).r;
        if (occluder >= uOccluderThreshold)
        {
            hitDistance = distanceValue;
            break;
        }
    }

    float normalizedDistance = clamp(hitDistance / max(uLightRadius, 0.0001), 0.0, 1.0);
    fragColor = vec4(normalizedDistance, 0.0, 0.0, 1.0);
}
""";
    }
}
