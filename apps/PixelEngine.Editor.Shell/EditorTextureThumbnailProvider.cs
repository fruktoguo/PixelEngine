using PixelEngine.Rendering;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Project Window 生产纹理缩略图：真实解码常见图片，按文件签名缓存 GL texture，并在失效/关闭时释放。
/// </summary>
internal sealed class EditorTextureThumbnailProvider : IEditorTextureThumbnailLeaseProvider, IDisposable
{
    private const int DefaultCapacity = 256;
    private readonly string _contentRoot;
    private readonly IEditorThumbnailDecoder _decoder;
    private readonly IEditorThumbnailTextureBackend _textures;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, RetiredEntry> _retired = [];
    private readonly Dictionary<string, FailedEntry> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;
    private long _accessSequence;
    private bool _disposed;

    public EditorTextureThumbnailProvider(string contentRoot, RenderWindow window, int capacity = DefaultCapacity)
        : this(contentRoot, StbEditorThumbnailDecoder.Instance, new GlEditorThumbnailTextureBackend(window), capacity)
    {
    }

    internal EditorTextureThumbnailProvider(
        string contentRoot,
        IEditorThumbnailDecoder decoder,
        IEditorThumbnailTextureBackend textures,
        int capacity = DefaultCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _contentRoot = Path.GetFullPath(contentRoot);
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _capacity = Math.Max(8, capacity);
    }

    public bool TryGetThumbnail(string assetPath, out AssetThumbnail thumbnail)
    {
        return TryResolveThumbnail(assetPath, acquireLease: false, out thumbnail);
    }

    /// <inheritdoc />
    public bool TryAcquireThumbnail(string assetPath, out AssetThumbnail thumbnail)
    {
        return TryResolveThumbnail(assetPath, acquireLease: true, out thumbnail);
    }

    /// <inheritdoc />
    public void ReleaseThumbnail(string assetPath, uint textureHandle)
    {
        if (_disposed || textureHandle == 0 || !TryResolvePath(assetPath, out string fullPath))
        {
            return;
        }

        if (_cache.TryGetValue(fullPath, out CacheEntry current) &&
            current.Thumbnail.TextureHandle == textureHandle)
        {
            if (current.LeaseCount > 0)
            {
                _cache[fullPath] = current with
                {
                    LeaseCount = current.LeaseCount - 1,
                    LastAccess = ++_accessSequence,
                };
                TrimCacheToCapacity();
            }

            return;
        }

        if (!_retired.TryGetValue(textureHandle, out RetiredEntry retired) ||
            !string.Equals(retired.FullPath, fullPath, StringComparison.OrdinalIgnoreCase) ||
            retired.LeaseCount <= 0)
        {
            return;
        }

        int remainingLeases = retired.LeaseCount - 1;
        if (remainingLeases == 0)
        {
            _ = _retired.Remove(textureHandle);
            _textures.Release(textureHandle);
        }
        else
        {
            _retired[textureHandle] = retired with { LeaseCount = remainingLeases };
        }

        TrimCacheToCapacity();
    }

    private bool TryResolveThumbnail(string assetPath, bool acquireLease, out AssetThumbnail thumbnail)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        thumbnail = default;
        if (!IsSupportedImage(assetPath) || !TryResolvePath(assetPath, out string fullPath) || !File.Exists(fullPath))
        {
            return false;
        }

        FileInfo info = new(fullPath);
        FileSignature signature = new(info.Length, info.LastWriteTimeUtc.Ticks);
        if (_cache.TryGetValue(fullPath, out CacheEntry cached) && cached.Signature == signature)
        {
            _cache[fullPath] = cached with
            {
                LeaseCount = acquireLease ? checked(cached.LeaseCount + 1) : cached.LeaseCount,
                LastAccess = ++_accessSequence,
            };
            thumbnail = cached.Thumbnail;
            return true;
        }

        if (_failures.TryGetValue(fullPath, out FailedEntry failed) && failed.Signature == signature)
        {
            _failures[fullPath] = failed with { LastAccess = ++_accessSequence };
            return false;
        }

        try
        {
            EditorDecodedThumbnail decoded = _decoder.Decode(fullPath);
            AssetThumbnail uploaded = _textures.Upload(in decoded);
            if (cached.Thumbnail.TextureHandle == 0)
            {
                TrimCacheForInsertion();
            }
            else
            {
                RetireOrReleaseInvalidatedEntry(fullPath, in cached);
            }

            _cache[fullPath] = new CacheEntry(
                signature,
                uploaded,
                acquireLease ? 1 : 0,
                ++_accessSequence);
            _ = _failures.Remove(fullPath);
            thumbnail = uploaded;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException)
        {
            if (cached.Thumbnail.TextureHandle != 0 &&
                _cache.TryGetValue(fullPath, out CacheEntry invalidated) &&
                invalidated.Thumbnail.TextureHandle == cached.Thumbnail.TextureHandle)
            {
                RetireOrReleaseInvalidatedEntry(fullPath, in invalidated);
            }

            RecordFailure(fullPath, signature);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (CacheEntry entry in _cache.Values)
        {
            _textures.Release(entry.Thumbnail.TextureHandle);
        }

        foreach (RetiredEntry entry in _retired.Values)
        {
            _textures.Release(entry.Thumbnail.TextureHandle);
        }

        _cache.Clear();
        _retired.Clear();
        _failures.Clear();
        _textures.Dispose();
        _disposed = true;
    }

