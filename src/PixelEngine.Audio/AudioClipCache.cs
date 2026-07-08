namespace PixelEngine.Audio;

/// <summary>
/// 音频 clip 加载、解码、上传与引用计数缓存。
/// </summary>
/// <param name="backend">音频后端。</param>
/// <param name="assets">资产字节源。</param>
/// <param name="decoder">音频解码器。</param>
public sealed class AudioClipCache(IAudioBackend backend, IAudioAssetStore assets, IAudioDecoder decoder) : IDisposable
{
    private readonly IAudioBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private readonly IAudioAssetStore _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly IAudioDecoder _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
    private int _loadingCount;
    private bool _disposed;

    /// <summary>
    /// 已加载 clip 数。
    /// </summary>
    public int LoadedCount => _clips.Count;

    /// <summary>
    /// 正在加载的 clip 数。
    /// </summary>
    public int LoadingCount => Volatile.Read(ref _loadingCount);

    /// <summary>
    /// 异步加载 clip。若 clip 已缓存则增加引用计数并立即返回。
    /// </summary>
    /// <param name="assetId">资产 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载后的 clip。</returns>
    public async ValueTask<AudioClip> LoadAsync(string assetId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (_clips.TryGetValue(assetId, out AudioClip? existing))
        {
            existing.AddRef();
            return existing;
        }

        _ = Interlocked.Increment(ref _loadingCount);
        try
        {
            byte[] bytes = await _assets.LoadBytesAsync(assetId, cancellationToken).ConfigureAwait(false);
            if (!_decoder.TryDecode(bytes, out DecodedAudioData decoded))
            {
                throw new InvalidDataException($"音频资产无法由当前 decoder 解码：{assetId}");
            }

            uint handle = _backend.CreateBuffer();
            _backend.UploadBuffer(handle, decoded.Format, decoded.Pcm, decoded.SampleRate);
            AudioClip clip = new(assetId, new AudioBuffer(handle, decoded.Format, decoded.SampleRate, decoded.Pcm.Length));
            _clips.Add(assetId, clip);
            return clip;
        }
        finally
        {
            _ = Interlocked.Decrement(ref _loadingCount);
        }
    }

    /// <summary>
    /// 尝试获取已加载 clip，不改变引用计数。
    /// </summary>
    /// <param name="assetId">资产 id。</param>
    /// <param name="clip">clip。</param>
    /// <returns>是否已加载。</returns>
    public bool TryGetLoaded(string assetId, out AudioClip? clip)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _clips.TryGetValue(assetId, out clip);
    }

    /// <summary>
    /// 释放 clip 引用；引用计数归零时删除后端 buffer。
    /// </summary>
    /// <param name="clip">clip。</param>
    public void Unload(AudioClip clip)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(clip);
        if (!_clips.TryGetValue(clip.AssetId, out AudioClip? cached) || !ReferenceEquals(cached, clip))
        {
            return;
        }

        if (clip.Release() > 0)
        {
            return;
        }

        _ = _clips.Remove(clip.AssetId);
        _backend.DeleteBuffer(clip.Buffer.Handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (AudioClip clip in _clips.Values)
        {
            _backend.DeleteBuffer(clip.Buffer.Handle);
        }

        _clips.Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
