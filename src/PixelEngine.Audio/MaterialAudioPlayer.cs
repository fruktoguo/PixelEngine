namespace PixelEngine.Audio;

/// <summary>
/// 基于 <see cref="MaterialAudioTable"/> 的音频事件播放器。
/// </summary>
/// <param name="table">材质音频表。</param>
/// <param name="buffers">cue 到 buffer 的解析器。</param>
/// <param name="settings">音频设置；为 <see langword="null"/> 时使用默认设置。</param>
public sealed class MaterialAudioPlayer(MaterialAudioTable table, IAudioCueBufferResolver buffers, AudioSettings? settings = null) : IAudioEventPlayer
{
    private readonly MaterialAudioTable _table = table ?? throw new ArgumentNullException(nameof(table));
    private readonly IAudioCueBufferResolver _buffers = buffers ?? throw new ArgumentNullException(nameof(buffers));
    private readonly AudioSettings _settings = (settings ?? new AudioSettings()).Validate();

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

        AudioVolumeCategory category = AudioEventTypeTraits.GetVolumeCategory(audioEvent.Type);
        voice.Play(buffer, playback.Gain * _settings.GetCategoryVolume(category), playback.Pitch);
        return true;
    }
}
