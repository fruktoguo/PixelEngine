using PixelEngine.Simulation;

namespace PixelEngine.Rendering;

/// <summary>
/// 材质纹理采样接口。未提供或采样失败时渲染回退到 <see cref="MaterialDef.BaseColorBGRA" />。
/// </summary>
public interface IMaterialTextureProvider
{
    /// <summary>
    /// 尝试按世界坐标采样材质纹理。
    /// </summary>
    /// <param name="material">材质定义。</param>
    /// <param name="worldX">世界 X 坐标。</param>
    /// <param name="worldY">世界 Y 坐标。</param>
    /// <param name="bgra">输出 BGRA8 颜色。</param>
    /// <returns>采样成功返回 true。</returns>
    bool TrySample(in MaterialDef material, int worldX, int worldY, out uint bgra);
}
