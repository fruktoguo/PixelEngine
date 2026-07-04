namespace PixelEngine.Rendering;

/// <summary>
/// UI primitive renderer 使用的 GLSL shader 源码生成器。
/// </summary>
public static class UiShaderSources
{
    /// <summary>
    /// 返回 UI vertex shader。
    /// </summary>
    public static string Vertex(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

out vec2 vTexCoord;
out vec4 vColor;

uniform vec2 uFramebufferSize;
uniform mat3 uTransform;

void main()
{
    vec3 transformed = uTransform * vec3(aPosition, 1.0);
    vec2 normalized = transformed.xy / uFramebufferSize;
    vec2 clip = vec2((normalized.x * 2.0) - 1.0, 1.0 - (normalized.y * 2.0));
    vTexCoord = aTexCoord;
    vColor = aColor;
    gl_Position = vec4(clip, 0.0, 1.0);
}
""";
    }

    /// <summary>
    /// 返回 UI fragment shader。
    /// </summary>
    public static string Fragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vTexCoord;
in vec4 vColor;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uTexture;
uniform float uUseTexture;

void main()
{
    vec4 source = uUseTexture > 0.5 ? texture(uTexture, vTexCoord) : vec4(1.0);
    fragColor = source * vColor;
}
""";
    }
}
