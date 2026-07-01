using System.Numerics;

namespace PixelEngine.Audio;

/// <summary>
/// 音频后端抽象。生产实现封装 OpenAL，测试实现可记录调用但不发声。
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>
    /// 创建一个 positional source。
    /// </summary>
    /// <returns>后端 source 句柄。</returns>
    uint CreateSource();

    /// <summary>
    /// 创建一个音频 buffer。
    /// </summary>
    /// <returns>后端 buffer 句柄。</returns>
    uint CreateBuffer();

    /// <summary>
    /// 删除 source。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    void DeleteSource(uint source);

    /// <summary>
    /// 删除 buffer。
    /// </summary>
    /// <param name="buffer">buffer 句柄。</param>
    void DeleteBuffer(uint buffer);

    /// <summary>
    /// 上传 PCM 数据到 buffer。
    /// </summary>
    /// <param name="buffer">buffer 句柄。</param>
    /// <param name="format">PCM 样本格式。</param>
    /// <param name="pcm">PCM 字节。</param>
    /// <param name="sampleRate">采样率。</param>
    void UploadBuffer(uint buffer, AudioSampleFormat format, ReadOnlySpan<byte> pcm, int sampleRate);

    /// <summary>
    /// 将一组 buffer 排入 streaming source。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="buffers">buffer 句柄列表。</param>
    void QueueBuffers(uint source, ReadOnlySpan<uint> buffers);

    /// <summary>
    /// 从 streaming source 取回已处理 buffer。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="destination">目标 buffer 句柄列表。</param>
    /// <returns>实际取回数量。</returns>
    int UnqueueProcessedBuffers(uint source, Span<uint> destination);

    /// <summary>
    /// 查询 source 已处理的 queued buffer 数量。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>已处理 buffer 数。</returns>
    int GetProcessedBufferCount(uint source);

    /// <summary>
    /// 配置 source 的距离衰减参数。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="settings">音频设置。</param>
    void ConfigureSource(uint source, AudioSettings settings);

    /// <summary>
    /// 播放静态 buffer。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="buffer">OpenAL buffer 句柄；测试后端可接受任意非零值。</param>
    /// <param name="position">米空间位置。</param>
    /// <param name="gain">增益。</param>
    /// <param name="pitch">音高。</param>
    void Play(uint source, uint buffer, in Vector3 position, float gain, float pitch);

    /// <summary>
    /// 设置 source 增益。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="gain">线性增益。</param>
    void SetSourceGain(uint source, float gain);

    /// <summary>
    /// 设置 source 是否循环播放当前 buffer。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="looping">是否循环。</param>
    void SetSourceLooping(uint source, bool looping);

    /// <summary>
    /// 停止 source。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    void Stop(uint source);

    /// <summary>
    /// 查询 source 状态。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>播放状态。</returns>
    AudioSourceState GetState(uint source);

    /// <summary>
    /// 更新 listener 状态。
    /// </summary>
    /// <param name="listener">listener 状态。</param>
    void SetListener(in AudioListenerState listener);
}
