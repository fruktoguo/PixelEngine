namespace PixelEngine.Rendering;

/// <summary>
/// GPU 自由粒子 point-sprite shader 源码。
/// </summary>
public static class GpuParticleShaderSources
{
    /// <summary>
    /// 顶点 shader：把 world cell 坐标按相机转换到 NDC，并设置 point size。
    /// </summary>
    public static string Vertex(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
layout(location = 0) in vec2 aWorldPosition;
layout(location = 1) in vec4 aColor;
layout(location = 2) in float aEmissive;
layout(location = 3) in float aRadiusPixels;
layout(location = 4) in float aMaterialId;
layout(location = 5) in float aColorVariant;

uniform vec2 uCameraWorldOrigin;
uniform vec2 uViewportSize;
uniform float uCellsPerPixel;

out vec4 vColor;
flat out float vEmissive;
flat out float vMaterialId;
flat out float vColorVariant;

void main()
{
    vec2 screen = (aWorldPosition - uCameraWorldOrigin) / uCellsPerPixel;
    vec2 ndc = vec2((screen.x / uViewportSize.x) * 2.0 - 1.0, 1.0 - (screen.y / uViewportSize.y) * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    gl_PointSize = max(1.0, aRadiusPixels);

    vColor = aColor;
    vEmissive = aEmissive;
    vMaterialId = aMaterialId;
    vColorVariant = aColorVariant;
}
""";
    }

    /// <summary>
    /// 片元 shader：圆形 point sprite，scene pass 输出粒子色，emissive pass 只输出发光粒子。
    /// </summary>
    public static string Fragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec4 vColor;
flat in float vEmissive;
flat in float vMaterialId;
flat in float vColorVariant;

uniform int uEmissivePass;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec2 centered = (gl_PointCoord * 2.0) - vec2(1.0);
    if (dot(centered, centered) > 1.0)
    {
        discard;
    }

    if (uEmissivePass != 0)
    {
        if (vEmissive <= 0.0)
        {
            discard;
        }

        fragColor = vec4(vColor.rgb * vEmissive, vColor.a);
        return;
    }

    fragColor = vColor;
}
""";
    }
}
