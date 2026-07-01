namespace PixelEngine.Audio;

/// <summary>
/// 音频 listener 所需的相机视口快照，单位均为世界 cell。
/// </summary>
/// <param name="OriginWorldX">视口左上角世界 X。</param>
/// <param name="OriginWorldY">视口左上角世界 Y。</param>
/// <param name="CellsPerPixel">每个屏幕像素覆盖的世界 cell 数。</param>
/// <param name="ViewportWidth">视口宽度，单位为像素。</param>
/// <param name="ViewportHeight">视口高度，单位为像素。</param>
public readonly record struct AudioListenerView(
    float OriginWorldX,
    float OriginWorldY,
    float CellsPerPixel,
    int ViewportWidth,
    int ViewportHeight)
{
    /// <summary>
    /// listener 中心 X，单位为世界 cell。
    /// </summary>
    public float CenterWorldX => OriginWorldX + (ViewportWidth * CellsPerPixel * 0.5f);

    /// <summary>
    /// listener 中心 Y，单位为世界 cell。
    /// </summary>
    public float CenterWorldY => OriginWorldY + (ViewportHeight * CellsPerPixel * 0.5f);
}