    private static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga";
    }

    private bool TryResolvePath(string assetPath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(assetPath) || Path.IsPathRooted(assetPath))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(
            _contentRoot,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.EndsInDirectorySeparator(_contentRoot)
            ? _contentRoot
            : _contentRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(prefix, comparison))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private void RetireOrReleaseInvalidatedEntry(string fullPath, in CacheEntry cached)
    {
        _ = _cache.Remove(fullPath);
        if (cached.LeaseCount == 0)
        {
            _textures.Release(cached.Thumbnail.TextureHandle);
            return;
        }

        _retired.Add(
            cached.Thumbnail.TextureHandle,
            new RetiredEntry(fullPath, cached.Thumbnail, cached.LeaseCount));
    }

    private void TrimCacheForInsertion()
    {
        while (_cache.Count >= _capacity && TryEvictOldestUnleased())
        {
        }
    }

    private void TrimCacheToCapacity()
    {
        while (_cache.Count > _capacity && TryEvictOldestUnleased())
        {
        }
    }

    private bool TryEvictOldestUnleased()
    {
        string? oldestPath = null;
        CacheEntry oldest = default;
        foreach (KeyValuePair<string, CacheEntry> pair in _cache)
        {
            if (pair.Value.LeaseCount != 0 ||
                (oldestPath is not null && pair.Value.LastAccess >= oldest.LastAccess))
            {
                continue;
            }

            oldestPath = pair.Key;
            oldest = pair.Value;
        }

        if (oldestPath is null)
        {
            return false;
        }

        _ = _cache.Remove(oldestPath);
        _textures.Release(oldest.Thumbnail.TextureHandle);
        return true;
    }

    private void RecordFailure(string fullPath, FileSignature signature)
    {
        if (!_failures.ContainsKey(fullPath))
        {
            while (_failures.Count >= _capacity)
            {
                EvictOldestFailure();
            }
        }

        _failures[fullPath] = new FailedEntry(signature, ++_accessSequence);
    }

    private void EvictOldestFailure()
    {
        string? oldestPath = null;
        long oldestAccess = long.MaxValue;
        foreach (KeyValuePair<string, FailedEntry> pair in _failures)
        {
            if (pair.Value.LastAccess >= oldestAccess)
            {
                continue;
            }

            oldestPath = pair.Key;
            oldestAccess = pair.Value.LastAccess;
        }

        if (oldestPath is not null)
        {
            _ = _failures.Remove(oldestPath);
        }
    }

    private readonly record struct FileSignature(long Length, long LastWriteTicks);

    private readonly record struct CacheEntry(
        FileSignature Signature,
        AssetThumbnail Thumbnail,
        int LeaseCount,
        long LastAccess);

    private readonly record struct RetiredEntry(
        string FullPath,
        AssetThumbnail Thumbnail,
        int LeaseCount);

    private readonly record struct FailedEntry(FileSignature Signature, long LastAccess);
}

/// <summary>
/// Editor Shell 缩略图 provider 的 lease 扩展。文件失效或 LRU 淘汰不得释放仍被 Project Window 使用的纹理。
/// </summary>
internal interface IEditorTextureThumbnailLeaseProvider : ITextureThumbnailProvider
{
    /// <summary>取得一个在显式释放前持续有效的缩略图。</summary>
    bool TryAcquireThumbnail(string assetPath, out AssetThumbnail thumbnail);

    /// <summary>释放先前取得的缩略图 lease。</summary>
    void ReleaseThumbnail(string assetPath, uint textureHandle);
}

internal readonly record struct EditorDecodedThumbnail(int Width, int Height, byte[] Rgba)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        ArgumentNullException.ThrowIfNull(Rgba);
        if (Rgba.Length != checked(Width * Height * 4))
        {
            throw new InvalidDataException("缩略图 RGBA 数据长度与图片尺寸不一致。");
        }
    }
}

internal interface IEditorThumbnailDecoder
{
    EditorDecodedThumbnail Decode(string fullPath);
}

internal sealed class StbEditorThumbnailDecoder : IEditorThumbnailDecoder
{
    private const int MaximumDimension = 8192;
    private const long MaximumPixels = 16_777_216;
    internal const int MaximumThumbnailDimension = 256;
    public static StbEditorThumbnailDecoder Instance { get; } = new();

    private StbEditorThumbnailDecoder()
    {
    }

