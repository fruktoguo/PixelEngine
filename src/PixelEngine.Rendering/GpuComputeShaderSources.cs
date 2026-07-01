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
    /// Radiance Cascades SDF Jump Flood compute pass 名称。
    /// </summary>
    public const string RadianceCascadeSdfJfaName = "rc_sdf_jfa";

    /// <summary>
    /// Radiance Cascades cascade build compute pass 名称。
    /// </summary>
    public const string RadianceCascadeBuildName = "rc_cascade_build";

    /// <summary>
    /// Radiance Cascades merge compute pass 名称。
    /// </summary>
    public const string RadianceCascadeMergeName = "rc_merge";

    /// <summary>
    /// Radiance Cascades apply compute pass 名称。
    /// </summary>
    public const string RadianceCascadeApplyName = "rc_apply";

    /// <summary>
    /// 非权威 air/smoke Margolus 扩散 compute pass 名称。
    /// </summary>
    public const string AirSmokeDiffuseMargolusName = "air_diffuse_margolus";

    /// <summary>
    /// 非权威 air/smoke Margolus 扩散 compute pass 名称兼容别名。
    /// </summary>
    public const string AirDiffuseMargolusName = AirSmokeDiffuseMargolusName;

    /// <summary>
    /// GPU 粒子 point-sprite 顶点 shader 文件名。
    /// </summary>
    public const string ParticlePointSpriteVertexName = "particle_pointsprite.vert";

    /// <summary>
    /// GPU 粒子 point-sprite 片元 shader 文件名。
    /// </summary>
    public const string ParticlePointSpriteFragmentName = "particle_pointsprite.frag";

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
        RadianceCascadeSdfJfaName,
        RadianceCascadeBuildName,
        RadianceCascadeMergeName,
        RadianceCascadeApplyName,
        AirSmokeDiffuseMargolusName,
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
    /// Radiance Cascades SDF Jump Flood compute shader 入口，生成渲染侧 SDF image。
    /// </summary>
    public const string RadianceCascadeSdfJfa = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSeedTexture;
layout(rgba32f, binding = 0) writeonly uniform image2D uSdfImage;

uniform ivec2 uOutputSize;
uniform int uJumpStep;
uniform int uInitialize;
uniform float uOccluderThreshold;

void main()
{
    // Render-side image output only; this pass has no GPU->CPU readback semantics.
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    if (uInitialize != 0)
    {
        float occluder = texture(uSeedTexture, uv).r;
        vec2 seed = occluder >= uOccluderThreshold ? vec2(pixel) : vec2(-1.0);
        float distance = occluder >= uOccluderThreshold ? 0.0 : 3.402823e+38;
        imageStore(uSdfImage, pixel, vec4(seed, distance, occluder >= uOccluderThreshold ? 1.0 : 0.0));
        return;
    }

    vec2 texel = 1.0 / vec2(uOutputSize);
    vec4 best = texture(uSeedTexture, uv);
    float bestDistance = best.w > 0.0 ? length(vec2(pixel) - best.xy) : 3.402823e+38;
    for (int oy = -1; oy <= 1; oy++)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            vec2 sampleUv = uv + vec2(ox, oy) * texel * float(uJumpStep);
            if (any(lessThan(sampleUv, vec2(0.0))) || any(greaterThan(sampleUv, vec2(1.0))))
            {
                continue;
            }

            vec4 candidate = texture(uSeedTexture, sampleUv);
            if (candidate.w <= 0.0)
            {
                continue;
            }

            float distance = length(vec2(pixel) - candidate.xy);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
    }

    imageStore(uSdfImage, pixel, vec4(best.xy, bestDistance, best.w));
}
""";

    /// <summary>
    /// Radiance Cascades cascade build compute shader 入口，构建单级 radiance cascade image。
    /// </summary>
    public const string RadianceCascadeBuild = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSdfTexture;
layout(binding = 1) uniform sampler2D uEmissiveTexture;
layout(rgba16f, binding = 0) writeonly uniform image2D uCascadeImage;

uniform ivec2 uOutputSize;
uniform int uCascadeIndex;
uniform int uRayCount;
uniform float uCascadeRadius;
uniform int uMaxRaySteps;

void main()
{
    // Render-side image output only; this pass has no GPU->CPU readback semantics.
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec3 radiance = vec3(0.0);
    int rayCount = max(uRayCount, 1);
    int maxSteps = max(uMaxRaySteps, 1);
    float stepLength = max(uCascadeRadius / float(maxSteps), 1.0);
    for (int ray = 0; ray < rayCount; ray++)
    {
        float angle = (6.28318530718 * (float(ray) + 0.5)) / float(rayCount);
        vec2 direction = vec2(cos(angle), sin(angle));
        vec3 rayRadiance = vec3(0.0);
        float transmittance = 1.0;
        for (int stepIndex = 1; stepIndex <= maxSteps; stepIndex++)
        {
            vec2 samplePixel = vec2(pixel) + direction * stepLength * float(stepIndex);
            vec2 sampleUv = (samplePixel + vec2(0.5)) / vec2(uOutputSize);
            if (any(lessThan(sampleUv, vec2(0.0))) || any(greaterThan(sampleUv, vec2(1.0))))
            {
                break;
            }

            vec4 sdf = texture(uSdfTexture, sampleUv);
            if (sdf.w > 0.0 && sdf.z <= 1.0)
            {
                break;
            }

            vec3 emissive = texture(uEmissiveTexture, sampleUv).rgb;
            rayRadiance += emissive * transmittance;
            transmittance *= 0.92;
        }

        radiance += rayRadiance / float(maxSteps);
    }

    float cascadeWeight = 1.0 / float(uCascadeIndex + 1);
    imageStore(uCascadeImage, pixel, vec4(radiance * cascadeWeight / float(rayCount), 1.0));
}
""";

    /// <summary>
    /// Radiance Cascades merge compute shader 入口，合并相邻 cascade image。
    /// </summary>
    public const string RadianceCascadeMerge = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uNearCascadeTexture;
