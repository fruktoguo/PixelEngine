namespace PixelEngine.Audio;

/// <summary>
/// 音频资产字节源。Content / Demo 可用文件系统、包体或虚拟资源实现该接口。
/// </summary>
public interface IAudioAssetStore
{
    /// <summary>
    /// 异步读取音频资产字节。
    /// </summary>
    /// <param name="assetId">资产 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>资产字节。</returns>
    ValueTask<byte[]> LoadBytesAsync(string assetId, CancellationToken cancellationToken = default);
}
