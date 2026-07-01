namespace PixelEngine.Rendering;

/// <summary>
/// OverlayRenderer 使用的 GLSL shader 源码生成器。
/// </summary>
public static class OverlayShaderSources
{
    /// <summary>
    /// 返回 overlay vertex shader。输入坐标为 viewport pixel，由 shader 转换到 clip space。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string Vertex(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;
layout(location = 3) in float aUseTexture;

out vec2 vTexCoord;
out vec4 vColor;
out float vUseTexture;

uniform vec2 uViewportSize;

void main()
{
    vec2 normalized = aPosition / uViewportSize;
    vec2 clip = vec2((normalized.x * 2.0) - 1.0, 1.0 - (normalized.y * 2.0));
    vTexCoord = aTexCoord;
    vColor = aColor;
    vUseTexture = aUseTexture;
    gl_Position = vec4(clip, 0.0, 1.0);
}
""";
    }

    /// <summary>
    /// 返回 overlay fragment shader。矩形直接输出颜色，精灵采样 uSpriteTexture 并乘以 tint。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 源码。</returns>
    public static string Fragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vTexCoord;
in vec4 vColor;
in float vUseTexture;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uSpriteTexture;

void main()
{
    vec4 source = vUseTexture > 0.5 ? texture(uSpriteTexture, vTexCoord) : vec4(1.0);
    fragColor = source * vColor;
}
""";
    }
}
