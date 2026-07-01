namespace PixelEngine.Rendering;

/// <summary>
/// RenderBufferBuilder 的逐 cell 调试着色钩子。
/// </summary>
public interface IDebugCellColorProvider
{
    /// <summary>
    /// 尝试为指定世界 cell 提供调试颜色；返回 false 时使用正常材质/温度渲染色。
    /// </summary>
    bool TryGetDebugColor(
        int worldX,
        int worldY,
        ushort materialId,
        byte flags,
        float temperatureCelsius,
        out uint colorBgra);
}
