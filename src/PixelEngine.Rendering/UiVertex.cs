namespace PixelEngine.Rendering;

/// <summary>
/// UI 三角形顶点。颜色为 BGRA8，渲染相位转换为 shader 所需 RGBA。
/// </summary>
/// <param name="X">framebuffer 像素 X。</param>
/// <param name="Y">framebuffer 像素 Y。</param>
/// <param name="U">纹理 U。</param>
/// <param name="V">纹理 V。</param>
/// <param name="ColorBgra">顶点颜色，BGRA8。</param>
public readonly record struct UiVertex(float X, float Y, float U, float V, uint ColorBgra);
