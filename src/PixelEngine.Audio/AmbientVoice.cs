using System.Numerics;

namespace PixelEngine.Audio;

/// <summary>
/// 固定 ambient source 槽位，负责循环播放和淡入淡出。
/// </summary>
public sealed class AmbientVoice
{
    private readonly IAudioBackend _backend;
    private float _fadeRate;

    internal AmbientVoice(IAudioBackend backend, uint source, int slotIndex, AudioSettings settings)
    {
        _backend = backend;
        Source = source;
        SlotIndex = slotIndex;
        _fadeRate = settings.AmbientFadeRate;
        _backend.ConfigureSource(source, settings);
    }

    /// <summary>
    /// ambient source 句柄。
    /// </summary>
    public uint Source { get; }

    /// <summary>
    /// 固定槽位索引。
    /// </summary>
    public int SlotIndex { get; private set; }

    internal void Configure(int slotIndex, AudioSettings settings)
    {
        SlotIndex = slotIndex;
        _fadeRate = settings.AmbientFadeRate;
        _backend.ConfigureSource(Source, settings);
    }

    /// <summary>
    /// 当前绑定材质 id。
    /// </summary>
    public ushort MaterialId { get; private set; }

    /// <summary>
    /// 当前 cue 句柄。
    /// </summary>
    public int CueHandle { get; private set; }

    /// <summary>
    /// 当前增益。
    /// </summary>
    public float Gain { get; private set; }

    /// <summary>
    /// 目标增益。
    /// </summary>
    public float TargetGain { get; private set; }

    /// <summary>
    /// 是否正在占用。
    /// </summary>
    public bool IsActive { get; private set; }

    internal void Begin(ushort materialId, int cueHandle, uint buffer, in Vector3 position, float targetGain)
    {
        MaterialId = materialId;
        CueHandle = cueHandle;
        Gain = 0f;
        TargetGain = Math.Clamp(targetGain, 0f, 1f);
        IsActive = true;
        _backend.SetSourceLooping(Source, true);
        _backend.Play(Source, buffer, in position, 0f, 1f);
    }

    internal void SetTarget(float targetGain)
    {
        TargetGain = Math.Clamp(targetGain, 0f, 1f);
    }

    internal void FadeOut()
    {
        TargetGain = 0f;
    }

    internal void Step()
    {
        if (!IsActive)
        {
            return;
        }

        if (Gain < TargetGain)
        {
            Gain = Math.Min(TargetGain, Gain + _fadeRate);
        }
        else if (Gain > TargetGain)
        {
            Gain = Math.Max(TargetGain, Gain - _fadeRate);
        }

        _backend.SetSourceGain(Source, Gain);
        if (Gain <= 0f && TargetGain <= 0f)
        {
            _backend.Stop(Source);
            _backend.SetSourceLooping(Source, false);
            IsActive = false;
            MaterialId = 0;
            CueHandle = 0;
        }
    }
}
