using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.UI;
using Silk.NET.OpenGL;

namespace PixelEngine.Hosting;

/// <summary>
/// 把 ContentRoot 内的 PNG 世界视觉资产惰性解码为共享 GL 纹理；加载后按 stable asset id 复用。
/// </summary>
internal sealed class WorldVisualTextureCache(GL gl, string contentRoot) : IDisposable
{
    private readonly GL _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    private readonly string _contentRoot = NormalizeContentRoot(contentRoot);
    private readonly Dictionary<string, TextureEntry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// 解析并缓存一张位于 ContentRoot 内的 PNG 世界视觉纹理。
    /// </summary>
    /// <param name="asset">稳定 Texture 资产引用。</param>
    /// <returns>绑定到当前 GL context 的 overlay sprite。</returns>
    public OverlaySprite Resolve(ScriptAssetReference asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!asset.IsValid || asset.AssetType != ScriptAssetKind.Texture)
        {
            throw new ArgumentException("世界视觉层必须引用有效 Texture 资产。", nameof(asset));
        }

        string fullPath = ResolveAssetPath(_contentRoot, asset.LogicalPath);
        if (_entries.TryGetValue(asset.AssetId, out TextureEntry? existing))
        {
            return string.Equals(existing.Path, fullPath, StringComparison.OrdinalIgnoreCase)
                ? existing.Sprite
                : throw new InvalidDataException($"世界视觉资产 id '{asset.AssetId}' 同时指向多个路径。");
        }

        UiImageBitmap bitmap = UiPngImageLoader.Load(fullPath);
        GlTexture texture = new(
            _gl,
            bitmap.Width,
            bitmap.Height,
            InternalFormat.Srgb8Alpha8,
            PixelFormat.Rgba,
            PixelType.UnsignedByte);
        try
        {
            texture.UploadPixels(bitmap.Pixels, PixelFormat.Rgba, PixelType.UnsignedByte);
            OverlaySprite sprite = new(texture.Handle, texture.Width, texture.Height);
            _entries.Add(asset.AssetId, new TextureEntry(fullPath, texture, sprite));
            return sprite;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 释放此缓存持有的全部 GL 纹理。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (TextureEntry entry in _entries.Values)
        {
            entry.Texture.Dispose();
        }

        _entries.Clear();
        _disposed = true;
    }

    internal static string ResolveAssetPath(string contentRoot, string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        string normalizedRoot = NormalizeContentRoot(contentRoot);
        string relative = Path.IsPathRooted(logicalPath)
            ? throw new InvalidDataException("世界视觉资产路径必须相对 ContentRoot。")
            : logicalPath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        return !fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidDataException("世界视觉资产路径越出 ContentRoot。")
            : !string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidDataException("世界视觉层当前只接受 PNG Texture 资产。")
            : File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException("世界视觉 PNG 资产不存在。", fullPath);
    }

    private static string NormalizeContentRoot(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        string fullPath = Path.GetFullPath(contentRoot);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private sealed record TextureEntry(string Path, GlTexture Texture, OverlaySprite Sprite);
}
