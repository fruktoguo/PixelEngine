using System.IO.Compression;
using System.Security.Cryptography;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Noita Build 17130612 的 VegetationComponent 纯 C# 目录。
/// </summary>
internal static partial class NoitaVegetationCatalog
{
    private static readonly Lazy<DecodedNoitaVegetationAsset[]> DecodedAssetValues = new(DecodeAssets);

    internal static ReadOnlySpan<DecodedNoitaVegetationAsset> DecodedAssets => DecodedAssetValues.Value;

    internal static CompiledNoitaVegetationCatalog Compile(IMaterialQuery materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        Dictionary<string, List<CompiledNoitaVegetationLayer>> builders = new(StringComparer.Ordinal);
        ReadOnlySpan<NoitaVegetationLayerDefinition> layers = Layers;
        for (int index = 0; index < layers.Length; index++)
        {
            ref readonly NoitaVegetationLayerDefinition layer = ref layers[index];
            ushort materialId = 0;
            if (!string.IsNullOrWhiteSpace(layer.Material))
            {
                if (!materials.TryResolve(layer.Material, out MaterialId material))
                {
                    throw new InvalidDataException($"Noita vegetation 缺少材质 {layer.Material}。");
                }

                materialId = material.Value;
            }

            int maximumWidth = 1;
            int maximumHeight = 1;
            for (int variantIndex = 0; variantIndex < layer.VariantIndices.Length; variantIndex++)
            {
                ref readonly DecodedNoitaVegetationAsset asset = ref DecodedAssets[layer.VariantIndices[variantIndex]];
                maximumWidth = Math.Max(maximumWidth, asset.Definition.Width);
                maximumHeight = Math.Max(maximumHeight, asset.Definition.Height);
            }

            if (!builders.TryGetValue(layer.BiomeId, out List<CompiledNoitaVegetationLayer>? biomeLayers))
            {
                biomeLayers = [];
                builders.Add(layer.BiomeId, biomeLayers);
            }

            biomeLayers.Add(new CompiledNoitaVegetationLayer(
                layer,
                materialId,
                maximumWidth,
                maximumHeight));
        }

        Dictionary<string, CompiledNoitaVegetationLayer[]> groups = new(builders.Count, StringComparer.Ordinal);
        foreach ((string biomeId, List<CompiledNoitaVegetationLayer> biomeLayers) in builders)
        {
            groups.Add(biomeId, [.. biomeLayers]);
        }

        return new CompiledNoitaVegetationCatalog(groups, [.. DecodedAssets]);
    }

    private static DecodedNoitaVegetationAsset[] DecodeAssets()
    {
        ReadOnlySpan<NoitaVegetationAssetDefinition> assets = Assets;
        DecodedNoitaVegetationAsset[] decoded = new DecodedNoitaVegetationAsset[assets.Length];
        for (int index = 0; index < assets.Length; index++)
        {
            ref readonly NoitaVegetationAssetDefinition asset = ref assets[index];
            byte[] compressed = Convert.FromBase64String(asset.MaskData);
            byte[] mask = new byte[checked(asset.Width * asset.Height)];
            using MemoryStream source = new(compressed, writable: false);
            using BrotliStream brotli = new(source, CompressionMode.Decompress, leaveOpen: false);
            brotli.ReadExactly(mask);
            if (brotli.ReadByte() >= 0 ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(mask)),
                    asset.DecodedMaskSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Noita vegetation asset {index} mask 校验失败。");
            }

            decoded[index] = new DecodedNoitaVegetationAsset(asset, mask);
        }

        return decoded;
    }
}

internal sealed class CompiledNoitaVegetationCatalog(
    Dictionary<string, CompiledNoitaVegetationLayer[]> groups,
    DecodedNoitaVegetationAsset[] assets)
{
    public DecodedNoitaVegetationAsset[] Assets { get; } = assets;

    public bool TryGetLayers(string biomeId, out CompiledNoitaVegetationLayer[] layers)
    {
        return groups.TryGetValue(biomeId, out layers!);
    }
}

internal readonly record struct NoitaVegetationAssetDefinition(
    int Width,
    int Height,
    int OffsetX,
    int OffsetY,
    string DecodedMaskSha256,
    string MaskData,
    ScriptAssetReference Asset);

internal readonly record struct DecodedNoitaVegetationAsset(
    NoitaVegetationAssetDefinition Definition,
    byte[] Mask);

internal readonly record struct NoitaVegetationLayerDefinition(
    int Ordinal,
    string BiomeId,
    bool Enabled,
    bool IsVisual,
    bool IsCeiling,
    double RandomSeed,
    string Material,
    double Probability,
    double RadiusLow,
    double RadiusHigh,
    double TreeWidth,
    double ExtraY,
    uint VisualColor,
    double VisualOffsetX,
    double VisualOffsetY,
    int[] VariantIndices);

internal readonly record struct CompiledNoitaVegetationLayer(
    NoitaVegetationLayerDefinition Definition,
    ushort MaterialId,
    int MaximumWidth,
    int MaximumHeight);

internal readonly record struct NoitaVegetationPlacement(
    long AnchorX,
    long AnchorY,
    int VariantIndex);
