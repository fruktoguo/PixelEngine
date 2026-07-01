namespace PixelEngine.Rendering;

/// <summary>
/// 渲染相机快照。相机权威归 World/Hosting，Rendering 只读消费该快照。
/// </summary>
/// <param name="OriginWorldX">屏幕左上角对应的世界 X 坐标，单位为 cell。</param>
/// <param name="OriginWorldY">屏幕左上角对应的世界 Y 坐标，单位为 cell。</param>
/// <param name="CellsPerPixel">每个屏幕像素覆盖的世界 cell 数，1 表示 1:1。</param>
/// <param name="ViewportWidth">视口宽度，单位为像素。</param>
/// <param name="ViewportHeight">视口高度，单位为像素。</param>
public readonly record struct CameraState(
    float OriginWorldX,
    float OriginWorldY,
    float CellsPerPixel,
    int ViewportWidth,
    int ViewportHeight)
{
    /// <summary>
    /// 创建 1:1 cell/pixel 相机。
    /// </summary>
    /// <param name="originWorldX">左上角世界 X。</param>
    /// <param name="originWorldY">左上角世界 Y。</param>
    /// <param name="viewportWidth">视口宽度。</param>
    /// <param name="viewportHeight">视口高度。</param>
    /// <returns>相机快照。</returns>
    public static CameraState OneToOne(float originWorldX, float originWorldY, int viewportWidth, int viewportHeight)
    {
        return new CameraState(originWorldX, originWorldY, 1f, viewportWidth, viewportHeight);
    }
}
