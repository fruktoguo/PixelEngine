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
    /// 删除 source。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    void DeleteSource(uint source);

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