layout(binding = 1) uniform sampler2D uFarCascadeTexture;
layout(rgba16f, binding = 0) writeonly uniform image2D uMergedCascadeImage;

uniform ivec2 uOutputSize;
uniform int uCascadeIndex;
uniform float uMergeFactor;

void main()
{
    // Render-side image output only; this pass has no GPU->CPU readback semantics.
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec4 nearRadiance = texture(uNearCascadeTexture, uv);
    vec4 farRadiance = texture(uFarCascadeTexture, uv);
    float factor = clamp(uMergeFactor + (float(uCascadeIndex) * 0.0), 0.0, 1.0);
    imageStore(uMergedCascadeImage, pixel, mix(nearRadiance, farRadiance, factor));
}
""";

    /// <summary>
    /// Radiance Cascades apply compute shader 入口，把合并后的 radiance 写入渲染输出 image。
    /// </summary>
    public const string RadianceCascadeApply = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D uSceneTexture;
layout(binding = 1) uniform sampler2D uRadianceTexture;
layout(rgba8, binding = 0) writeonly uniform image2D uOutputImage;

uniform ivec2 uOutputSize;
uniform float uRadianceIntensity;

void main()
{
    // Render-side image output only; this pass has no GPU->CPU readback semantics.
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(pixel, uOutputSize)))
    {
        return;
    }

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(uOutputSize);
    vec4 scene = texture(uSceneTexture, uv);
    vec3 radiance = texture(uRadianceTexture, uv).rgb * uRadianceIntensity;
    imageStore(uOutputImage, pixel, vec4(clamp(scene.rgb + radiance, 0.0, 1.0), scene.a));
}
""";

    /// <summary>
    /// 非权威 air/smoke Margolus 2×2 扩散 compute shader，维护独立密度纹理。
    /// </summary>
    public const string AirSmokeDiffuseMargolus = """
#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(r16f, binding = 0) readonly uniform image2D uAirSmokeDensityInput;
layout(r16f, binding = 1) writeonly uniform image2D uAirSmokeDensityOutput;

uniform ivec2 uOutputSize;
uniform int uParity;
uniform float uDiffusion;

void main()
{
    // Non-authoritative air/smoke density texture only; this pass never reads or writes the CPU authority grid.
    // Render-side effect only; there is intentionally zero GPU->CPU readback from this pass.
    ivec2 blockOrigin = (ivec2(gl_GlobalInvocationID.xy) * 2) + ivec2(uParity & 1);
    ivec2 maxPixel = blockOrigin + ivec2(1, 1);
    if (any(greaterThanEqual(maxPixel, uOutputSize)))
    {
        return;
    }

    float d00 = imageLoad(uAirSmokeDensityInput, blockOrigin + ivec2(0, 0)).r;
    float d10 = imageLoad(uAirSmokeDensityInput, blockOrigin + ivec2(1, 0)).r;
    float d01 = imageLoad(uAirSmokeDensityInput, blockOrigin + ivec2(0, 1)).r;
    float d11 = imageLoad(uAirSmokeDensityInput, blockOrigin + ivec2(1, 1)).r;

    float total = d00 + d10 + d01 + d11;
    float average = total * 0.25;
    float diffusionRate = clamp(uDiffusion, 0.0, 1.0);

    // Margolus 2x2 local redistribution: blend each cell toward the conserved block average.
    float out00 = mix(d00, average, diffusionRate);
    float out10 = mix(d10, average, diffusionRate);
    float out01 = mix(d01, average, diffusionRate);
    float out11 = mix(d11, average, diffusionRate);

    // Correct the last cell so floating-point roundoff does not leak density from the 2x2 block.
    out11 = total - out00 - out10 - out01;

    imageStore(uAirSmokeDensityOutput, blockOrigin + ivec2(0, 0), vec4(max(out00, 0.0), 0.0, 0.0, 1.0));
    imageStore(uAirSmokeDensityOutput, blockOrigin + ivec2(1, 0), vec4(max(out10, 0.0), 0.0, 0.0, 1.0));
    imageStore(uAirSmokeDensityOutput, blockOrigin + ivec2(0, 1), vec4(max(out01, 0.0), 0.0, 0.0, 1.0));
    imageStore(uAirSmokeDensityOutput, blockOrigin + ivec2(1, 1), vec4(max(out11, 0.0), 0.0, 0.0, 1.0));
}
""";

    /// <summary>
    /// GPU 粒子 point-sprite 顶点 shader，定义 plan/09 §4.5 的粒子 buffer 到屏幕空间契约。
    /// </summary>
    public const string ParticlePointSpriteVertex = """
#version 430

// particle_pointsprite.vert
layout(location = 0) in vec2 aWorldPosition;
layout(location = 1) in vec4 aColor;
layout(location = 2) in uint aMaterialId;
layout(location = 3) in uint aColorVariant;
layout(location = 4) in float aRadiusPixels;
layout(location = 5) in float aEmissive;

uniform vec2 uCameraWorldOrigin;
uniform vec2 uViewportSize;
uniform float uPixelsPerWorldUnit;
uniform float uPointSizeScale;

flat out uint vMaterialId;
flat out uint vColorVariant;
flat out float vEmissive;
out vec4 vColor;
out vec2 vWorldPosition;

void main()
{
    // GPU particle buffer attributes mirror the render-side particle SoA/VBO packing.
    vec2 cameraRelativeWorldPosition = aWorldPosition - uCameraWorldOrigin;
    vec2 viewportPosition = cameraRelativeWorldPosition * uPixelsPerWorldUnit;
    vec2 clipPosition = vec2(
        (viewportPosition.x / max(uViewportSize.x, 1.0)) * 2.0 - 1.0,
        1.0 - ((viewportPosition.y / max(uViewportSize.y, 1.0)) * 2.0));
    gl_Position = vec4(clipPosition, 0.0, 1.0);
    gl_PointSize = max(1.0, aRadiusPixels * 2.0 * uPixelsPerWorldUnit * uPointSizeScale);

    vMaterialId = aMaterialId;
    vColorVariant = aColorVariant;
    vEmissive = aEmissive;
    vColor = aColor;
    vWorldPosition = aWorldPosition;
}
""";

    /// <summary>
    /// GPU 粒子 point-sprite 片元 shader，定义材质纹理、colorVariant 与 emissive 输出契约。
    /// </summary>
    public const string ParticlePointSpriteFragment = """
#version 430

// particle_pointsprite.frag
uniform float uAlphaCutoff;
uniform float uEmissiveScale;

flat in uint vMaterialId;
flat in uint vColorVariant;
flat in float vEmissive;
in vec4 vColor;
in vec2 vWorldPosition;

layout(location = 0) out vec4 oSceneColor;
layout(location = 1) out vec4 oEmissiveColor;

vec2 pointSpriteUv()
{
    return gl_PointCoord;
}

void main()
{
    vec2 spriteUv = pointSpriteUv();
    vec2 centered = (spriteUv * 2.0) - vec2(1.0);
    if (dot(centered, centered) > 1.0)
    {
        discard;
    }

    float variant = float(vColorVariant & 255u) / 255.0;
    vec3 variantTint = mix(vec3(0.9, 0.95, 1.0), vec3(1.1, 1.0, 0.9), variant);
    vec4 scene = vec4(vColor.rgb * variantTint, vColor.a);
    if (scene.a <= uAlphaCutoff)
    {
        discard;
    }

    oSceneColor = scene;
    oEmissiveColor = vec4(scene.rgb * vEmissive * uEmissiveScale, scene.a);
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
            RadianceCascadeSdfJfaName => RadianceCascadeSdfJfa,
            RadianceCascadeBuildName => RadianceCascadeBuild,
            RadianceCascadeMergeName => RadianceCascadeMerge,
            RadianceCascadeApplyName => RadianceCascadeApply,
            AirSmokeDiffuseMargolusName => AirSmokeDiffuseMargolus,
            _ => throw new ArgumentException($"未注册的 GPU compute shader pass：{passName}", nameof(passName)),
        };
    }
}
