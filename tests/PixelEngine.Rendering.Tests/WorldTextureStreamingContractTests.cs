using System.Text.RegularExpressions;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 世界纹理流式契约测试：区块上传与驻留同步。
/// </summary>
public sealed class WorldTextureStreamingContractTests
{
    /// <summary>
    /// 验证Render Buffer Exposes Bgra Span And调整大小行为符合预期。
    /// </summary>
    [Fact]
    public void RenderBufferExposesBgraSpanAndResizes()
    {
        RenderBuffer buffer = new(3, 2);

        Assert.Equal(3, buffer.Width);
        Assert.Equal(2, buffer.Height);
        Assert.Equal(6 * sizeof(uint), buffer.ByteLength);
        Assert.Equal(6, buffer.Pixels.Length);

        buffer.Pixels[0] = 0xFF336699u;
        buffer.Pixels[5] = 0x80112233u;

        Assert.Equal(0xFF336699u, buffer.Pixels[0]);
        Assert.Equal(0x80112233u, buffer.Pixels[^1]);

        buffer.Resize(3, 2);
        Assert.Equal(0xFF336699u, buffer.Pixels[0]);
        Assert.Equal(0x80112233u, buffer.Pixels[^1]);

        buffer.Resize(4, 3);

        Assert.Equal(4, buffer.Width);
        Assert.Equal(3, buffer.Height);
        Assert.Equal(12 * sizeof(uint), buffer.ByteLength);
        Assert.Equal(12, buffer.Pixels.Length);
    }

    /// <summary>
    /// 验证Render Buffer Rejects Invalid Sizes行为符合预期。
    /// </summary>
    [Fact]
    public void RenderBufferRejectsInvalidSizes()
    {
        AssertThrowsOutOfRange(() => new RenderBuffer(0, 1));
        AssertThrowsOutOfRange(() => new RenderBuffer(1, 0));

        RenderBuffer buffer = new(1, 1);

        AssertThrowsOutOfRange(() => buffer.Resize(-1, 1));
        AssertThrowsOutOfRange(() => buffer.Resize(1, -1));
    }

    /// <summary>
    /// 验证Dirty Rect Upload Validation Rejects Empty And Out Of Bounds Rects。
    /// </summary>
    [Fact]
    public void DirtyRectUploadValidationRejectsEmptyAndOutOfBoundsRects()
    {
        RenderBuffer buffer = new(8, 4);

        buffer.ValidateRect(new PixelUploadRect(0, 0, 8, 4));
        buffer.ValidateRect(new PixelUploadRect(2, 1, 3, 2));
        buffer.ValidateRect(new PixelUploadRect(7, 3, 1, 1));

        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(0, 0, 0, 1)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(0, 0, 1, 0)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(-1, 0, 1, 1)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(0, -1, 1, 1)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(8, 0, 1, 1)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(0, 4, 1, 1)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(6, 0, 3, 1)));
        AssertThrowsOutOfRange(() => buffer.ValidateRect(new PixelUploadRect(0, 2, 1, 3)));
    }

    /// <summary>
    /// 验证Render Buffer Source Uses Pinned Uint Bgra Storage行为符合预期。
    /// </summary>
    [Fact]
    public void RenderBufferSourceUsesPinnedUintBgraStorage()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderBuffer.cs"));

        Assert.Contains("GC.AllocateArray<uint>", source, StringComparison.Ordinal);
        Assert.Matches(@"pinned\s*:\s*true", source);
        Assert.Contains("Span<uint>", source, StringComparison.Ordinal);
        Assert.Contains("sizeof(uint)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Rendering Sources Do Not Introduce Per Chunk Texture Path行为符合预期。
    /// </summary>
    [Fact]
    public void RenderingSourcesDoNotIntroducePerChunkTexturePath()
    {
        string source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(ProjectPath("src", "PixelEngine.Rendering"), "*.cs")
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        Assert.Contains("per-chunk texture", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ChunkTexture", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PerChunkTexture", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"(?:Dictionary|ConcurrentDictionary)\s*<\s*" + Regex.Escape(nameof(ChunkCoord)) + @"\s*,\s*GlTexture",
            source);
    }

    /// <summary>
    /// 验证Pbo Uploader Exposes Non Default Persistent Mapped Path。
    /// </summary>
    [Fact]
    public void PboUploaderExposesNonDefaultPersistentMappedPath()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PboUploader.cs"));
        string modeSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PboUploadMode.cs"));
        string bufferSource = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GlBuffer.cs"));

        Assert.Contains("PboUploadMode.OrphanMap", source, StringComparison.Ordinal);
        Assert.Contains("PboUploadMode.PersistentMapped", source, StringComparison.Ordinal);
        Assert.Contains("HasBufferStorage", source, StringComparison.Ordinal);
        Assert.Contains("BufferStorage", bufferSource, StringComparison.Ordinal);
        Assert.Contains("MapPersistentBit", source, StringComparison.Ordinal);
        Assert.Contains("MapCoherentBit", source, StringComparison.Ordinal);
        Assert.Contains("FenceSync", source, StringComparison.Ordinal);
        Assert.Contains("ClientWaitSync", source, StringComparison.Ordinal);
        Assert.Contains("默认路径", modeSource, StringComparison.Ordinal);
        Assert.Contains("仅在", modeSource, StringComparison.Ordinal);
    }

    private static string ProjectPath(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            path = Directory.GetParent(path)!.FullName;
        }

        return Path.Combine([path, .. parts]);
    }

    private static void AssertThrowsOutOfRange(Action action)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
