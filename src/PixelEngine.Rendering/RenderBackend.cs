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
    /// 桌面 OpenGL 3.3，经 WGL_NV_DX_interop2 写入 D3D11/DXGI swap-chain 后呈现。
    /// </summary>
    DesktopGl33DxgiInterop,

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

    /// <summary>
    /// 优先 desktop GL + DXGI interop，创建失败时回退普通 desktop GL。Windows Editor 用此顺序保证系统捕获兼容性。
    /// </summary>
    CaptureCompatible,
}
