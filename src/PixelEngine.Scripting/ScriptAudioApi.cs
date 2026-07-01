using System.Numerics;
using PixelEngine.Audio;

namespace PixelEngine.Scripting;

/// <summary>
/// 将脚本音效播放请求桥接到真实 AudioSystem 与已加载 clip cache。
/// </summary>
/// <param name="audio">音频系统。</param>
/// <param name="clips">已加载音频 clip cache。</param>
public sealed class ScriptAudioApi(AudioSystem audio, AudioClipCache clips) : IAudioApi
{
    private readonly AudioSystem _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    private readonly AudioClipCache _clips = clips ?? throw new ArgumentNullException(nameof(clips));

    /// <inheritdoc />
    public void PlayAt(string cue, float x, float y, float volume = 1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cue);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(volume) || volume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "音效位置与音量必须是有限值，且音量不能为负。");
        }

        if (!_clips.TryGetLoaded(cue, out AudioClip? clip) || clip is null)
        {
            throw new InvalidOperationException($"脚本音效 cue 尚未加载：{cue}。");
        }

        Vector2 worldPos = new(x, y);
        if (!_audio.PlayOneShot(clip, in worldPos, volume))
        {
            throw new InvalidOperationException($"脚本音效 cue 无法取得可用 voice：{cue}。");
        }
    }
}
