using System.IO.Compression;
using System.Security.Cryptography;

namespace PixelEngine.Demo;

/// <summary>
/// Noita 全局 buffered pixel scene 的纯 C# 编译期目录。
/// </summary>
internal static partial class NoitaPixelSceneCatalog
{
    private const int SpatialBucketSize = 512;
    private static readonly Lazy<DecodedNoitaPixelScene[]> DecodedSceneValues = new(DecodeScenes);
    private static readonly Lazy<Dictionary<NoitaPixelSceneBucketKey, DecodedNoitaPixelScene[]>> SpatialBuckets = new(BuildSpatialBuckets);

    internal static ReadOnlySpan<DecodedNoitaPixelScene> DecodedScenes => DecodedSceneValues.Value;

    internal static bool TryFindAt(long worldX, long worldY, out NoitaPixelSceneDefinition scene)
    {
        ReadOnlySpan<NoitaPixelSceneDefinition> scenes = Scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            ref readonly NoitaPixelSceneDefinition candidate = ref scenes[i];
            if (worldX >= candidate.WorldX && worldY >= candidate.WorldY &&
                worldX < candidate.WorldX + candidate.Width &&
                worldY < candidate.WorldY + candidate.Height)
            {
                scene = candidate;
                return true;
            }
        }

        scene = default;
        return false;
    }

    internal static bool TrySample(long worldX, long worldY, out NoitaPixelSceneMaterial material)
    {
        long bucketX = FloorDivide(worldX, SpatialBucketSize);
        long bucketY = FloorDivide(worldY, SpatialBucketSize);
        if (!SpatialBuckets.Value.TryGetValue(new NoitaPixelSceneBucketKey(bucketX, bucketY), out DecodedNoitaPixelScene[]? bucket))
        {
            material = default;
            return false;
        }

        ReadOnlySpan<DecodedNoitaPixelScene> scenes = bucket;
        for (int i = scenes.Length - 1; i >= 0; i--)
        {
            ref readonly DecodedNoitaPixelScene scene = ref scenes[i];
            if (scene.TrySample(worldX, worldY, out material))
            {
                return true;
            }
        }

        material = default;
        return false;
    }

    private static Dictionary<NoitaPixelSceneBucketKey, DecodedNoitaPixelScene[]> BuildSpatialBuckets()
    {
        Dictionary<NoitaPixelSceneBucketKey, List<DecodedNoitaPixelScene>> builders = [];
        foreach (DecodedNoitaPixelScene scene in DecodedSceneValues.Value)
        {
            long minimumBucketX = FloorDivide(scene.WorldX, SpatialBucketSize);
            long maximumBucketX = FloorDivide(scene.WorldX + scene.Width - 1L, SpatialBucketSize);
            long minimumBucketY = FloorDivide(scene.WorldY, SpatialBucketSize);
            long maximumBucketY = FloorDivide(scene.WorldY + scene.Height - 1L, SpatialBucketSize);
            for (long bucketY = minimumBucketY; bucketY <= maximumBucketY; bucketY++)
            {
                for (long bucketX = minimumBucketX; bucketX <= maximumBucketX; bucketX++)
                {
                    NoitaPixelSceneBucketKey key = new(bucketX, bucketY);
                    if (!builders.TryGetValue(key, out List<DecodedNoitaPixelScene>? scenes))
                    {
                        scenes = [];
                        builders.Add(key, scenes);
                    }

                    scenes.Add(scene);
                }
            }
        }

        Dictionary<NoitaPixelSceneBucketKey, DecodedNoitaPixelScene[]> result = new(builders.Count);
        foreach ((NoitaPixelSceneBucketKey key, List<DecodedNoitaPixelScene> scenes) in builders)
        {
            result.Add(key, [.. scenes]);
        }

        return result;
    }

    private static long FloorDivide(long value, int divisor)
    {
        long quotient = Math.DivRem(value, divisor, out long remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }


    private static DecodedNoitaPixelScene[] DecodeScenes()
    {
        ReadOnlySpan<NoitaPixelSceneDefinition> scenes = Scenes;
        ReadOnlySpan<NoitaPixelSceneMaskDefinition> masks = Masks;
        DecodedNoitaPixelScene[] result = new DecodedNoitaPixelScene[masks.Length];
        for (int i = 0; i < masks.Length; i++)
        {
            ref readonly NoitaPixelSceneMaskDefinition mask = ref masks[i];
            NoitaPixelSceneDefinition scene = scenes[mask.Ordinal];
            int decodedLength = checked(mask.Width * mask.Height);
            byte[] compressed = Convert.FromBase64String(mask.Data);
            byte[] decoded = new byte[decodedLength];
            using MemoryStream source = new(compressed, writable: false);
            using BrotliStream brotli = new(source, CompressionMode.Decompress, leaveOpen: false);
            brotli.ReadExactly(decoded);
            if (brotli.ReadByte() >= 0 ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(decoded)),
                    mask.DecodedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Noita Pixel Scene {scene.StableId} 的语义掩码校验失败。");
            }

            result[i] = new DecodedNoitaPixelScene(
                scene.Ordinal,
                scene.WorldX,
                scene.WorldY,
                mask.Width,
                mask.Height,
                mask.MarkerPixelCount,
                decoded);
        }

        return result;
    }
}

internal enum NoitaPixelSceneMaterial : byte
{
    Empty,
    Dirt,
    PackedDirt,
    Water,
    Stone,
    Metal,
    Wood,
    PackedSand,
    BoundaryStone,
    Crystal,
    Smoke,
    Ice,
    Sand,
    Glass,
    Lava,
    Blood,
    BoneStatic,
    CheeseStatic,
    Mud,
    SandPetrify,
    SnowSticky,
}

internal readonly record struct NoitaPixelSceneMaskDefinition(
    int Ordinal,
    int Width,
    int Height,
    int MarkerPixelCount,
    string DecodedSha256,
    string Data);

internal readonly record struct NoitaPixelSceneBucketKey(long X, long Y);

internal readonly record struct NoitaPixelSceneMarkerDefinition(
    int SceneOrdinal,
    int LocalX,
    int LocalY,
    long WorldX,
    long WorldY,
    string Color,
    string Function,
    string Origin);

internal readonly record struct DecodedNoitaPixelScene(
    int Ordinal,
    int WorldX,
    int WorldY,
    int Width,
    int Height,
    int MarkerPixelCount,
    byte[] Materials)
{
    public bool TrySample(long worldX, long worldY, out NoitaPixelSceneMaterial material)
    {
        long localX = worldX - WorldX;
        long localY = worldY - WorldY;
        if ((ulong)localX >= (uint)Width || (ulong)localY >= (uint)Height)
        {
            material = default;
            return false;
        }

        material = (NoitaPixelSceneMaterial)Materials[checked(((int)localY * Width) + (int)localX)];
        return true;
    }
}

internal readonly record struct NoitaPixelSceneDefinition(
    int Ordinal,
    int WorldX,
    int WorldY,
    int Width,
    int Height,
    string MaterialPath,
    string ColorsPath,
    string BackgroundPath,
    string MaterialSha256,
    string ColorsSha256,
    string BackgroundSha256,
    bool CleanAreaBefore,
    bool SkipBiomeChecks,
    bool SkipEdgeTextures,
    string StableId);
