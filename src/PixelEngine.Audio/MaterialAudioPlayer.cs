namespace PixelEngine.Audio;

/// <summary>
/// 基于 <see cref="MaterialAudioTable"/> 的音频事件播放器。
/// </summary>
/// <param name="table">材质音频表。</param>
/// <param name="buffers">cue 到 buffer 的解析器。</param>
public sealed class MaterialAudioPlayer(MaterialAudioTable table, IAudioCueBufferResolver buffers) : IAudioEventPlayer
{
    private readonly MaterialAudioTable _table = table ?? throw new ArgumentNullException(nameof(table));
    private readonly IAudioCueBufferResolver _buffers = buffers ?? throw new ArgumentNullException(nameof(buffers));

    /// <inheritdoc />
    public bool TryPlay(in CoalescedAudioEvent audioEvent, AudioVoice voice, long tick)
    {
        ArgumentNullException.ThrowIfNull(voice);
        if (!_table.TryResolve(in audioEvent, tick, out MaterialAudioPlayback playback))
        {
            return false;
        }

        if (!_buffers.TryResolveBuffer(playback.CueHandle, out uint buffer))
        {
            return false;
        }

        voice.Play(buffer, playback.Gain, playback.Pitch);
        return true;
    }
}
