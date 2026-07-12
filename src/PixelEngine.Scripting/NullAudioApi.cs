namespace PixelEngine.Scripting;

/// <summary>
/// 没有音频内容时使用的显式无声脚本后端。
/// </summary>
/// <remarks>
/// 该后端只替代缺失的可选音频内容，不掩盖非法 cue、坐标或音量参数。
/// </remarks>
public sealed class NullAudioApi : IAudioApi
{
    private NullAudioApi()
    {
    }

    /// <summary>
    /// 共享无状态实例。
    /// </summary>
    public static NullAudioApi Instance { get; } = new();

    /// <inheritdoc />
    public void PlayOneShot(string cue, float volume = 1f)
    {
        Validate(cue, 0f, 0f, volume);
    }

    /// <inheritdoc />
    public void PlayAt(string cue, float x, float y, float volume = 1f)
    {
        Validate(cue, x, y, volume);
    }

    private static void Validate(string cue, float x, float y, float volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cue);
        if (!float.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "音效 X 坐标必须是有限值。");
        }

        if (!float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "音效 Y 坐标必须是有限值。");
        }

        if (!float.IsFinite(volume) || volume < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "音量必须是有限非负值。");
        }
    }
}
