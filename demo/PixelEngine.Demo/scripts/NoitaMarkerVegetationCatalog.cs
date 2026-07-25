using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Noita Build 17130612 中由 Wang marker 直接生成的静态植被视觉目录。
/// 这些对象与 biome <c>VegetationComponent</c> 分属两条来源链，不能合并成通用随机植被。
/// </summary>
internal static class NoitaMarkerVegetationCatalog
{
    private const ulong BigBushSelectionSalt = 0x4255_5348_4D41_524BUL;
    private static readonly ScriptAssetReference EntranceGrassAsset = new(
        ScriptAssetKind.Texture,
        "noita-marker-mountain-left-entrance-grass",
        "maps/noita/marker-vegetation/mountain_left_entrance_grass.png");
    private static readonly int[] BigBushVariantIndices = [82, 83, 84, 85, 86, 97, 98];

    internal static bool Supports(string function)
    {
        return function is "spawn_big_bushes" or "spawn_grass";
    }

    internal static bool TryCreateLayer(
        in NoitaWangMarkerAnchor anchor,
        ulong worldSeed,
        out WorldVisualLayerDescriptor layer)
    {
        if (anchor.Function == "spawn_grass")
        {
            // mountain_left_entrance_grass.xml: anchor=(198,40), image=397x94。
            layer = new WorldVisualLayerDescriptor(
                EntranceGrassAsset,
                anchor.WorldX - 198,
                anchor.WorldY - 40,
                397,
                94,
                WorldVisualLayerKind.Decoration);
            return true;
        }

        if (anchor.Function == "spawn_big_bushes")
        {
            int selection = (int)(PlayableCavernWorldGenerator.MarkerRandomUnit(
                anchor.WorldX,
                anchor.WorldY,
                worldSeed,
                BigBushSelectionSalt) * BigBushVariantIndices.Length);
            int assetIndex = BigBushVariantIndices[Math.Min(selection, BigBushVariantIndices.Length - 1)];
            ref readonly NoitaVegetationAssetDefinition asset = ref NoitaVegetationCatalog.Assets[assetIndex];
            // mountain_left_entrance.lua 先将 marker 下移 12px，再应用 PixelSprite anchor。
            layer = new WorldVisualLayerDescriptor(
                asset.Asset,
                anchor.WorldX - asset.OffsetX,
                anchor.WorldY + 12 - asset.OffsetY,
                asset.Width,
                asset.Height,
                WorldVisualLayerKind.Decoration);
            return true;
        }

        layer = default;
        return false;
    }
}
