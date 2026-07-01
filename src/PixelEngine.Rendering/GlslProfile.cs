namespace PixelEngine.Rendering;

/// <summary>
/// GLSL profile 变体。
/// </summary>
public enum GlslProfile
{
    /// <summary>
    /// 桌面 OpenGL 3.3 Core，使用 <c>#version 330 core</c>。
    /// </summary>
    DesktopGl330,

    /// <summary>
    /// OpenGL ES 3.0，使用 <c>#version 300 es</c>。
    /// </summary>
    Gles300,
}

/// <summary>
/// GLSL profile 辅助方法。
/// </summary>
public static class GlslProfileSource
{
    /// <summary>
    /// 返回指定 profile 的版本头。
    /// </summary>
    /// <param name="profile">GLSL profile。</param>
    /// <returns>shader 版本头。</returns>
    public static string VersionHeader(GlslProfile profile)
    {
        return profile switch
        {
            GlslProfile.DesktopGl330 => "#version 330 core\n",
            GlslProfile.Gles300 => "#version 300 es\nprecision mediump float;\n",
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }
}
