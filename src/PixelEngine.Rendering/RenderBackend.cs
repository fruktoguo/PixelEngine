namespace PixelEngine.Rendering;

/// <summary>
/// 渲染后端类型。默认使用桌面 OpenGL 3.3 Core，必要时回退到 OpenGL ES 3.0（ANGLE 路径）。
/// </summary>
public enum RenderBackend
{
    /// <summary>
    /// 桌面 OpenGL 3.3 Core profile。
    /// </summary>
    DesktopGl33,

    /// <summary>
    /// OpenGL ES 3.0 后端，Windows 上由 ANGLE 或系统 EGL/GLES 提供。
    /// </summary>
    GlEs30Angle,
}

/// <summary>
/// 渲染后端选择策略。
/// </summary>
public enum RenderBackendPreference
{
    /// <summary>
    /// 优先桌面 OpenGL 3.3，失败后回退 OpenGL ES 3.0。
    /// </summary>
    Auto,

    /// <summary>
    /// 只尝试桌面 OpenGL 3.3。
    /// </summary>
    DesktopGl33,

    /// <summary>
    /// 只尝试 OpenGL ES 3.0。
    /// </summary>
    GlEs30Angle,
}
