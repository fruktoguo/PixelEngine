using PixelEngine.Simulation;

namespace PixelEngine.Rendering;

/// <summary>
/// 材质图例使用的只读 swatch 采样入口。
/// </summary>
public static class MaterialSwatchProvider
{
    /// <summary>
    /// 返回材质代表色 BGRA。高亮类材质优先返回 HighlightColor，普通材质返回 BaseColor。
    /// </summary>
    public static uint GetSwatch(MaterialTable materials, ushort materialId)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ref readonly MaterialDef material = ref materials.Get(materialId);
        return (material.RenderStyle == MaterialRenderStyle.Emissive ||
                material.RenderStyle == MaterialRenderStyle.Hazard ||
                material.LegendCategory == MaterialLegendCategory.Resource) &&
            material.HighlightColorBGRA != 0
            ? material.HighlightColorBGRA
            : material.BaseColorBGRA;
    }
}
