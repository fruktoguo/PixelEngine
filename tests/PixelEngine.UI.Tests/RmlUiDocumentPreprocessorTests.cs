using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class RmlUiDocumentPreprocessorTests
{
    [Fact]
    public void PrepareRewritesDataImagePngToRmlUiReadableTga()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-preprocess-{Guid.NewGuid():N}");
        string screens = Path.Combine(root, "screens");
        string images = Path.Combine(root, "images");
        _ = Directory.CreateDirectory(screens);
        _ = Directory.CreateDirectory(images);
        string imagePath = Path.Combine(images, "logo.png");
        string documentPath = Path.Combine(screens, "main.rml");
        WritePng(imagePath, 3, 2);
        File.WriteAllText(
            documentPath,
            """
            <rml>
              <body>
                <img id="logo" data-image="logo" width="24" height="16" />
              </body>
            </rml>
            """);

        try
        {
            using RmlUiImageAssetCache cache = new();
            string processed = RmlUiDocumentPreprocessor.Prepare(documentPath, cache);
            XDocument document = XDocument.Parse(processed);
            string src = Assert.Single(document.Descendants("img")).Attribute("src")?.Value ??
                throw new InvalidDataException("预处理后的 img 缺少 src。");
            string tgaPath = src.Replace('/', Path.DirectorySeparatorChar);
            Assert.True(File.Exists(tgaPath));

            byte[] bytes = File.ReadAllBytes(tgaPath);
            Assert.True(bytes.Length >= 18 + (3 * 2 * 4));
            Assert.Equal(2, bytes[2]);
            Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2)));
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2)));
            Assert.Equal(32, bytes[16]);
            Assert.Equal(0x28, bytes[17]);
            Assert.Equal(160, bytes[18]);
            Assert.Equal(0, bytes[19]);
            Assert.Equal(0, bytes[20]);
            Assert.Equal(255, bytes[21]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PrepareRejectsImageOutsideContentImagesDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-preprocess-{Guid.NewGuid():N}");
        string screens = Path.Combine(root, "screens");
        string images = Path.Combine(root, "images");
        string other = Path.Combine(root, "other");
        _ = Directory.CreateDirectory(screens);
        _ = Directory.CreateDirectory(images);
        _ = Directory.CreateDirectory(other);
        string imagePath = Path.Combine(other, "logo.png");
        string documentPath = Path.Combine(screens, "main.rml");
        WritePng(imagePath, 1, 1);
        File.WriteAllText(
            documentPath,
            """
            <rml>
              <body>
                <img id="logo" src="../other/logo.png" />
              </body>
            </rml>
            """);

        try
        {
            using RmlUiImageAssetCache cache = new();
            _ = Assert.Throws<InvalidDataException>(() => RmlUiDocumentPreprocessor.Prepare(documentPath, cache));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        using MemoryStream idat = new();
        using (ZLibStream zlib = new(idat, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    zlib.WriteByte((byte)(x * 40));
                    zlib.WriteByte((byte)(y * 80));
                    zlib.WriteByte(160);
                    zlib.WriteByte(255);
                }
            }
        }

        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(file, "IHDR"u8, ihdr);
        WriteChunk(file, "IDAT"u8, idat.ToArray());
        WriteChunk(file, "IEND"u8, []);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        stream.Write(stackalloc byte[4]);
    }
}
