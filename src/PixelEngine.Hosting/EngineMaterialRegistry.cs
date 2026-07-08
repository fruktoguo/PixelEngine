using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 暴露给 Demo 与脚本的材质查询门面，内部适配 Simulation 材质表。
/// </summary>
public sealed class EngineMaterialRegistry : IMaterialQuery
{
    private readonly MaterialTable _materials;

    internal EngineMaterialRegistry(MaterialTable materials)
    {
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    }

    /// <summary>
    /// 当前 live/tombstone 材质槽数量。
    /// </summary>
    public int Count => _materials.Count;

    /// <summary>
    /// 按稳定材质名解析材质句柄；失败时返回无效句柄。
    /// </summary>
    /// <param name="name">稳定材质名。</param>
    /// <returns>解析得到的材质句柄。</returns>
    public MaterialId Resolve(string name)
    {
        return TryResolve(name, out MaterialId id) ? id : MaterialId.Invalid;
    }

    /// <summary>
    /// 尝试按稳定材质名解析材质句柄。
    /// </summary>
    /// <param name="name">稳定材质名。</param>
    /// <param name="id">解析成功时返回材质句柄。</param>
    /// <returns>若材质名存在则返回 true。</returns>
    public bool TryResolve(string name, out MaterialId id)
    {
        if (_materials.TryGetId(name, out ushort raw))
        {
            id = new MaterialId(raw);
            return true;
        }

        id = MaterialId.Invalid;
        return false;
    }

    /// <summary>
    /// 读取材质属性摘要。
    /// </summary>
    /// <param name="id">材质句柄。</param>
    /// <returns>材质属性摘要。</returns>
    public MaterialInfo GetInfo(MaterialId id)
    {
        ref readonly MaterialDef material = ref _materials.Get(id.Value);
        MaterialProperty flags = material.PropertyFlags;
        bool emissive = (flags & MaterialProperty.Emissive) != 0 || material.RenderStyle == MaterialRenderStyle.Emissive;
        bool destructible = id.Value != 0 &&
            material.Type is CellType.Solid or CellType.Powder &&
            (flags & MaterialProperty.Indestructible) == 0;
        bool blocksCharacter = material.Type is CellType.Solid or CellType.Powder;
        return new MaterialInfo(
            id,
            material.Name,
            material.Density,
            material.Type == CellType.Solid,
            string.IsNullOrWhiteSpace(material.DisplayName) ? material.Name : material.DisplayName,
            LegendCategoryName(material.LegendCategory),
            material.LegendVisible,
            material.BaseColorBGRA,
            material.MineYield,
            material.Type,
            material.LegendCategory,
            emissive,
            material.Hardness != 0 ? material.Hardness : material.Durability,
            material.MaxIntegrity,
            destructible,
            material.Dispersion,
            blocksCharacter);
    }

    private static string LegendCategoryName(MaterialLegendCategory category)
    {
        return category switch
        {
            MaterialLegendCategory.Terrain => "Terrain",
            MaterialLegendCategory.Liquid => "Liquid",
            MaterialLegendCategory.Gas => "Gas",
            MaterialLegendCategory.Destructible => "Destructible",
            MaterialLegendCategory.Hazard => "Hazard",
            MaterialLegendCategory.Resource => "Resource",
            MaterialLegendCategory.Special => "Special",
            _ => nameof(MaterialLegendCategory.Special),
        };
    }
}
