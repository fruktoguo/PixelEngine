using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Window 生产缩略图缓存的无 GL 单元测试。
/// </summary>
public sealed class EditorTextureThumbnailProviderTests
{
    /// <summary>
    /// 验证相同文件签名复用纹理，文件变化后只释放旧纹理并重新上传一次。
    /// </summary>
    [Fact]
    public void ProviderCachesByFileSignatureAndReleasesInvalidatedTextures()
    {
        using TemporaryDirectory temp = new();
        string path = Path.Combine(temp.Path, "image.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        RecordingDecoder decoder = new();
        RecordingTextureBackend textures = new();
        using (EditorTextureThumbnailProvider provider = new(temp.Path, decoder, textures))
        {
            Assert.True(provider.TryGetThumbnail("image.png", out AssetThumbnail first));
            Assert.True(provider.TryGetThumbnail("image.png", out AssetThumbnail cached));
            Assert.Equal(first, cached);
            Assert.Equal(1, decoder.DecodeCount);
            Assert.Equal(1, textures.UploadCount);

            File.AppendAllText(path, "changed");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            Assert.True(provider.TryGetThumbnail("image.png", out AssetThumbnail changed));
            Assert.NotEqual(first.TextureHandle, changed.TextureHandle);
            Assert.Equal(2, decoder.DecodeCount);
            Assert.Equal(2, textures.UploadCount);
            Assert.Contains(first.TextureHandle, textures.Released);
        }

        Assert.Equal(2, textures.Released.Distinct().Count());
        Assert.True(textures.Disposed);
    }

    /// <summary>
    /// 验证常见图片格式可进入解码链，未知格式和越界路径不会触发读取。
    /// </summary>
    [Fact]
    public void ProviderAcceptsCommonRasterFormatsAndRejectsEscapesAndUnsupportedFiles()
    {
        using TemporaryDirectory temp = new();
        foreach (string name in new[] { "a.png", "b.jpg", "c.jpeg", "d.bmp", "e.tga", "f.webp" })
        {
            File.WriteAllBytes(Path.Combine(temp.Path, name), [1]);
        }

        RecordingDecoder decoder = new();
        using RecordingTextureBackend textures = new();
        using EditorTextureThumbnailProvider provider = new(temp.Path, decoder, textures);
        Assert.True(provider.TryGetThumbnail("a.png", out _));
        Assert.True(provider.TryGetThumbnail("b.jpg", out _));
        Assert.True(provider.TryGetThumbnail("c.jpeg", out _));
        Assert.True(provider.TryGetThumbnail("d.bmp", out _));
        Assert.True(provider.TryGetThumbnail("e.tga", out _));
        Assert.False(provider.TryGetThumbnail("f.webp", out _));
        Assert.False(provider.TryGetThumbnail("../outside.png", out _));
        Assert.Equal(5, decoder.DecodeCount);
    }

    /// <summary>
    /// 验证损坏或尺寸溢出的图片只降级为无缩略图，不会把异常传播成 Editor 闪退。
    /// </summary>
    [Fact]
    public void ProviderContainsMalformedAndOversizeDecodeFailures()
    {
        using TemporaryDirectory temp = new();
        string brokenPath = Path.Combine(temp.Path, "broken.png");
        File.WriteAllBytes(brokenPath, [1, 2, 3]);
        using RecordingTextureBackend textures = new();
        ThrowingDecoder malformedDecoder = new(new InvalidDataException("broken"));
        using EditorTextureThumbnailProvider malformed = new(
            temp.Path,
            malformedDecoder,
            textures);
        Assert.False(malformed.TryGetThumbnail("broken.png", out _));
        Assert.False(malformed.TryGetThumbnail("broken.png", out _));
        Assert.Equal(1, malformedDecoder.DecodeCount);

        File.AppendAllText(brokenPath, "changed");
        File.SetLastWriteTimeUtc(brokenPath, DateTime.UtcNow.AddSeconds(2));
        Assert.False(malformed.TryGetThumbnail("broken.png", out _));
        Assert.Equal(2, malformedDecoder.DecodeCount);

        using EditorTextureThumbnailProvider oversize = new(
            temp.Path,
            new ThrowingDecoder(new OverflowException("oversize")),
            new RecordingTextureBackend());
        Assert.False(oversize.TryGetThumbnail("broken.png", out _));
    }

    /// <summary>
    /// 验证缓存容量不足时只淘汰无 lease 项；全部可见项被 pin 时允许短暂超量，绝不返回悬空 GL handle。
    /// </summary>
    [Fact]
    public void ProviderNeverEvictsLeasedTexturesWhenCapacityIsExceeded()
    {
        const int Capacity = 8;
        using TemporaryDirectory temp = new();
        for (int i = 0; i <= Capacity; i++)
        {
            File.WriteAllBytes(Path.Combine(temp.Path, $"image-{i}.png"), [(byte)i]);
        }

        RecordingDecoder decoder = new();
        RecordingTextureBackend textures = new();
        uint[] handles = new uint[Capacity + 1];
        using (EditorTextureThumbnailProvider provider = new(temp.Path, decoder, textures, Capacity))
        {
            for (int i = 0; i < handles.Length; i++)
            {
                Assert.True(provider.TryAcquireThumbnail($"image-{i}.png", out AssetThumbnail thumbnail));
                handles[i] = thumbnail.TextureHandle;
            }

            Assert.Empty(textures.Released);

            provider.ReleaseThumbnail("image-0.png", handles[0]);

            Assert.Equal([handles[0]], textures.Released);
            Assert.DoesNotContain(handles[1], textures.Released);
            Assert.DoesNotContain(handles[^1], textures.Released);
        }

        Assert.Equal(handles.Order(), textures.Released.Distinct().Order());
    }

    /// <summary>
    /// 验证文件签名变化时旧版本若仍被绘制会进入 retired 集合，直到其 lease 显式释放才删除纹理。
    /// </summary>
    [Fact]
    public void ProviderRetiresInvalidatedTextureUntilItsLeaseIsReleased()
    {
        using TemporaryDirectory temp = new();
        string path = Path.Combine(temp.Path, "image.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        RecordingDecoder decoder = new();
        RecordingTextureBackend textures = new();
        uint originalHandle;
        uint changedHandle;
        using (EditorTextureThumbnailProvider provider = new(temp.Path, decoder, textures))
        {
            Assert.True(provider.TryAcquireThumbnail("image.png", out AssetThumbnail original));
            originalHandle = original.TextureHandle;

            File.AppendAllText(path, "changed");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            Assert.True(provider.TryAcquireThumbnail("image.png", out AssetThumbnail changed));
            changedHandle = changed.TextureHandle;

            Assert.NotEqual(originalHandle, changedHandle);
            Assert.DoesNotContain(originalHandle, textures.Released);

            provider.ReleaseThumbnail("image.png", originalHandle);

            Assert.Equal([originalHandle], textures.Released);
            provider.ReleaseThumbnail("image.png", changedHandle);
            Assert.Equal([originalHandle], textures.Released);
        }

        Assert.Equal([originalHandle, changedHandle], textures.Released);
    }

    /// <summary>
    /// 验证生产解码器不会把高分辨率原图直接上传并常驻到 GL 缩略图缓存。
    /// </summary>
    [Fact]
    public void DecoderFitsLargeImagesInsideBoundedGpuThumbnailBudget()
    {
        const int SourceWidth = 1024;
        const int SourceHeight = 512;
        byte[] pixels = new byte[SourceWidth * SourceHeight * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 220;
            pixels[i + 1] = 120;
            pixels[i + 2] = 40;
            pixels[i + 3] = 255;
        }

        EditorDecodedThumbnail thumbnail = StbEditorThumbnailDecoder.FitToThumbnail(
            new EditorDecodedThumbnail(SourceWidth, SourceHeight, pixels));

        Assert.Equal(StbEditorThumbnailDecoder.MaximumThumbnailDimension, thumbnail.Width);
        Assert.Equal(StbEditorThumbnailDecoder.MaximumThumbnailDimension / 2, thumbnail.Height);
        Assert.Equal(thumbnail.Width * thumbnail.Height * 4, thumbnail.Rgba.Length);
        Assert.All(thumbnail.Rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal(255, alpha));
    }

    private sealed class RecordingDecoder : IEditorThumbnailDecoder
    {
        public int DecodeCount { get; private set; }

        public EditorDecodedThumbnail Decode(string fullPath)
        {
            Assert.True(File.Exists(fullPath));
            DecodeCount++;
            return new EditorDecodedThumbnail(2, 1, [255, 0, 0, 255, 0, 255, 0, 255]);
        }
    }

    private sealed class ThrowingDecoder(Exception exception) : IEditorThumbnailDecoder
    {
        public int DecodeCount { get; private set; }

        public EditorDecodedThumbnail Decode(string fullPath)
        {
            Assert.True(File.Exists(fullPath));
            DecodeCount++;
            throw exception;
        }
    }

    private sealed class RecordingTextureBackend : IEditorThumbnailTextureBackend
    {
        private uint _nextHandle = 10;

        public int UploadCount { get; private set; }

        public List<uint> Released { get; } = [];

        public bool Disposed { get; private set; }

        public AssetThumbnail Upload(in EditorDecodedThumbnail decoded)
        {
            decoded.Validate();
            UploadCount++;
            return new AssetThumbnail(_nextHandle++, decoded.Width, decoded.Height);
        }

        public void Release(uint textureHandle)
        {
            Released.Add(textureHandle);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PixelEngineThumbnailTests", Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
