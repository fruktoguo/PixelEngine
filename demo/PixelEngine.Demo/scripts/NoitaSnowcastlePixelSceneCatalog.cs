using System.IO.Compression;
using System.Security.Cryptography;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Build 17130612 Snowcastle Wang marker 随机场景的纯 C# 目录。
/// </summary>
internal static partial class NoitaSnowcastlePixelSceneCatalog
{
    private const string ReferenceBiomeId = "snowcastle";
    private static readonly Lazy<DecodedNoitaSnowcastlePixelScene[]> DecodedValues = new(DecodeScenes);

    internal static ReadOnlySpan<DecodedNoitaSnowcastlePixelScene> DecodedScenes => DecodedValues.Value;

    internal static bool Supports(in NoitaWangMarkerAnchor anchor)
    {
        return string.Equals(anchor.ReferenceBiomeId, ReferenceBiomeId, StringComparison.Ordinal) &&
            ResolveMarkerFunction(in anchor) is not null;
    }

    internal static string? ResolveMarkerFunction(in NoitaWangMarkerAnchor anchor)
    {
        return !string.Equals(anchor.ReferenceBiomeId, ReferenceBiomeId, StringComparison.Ordinal)
            ? null
            : anchor.MarkerColor switch
            {
                "ffff0000" => "load_pixel_scene",
                "ffffff00" => "load_pixel_scene2",
                _ => null,
            };
    }

    internal static CompiledNoitaSnowcastlePixelSceneCatalog Compile(IMaterialQuery materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ReadOnlySpan<string> names = MaterialNames;
        ushort[] materialIds = new ushort[names.Length + 1];
        for (int i = 0; i < names.Length; i++)
        {
            string materialName = names[i];
            if (!materials.TryResolve(materialName, out MaterialId material))
            {
                throw new InvalidDataException($"Snowcastle Pixel Scene 缺少材质 {materialName}。");
            }

            materialIds[i + 1] = material.Value;
        }

        return new CompiledNoitaSnowcastlePixelSceneCatalog(materialIds, [.. DecodedScenes]);
    }

    internal static int SelectSceneIndex(string markerFunction, long worldX, long worldY, ulong worldSeed)
    {
        ReadOnlySpan<NoitaSnowcastlePixelSceneDefinition> scenes = Scenes;
        double total = 0d;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (string.Equals(scenes[i].MarkerFunction, markerFunction, StringComparison.Ordinal))
            {
                total += scenes[i].Probability;
            }
        }

        if (total <= 0d)
        {
            return -1;
        }

        double choice = PlayableCavernWorldGenerator.PixelSceneRandomUnit(worldX, worldY, worldSeed) * total;
        int last = -1;
        for (int i = 0; i < scenes.Length; i++)
        {
            ref readonly NoitaSnowcastlePixelSceneDefinition scene = ref scenes[i];
            if (!string.Equals(scene.MarkerFunction, markerFunction, StringComparison.Ordinal))
            {
                continue;
            }

            last = i;
            if (choice <= scene.Probability)
            {
                return i;
            }

            choice -= scene.Probability;
        }

        return last;
    }

    private static DecodedNoitaSnowcastlePixelScene[] DecodeScenes()
    {
        ReadOnlySpan<NoitaSnowcastlePixelSceneDefinition> scenes = Scenes;
        DecodedNoitaSnowcastlePixelScene[] decoded = new DecodedNoitaSnowcastlePixelScene[scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
        {
            ref readonly NoitaSnowcastlePixelSceneDefinition scene = ref scenes[i];
            byte[] compressed = Convert.FromBase64String(scene.Data);
            byte[] materialIndices = new byte[checked(scene.Width * scene.Height)];
            using MemoryStream source = new(compressed, writable: false);
            using BrotliStream brotli = new(source, CompressionMode.Decompress, leaveOpen: false);
            brotli.ReadExactly(materialIndices);
            if (brotli.ReadByte() >= 0 ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(materialIndices)),
                    scene.DecodedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Snowcastle Pixel Scene {scene.Id} 掩码校验失败。");
            }

            decoded[i] = new DecodedNoitaSnowcastlePixelScene(scene, materialIndices);
        }

        return decoded;
    }
}

internal sealed class CompiledNoitaSnowcastlePixelSceneCatalog(
    ushort[] materialIds,
    DecodedNoitaSnowcastlePixelScene[] scenes)
{
    public ushort[] MaterialIds { get; } = materialIds;

    public DecodedNoitaSnowcastlePixelScene[] Scenes { get; } = scenes;
}

internal readonly record struct NoitaSnowcastlePixelSceneDefinition(
    string Id,
    string MarkerFunction,
    double Probability,
    int Width,
    int Height,
    string DecodedSha256,
    string Data,
    ScriptAssetReference VisualAsset,
    ScriptAssetReference BackgroundAsset);

internal readonly record struct DecodedNoitaSnowcastlePixelScene(
    NoitaSnowcastlePixelSceneDefinition Definition,
    byte[] MaterialIndices);

internal readonly record struct NoitaSnowcastlePixelScenePlacement(
    long WorldX,
    long WorldY,
    int SceneIndex);
