namespace PixelEngine.Audio;

/// <summary>
/// 引用计数音频 clip 句柄。
/// </summary>
public sealed class AudioClip
{
    internal AudioClip(string assetId, AudioBuffer buffer)
    {
        AssetId = assetId;
        Buffer = buffer;
        RefCount = 1;
    }

    /// <summary>
    /// 资产 id。
    /// </summary>
    public string AssetId { get; }

    /// <summary>
    /// 已上传 buffer。
    /// </summary>
    public AudioBuffer Buffer { get; }

    /// <summary>
    /// 当前引用计数。
    /// </summary>
    public int RefCount { get; private set; }

    internal void AddRef()
    {
        RefCount++;
    }

    internal int Release()
    {
        RefCount--;
        return RefCount;
    }
}
