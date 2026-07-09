using System.Buffers.Binary;
using System.IO.Compression;

namespace PixelEngine.Demo;

/// <summary>
/// 解码后的材质地图 RGBA 像素缓冲，行优先存储。
/// </summary>
internal readonly struct MaterialMapImage
{
    public MaterialMapImage(int width, int height, uint[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != checked(width * height))
        {
            throw new ArgumentException("RGBA 像素数量与图片宽高不一致。", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Rgba { get; }

    public uint PixelAt(int x, int y)
    {
        return Rgba[checked((y * Width) + x)];
    }
}

/// <summary>
/// 轻量 PNG 解码器，仅支持 Demo 材质地图所需的 8-bit 格式与标准 filter。
/// </summary>
internal static class MaterialMapPngLoader
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static MaterialMapImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException($"不是有效 PNG 文件：{path}");
        }

        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using MemoryStream idat = new();
        // 逐 chunk 解析 IHDR / PLTE / tRNS / IDAT
        int offset = PngSignature.Length;
        while (offset < bytes.Length)
        {
            if (offset + 12 > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk 头不完整。");
            }

            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            offset += 4;
            if (length < 0 || offset + 4 + length + 4 > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk 长度越界。");
            }

            ReadOnlySpan<byte> type = bytes.AsSpan(offset, 4);
            offset += 4;
            ReadOnlySpan<byte> data = bytes.AsSpan(offset, length);
            offset += length;
            offset += 4; // CRC 由 PNG 编码器保证；这里解析材质掩码时不做重复校验。

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                {
                    throw new InvalidDataException("PNG IHDR 长度非法。");
                }

                width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                bitDepth = data[8];
                colorType = data[9];
                if (data[10] != 0 || data[11] != 0 || data[12] != 0)
                {
                    throw new InvalidDataException("仅支持标准 deflate / adaptive filter / non-interlaced PNG。");
                }
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PNG 缺少有效 IHDR。");
        }

        if (bitDepth != 8)
        {
            throw new InvalidDataException($"材质地图仅支持 8-bit PNG，当前 bitDepth={bitDepth}。");
        }

        // zlib 解压 IDAT 后按 scanline filter 还原 RGBA
        byte[] decompressed = Decompress(idat.ToArray());
        return DecodeScanlines(width, height, colorType, decompressed, palette, transparency);
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using MemoryStream source = new(compressed, writable: false);
        using ZLibStream zlib = new(source, CompressionMode.Decompress);
        using MemoryStream output = new();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static MaterialMapImage DecodeScanlines(
        int width,
        int height,
        byte colorType,
        ReadOnlySpan<byte> compressedRows,
        byte[]? palette,
        byte[]? transparency)
    {
        int channels = ChannelsFor(colorType);
        int bytesPerPixel = channels;
        int rowBytes = checked(width * bytesPerPixel);
        int expectedBytes = checked((rowBytes + 1) * height);
        if (compressedRows.Length != expectedBytes)
        {
            throw new InvalidDataException($"PNG scanline 尺寸不匹配，expected={expectedBytes}, actual={compressedRows.Length}。");
        }

        byte[] previous = new byte[rowBytes];
        byte[] current = new byte[rowBytes];
        uint[] rgba = new uint[checked(width * height)];
        int sourceOffset = 0;
        for (int y = 0; y < height; y++)
        {
            byte filter = compressedRows[sourceOffset++];
            compressedRows.Slice(sourceOffset, rowBytes).CopyTo(current);
            sourceOffset += rowBytes;
            ApplyFilter(filter, current, previous, bytesPerPixel);
            DecodeRow(colorType, current, width, y, rgba, palette, transparency);
            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        return new MaterialMapImage(width, height, rgba);
    }

    private static int ChannelsFor(byte colorType)
    {
        return colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"不支持的 PNG colorType={colorType}。"),
        };
    }

    private static void ApplyFilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        for (int i = 0; i < row.Length; i++)
        {
            int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
            int up = previous[i];
            int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
            int add = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) >> 1,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"不支持的 PNG filter={filter}。"),
            };
            row[i] = unchecked((byte)(row[i] + add));
        }
    }

    private static void DecodeRow(
        byte colorType,
        ReadOnlySpan<byte> row,
        int width,
        int y,
        Span<uint> rgba,
        byte[]? palette,
        byte[]? transparency)
    {
        for (int x = 0; x < width; x++)
        {
            int source = colorType switch
            {
                0 or 3 => x,
                2 => x * 3,
                4 => x * 2,
                6 => x * 4,
                _ => throw new InvalidDataException($"不支持的 PNG colorType={colorType}。"),
            };
            rgba[(y * width) + x] = colorType switch
            {
                0 => PackRgba(row[source], row[source], row[source], AlphaForGray(row[source], transparency)),
                2 => PackRgba(row[source], row[source + 1], row[source + 2], AlphaForRgb(row[source], row[source + 1], row[source + 2], transparency)),
                3 => PaletteRgba(row[source], palette, transparency),
                4 => PackRgba(row[source], row[source], row[source], row[source + 1]),
                6 => PackRgba(row[source], row[source + 1], row[source + 2], row[source + 3]),
                _ => throw new InvalidDataException($"不支持的 PNG colorType={colorType}。"),
            };
        }
    }

    private static byte AlphaForGray(byte gray, byte[]? transparency)
    {
        return transparency is { Length: >= 2 } && transparency[1] == gray ? (byte)0 : byte.MaxValue;
    }

    private static byte AlphaForRgb(byte r, byte g, byte b, byte[]? transparency)
    {
        return transparency is { Length: >= 6 } &&
            transparency[1] == r && transparency[3] == g && transparency[5] == b
            ? (byte)0
            : byte.MaxValue;
    }

    private static uint PaletteRgba(byte index, byte[]? palette, byte[]? transparency)
    {
        if (palette is null || (index * 3) + 2 >= palette.Length)
        {
            throw new InvalidDataException("Indexed PNG 缺少有效 PLTE。");
        }

        int offset = index * 3;
        byte alpha = transparency is not null && index < transparency.Length
            ? transparency[index]
            : byte.MaxValue;
        return PackRgba(palette[offset], palette[offset + 1], palette[offset + 2], alpha);
    }

    private static uint PackRgba(byte r, byte g, byte b, byte a)
    {
        return (uint)(r | (g << 8) | (b << 16) | (a << 24));
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpLeft = Math.Abs(estimate - upLeft);
        return distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft
            ? left
            : distanceUp <= distanceUpLeft ? up : upLeft;
    }
}