    public EditorDecodedThumbnail Decode(string fullPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            if (ImageInfo.FromStream(stream) is not { } info)
            {
                throw new InvalidDataException($"无法识别图片格式：{fullPath}");
            }

            if (info.Width <= 0 ||
                info.Height <= 0 ||
                info.Width > MaximumDimension ||
                info.Height > MaximumDimension ||
                checked((long)info.Width * info.Height) > MaximumPixels)
            {
                throw new InvalidDataException(
                    $"图片尺寸无效或超过缩略图安全上限：{info.Width}×{info.Height}，path={fullPath}");
            }

            stream.Position = 0;
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return FitToThumbnail(new EditorDecodedThumbnail(image.Width, image.Height, image.Data));
        }
        catch (Exception exception) when (exception is not (IOException or UnauthorizedAccessException or OutOfMemoryException))
        {
            throw new InvalidDataException($"无法解码图片缩略图：{fullPath}", exception);
        }
    }

    /// <summary>
    /// 把解码结果限制在缩略图纹理预算内。Project Window 最大只显示 128 px，保留 2x 像素密度即可；
    /// 绝不能把 4K/8K 原图直接常驻到最多 256 个 GL texture 中。
    /// </summary>
    internal static EditorDecodedThumbnail FitToThumbnail(in EditorDecodedThumbnail source)
    {
        source.Validate();
        if (source.Width <= MaximumThumbnailDimension && source.Height <= MaximumThumbnailDimension)
        {
            return source;
        }

        double scale = Math.Min(
            MaximumThumbnailDimension / (double)source.Width,
            MaximumThumbnailDimension / (double)source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero));
        byte[] rgba = new byte[checked(width * height * 4)];

        // 双线性缩放只发生在资产刷新/失效时，不进入 runtime 帧热路径；它避免大图缩略图出现锯齿，
        // 同时把每个缓存纹理的上限收敛到 256 KiB（RGBA8 256x256）。
        for (int targetY = 0; targetY < height; targetY++)
        {
            float sourceY = ((targetY + 0.5f) * source.Height / height) - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sourceY), 0, source.Height - 1);
            int y1 = Math.Min(y0 + 1, source.Height - 1);
            float fy = Math.Clamp(sourceY - y0, 0f, 1f);
            for (int targetX = 0; targetX < width; targetX++)
            {
                float sourceX = ((targetX + 0.5f) * source.Width / width) - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(sourceX), 0, source.Width - 1);
                int x1 = Math.Min(x0 + 1, source.Width - 1);
                float fx = Math.Clamp(sourceX - x0, 0f, 1f);
                int destination = ((targetY * width) + targetX) * 4;
                int topLeft = ((y0 * source.Width) + x0) * 4;
                int topRight = ((y0 * source.Width) + x1) * 4;
                int bottomLeft = ((y1 * source.Width) + x0) * 4;
                int bottomRight = ((y1 * source.Width) + x1) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    float top = Lerp(source.Rgba[topLeft + channel], source.Rgba[topRight + channel], fx);
                    float bottom = Lerp(source.Rgba[bottomLeft + channel], source.Rgba[bottomRight + channel], fx);
                    rgba[destination + channel] = (byte)Math.Clamp(
                        (int)MathF.Round(Lerp(top, bottom, fy)),
                        byte.MinValue,
                        byte.MaxValue);
                }
            }
        }

        return new EditorDecodedThumbnail(width, height, rgba);
    }

    private static float Lerp(float left, float right, float amount)
    {
        return left + ((right - left) * amount);
    }
}

internal interface IEditorThumbnailTextureBackend : IDisposable
{
    AssetThumbnail Upload(in EditorDecodedThumbnail decoded);

    void Release(uint textureHandle);
}

internal sealed unsafe class GlEditorThumbnailTextureBackend(RenderWindow window) : IEditorThumbnailTextureBackend
{
    private readonly RenderWindow _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<uint, GlTexture> _textures = [];
    private bool _disposed;

    public AssetThumbnail Upload(in EditorDecodedThumbnail decoded)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureOwnerThread();
        decoded.Validate();
        GlTexture texture = new(
            _window.Gl,
            decoded.Width,
            decoded.Height,
            InternalFormat.Rgba8,
            PixelFormat.Rgba,
            PixelType.UnsignedByte);
        try
        {
            texture.Bind();
            _window.Gl.GetInteger(GLEnum.UnpackAlignment, out int previousAlignment);
            _window.Gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            try
            {
                fixed (byte* pixels = decoded.Rgba)
                {
                    _window.Gl.TexSubImage2D(
                        TextureTarget.Texture2D,
                        level: 0,
                        xoffset: 0,
                        yoffset: 0,
                        (uint)decoded.Width,
                        (uint)decoded.Height,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixels);
                }
            }
            finally
            {
                _window.Gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment);
            }

            _textures.Add(texture.Handle, texture);
            return new AssetThumbnail(texture.Handle, decoded.Width, decoded.Height);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    public void Release(uint textureHandle)
    {
        if (textureHandle == 0 || !_textures.Remove(textureHandle, out GlTexture? texture))
        {
            return;
        }

        EnsureOwnerThread();
        texture.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        EnsureOwnerThread();
        foreach (GlTexture texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _disposed = true;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Project Window GL 缩略图必须在创建它的渲染线程上传与释放。");
        }
    }
}
