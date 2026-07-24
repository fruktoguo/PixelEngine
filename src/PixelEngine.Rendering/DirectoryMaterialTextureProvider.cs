using System.Globalization;
using StbImageSharp;

namespace PixelEngine.Rendering;

/// <summary>
/// 从内容目录加载以数值 id 为文件名前缀的材质纹理，并按世界坐标平铺采样。
/// </summary>
public sealed class DirectoryMaterialTextureProvider : IMaterialTextureProvider
{
    private readonly TextureData?[] _textures;

    private DirectoryMaterialTextureProvider(TextureData?[] textures)
    {
        _textures = textures;
    }

    /// <summary>
    /// 尝试从目录加载 <c>00_name.png</c>、<c>19_name.png</c> 等材质纹理。
    /// 不存在目录或目录中没有匹配文件时返回 null。
    /// </summary>
    /// <param name="directoryPath">材质纹理目录。</param>
    /// <returns>已加载的纹理提供器；没有纹理时返回 null。</returns>
    public static DirectoryMaterialTextureProvider? TryLoad(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            return null;
        }

        string[] paths = Directory.GetFiles(directoryPath, "*.png", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        int maxId = -1;
        for (int i = 0; i < paths.Length; i++)
        {
            if (TryParseTextureId(paths[i], out int id))
            {
                maxId = Math.Max(maxId, id);
            }
        }

        if (maxId < 0)
        {
            return null;
        }

        TextureData?[] textures = new TextureData?[maxId + 1];
        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (!TryParseTextureId(path, out int id))
            {
                continue;
            }

            if (textures[id] is not null)
            {
                throw new InvalidDataException($"材质纹理 id {id} 重复：'{path}'。");
            }

            using FileStream stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            if (image.Width <= 0 || image.Height <= 0)
            {
                throw new InvalidDataException($"材质纹理尺寸无效：'{path}'。");
            }

            uint[] pixels = GC.AllocateUninitializedArray<uint>(checked(image.Width * image.Height));
            ReadOnlySpan<byte> rgba = image.Data;
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                int source = pixelIndex * 4;
                pixels[pixelIndex] = rgba[source + 2] |
                    ((uint)rgba[source + 1] << 8) |
                    ((uint)rgba[source] << 16) |
                    ((uint)rgba[source + 3] << 24);
            }

            textures[id] = new TextureData(image.Width, image.Height, pixels);
        }

        return new DirectoryMaterialTextureProvider(textures);
    }

    /// <inheritdoc />
    public bool TrySample(in Simulation.MaterialDef material, int worldX, int worldY, out uint bgra)
    {
        int textureId = material.TextureId;
        if ((uint)textureId >= (uint)_textures.Length || _textures[textureId] is not TextureData texture)
        {
            bgra = 0;
            return false;
        }

        int x = PositiveModulo(worldX, texture.Width);
        int y = PositiveModulo(worldY, texture.Height);
        bgra = texture.Pixels[(y * texture.Width) + x];
        return true;
    }

    private static bool TryParseTextureId(string path, out int id)
    {
        id = -1;
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.IndexOf('_');
        return separator > 0 && int.TryParse(
            name.AsSpan(0, separator),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out id) && id >= 0;
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private sealed record TextureData(int Width, int Height, uint[] Pixels);
}
