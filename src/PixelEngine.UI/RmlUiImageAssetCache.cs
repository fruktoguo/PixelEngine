using System.Buffers.Binary;

namespace PixelEngine.UI;

internal sealed class RmlUiImageAssetCache : IDisposable
{
    private readonly Dictionary<string, string> _converted = new(StringComparer.OrdinalIgnoreCase);
    private string? _directory;

    internal string ConvertPngToTga(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (_converted.TryGetValue(fullPath, out string? cached))
        {
            return cached;
        }

        UiImageBitmap bitmap = UiPngImageLoader.Load(fullPath);
        string directory = EnsureDirectory();
        string target = Path.Combine(directory, $"{UiStableId.Hash(fullPath):X8}.tga");
        WriteTga(target, bitmap);
        _converted.Add(fullPath, target);
        return target;
    }

    /// <summary>
    /// 删除 RmlUi GL3 后端为 PNG 转 TGA 产生的临时图片缓存目录。
    /// </summary>
    public void Dispose()
    {
        if (_directory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        _directory = null;
        _converted.Clear();
    }

    private string EnsureDirectory()
    {
        if (_directory is not null)
        {
            return _directory;
        }

        _directory = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-images-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_directory);
        return _directory;
    }

    private static void WriteTga(string path, UiImageBitmap bitmap)
    {
        using FileStream file = File.Create(path);
        Span<byte> header = stackalloc byte[18];
        header[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(12, 2), checked((ushort)bitmap.Width));
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(14, 2), checked((ushort)bitmap.Height));
        header[16] = 32;
        header[17] = 0x28;
        file.Write(header);

        Span<byte> pixel = stackalloc byte[4];
        uint[] rgba = bitmap.Rgba;
        for (int i = 0; i < rgba.Length; i++)
        {
            uint value = rgba[i];
            pixel[0] = (byte)((value >> 16) & 0xFF);
            pixel[1] = (byte)((value >> 8) & 0xFF);
            pixel[2] = (byte)(value & 0xFF);
            pixel[3] = (byte)((value >> 24) & 0xFF);
            file.Write(pixel);
        }
    }
}
