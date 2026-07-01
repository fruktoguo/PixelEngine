using System.Numerics;
using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 固定池中的一个 positional voice，封装后端 source 与抢占评分。
/// </summary>
public sealed class AudioVoice
{
    private readonly IAudioBackend _backend;

    internal AudioVoice(IAudioBackend backend, uint source, int slotIndex, AudioSettings settings)
    {
        _backend = backend;
        Source = source;
        SlotIndex = slotIndex;
        Configure(settings);
    }

    /// <summary>
    /// voice 在池中的固定槽位。
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// 后端 source 句柄。
    /// </summary>
    public uint Source { get; }

    /// <summary>
    /// 当前事件优先级。
    /// </summary>
    public byte Priority { get; private set; }

    /// <summary>
    /// 当前事件类型。
    /// </summary>
    public AudioEventType EventType { get; private set; }

    /// <summary>
    /// 当前播放位置，单位为米。
    /// </summary>
    public Vector3 Position { get; private set; }

    /// <summary>
    /// 开始占用该 voice 的逻辑 tick。
    /// </summary>
    public long StartedTick { get; private set; }

    /// <summary>
    /// voice 是否已被池分配。
    /// </summary>
    public bool IsAllocated { get; private set; }

    /// <summary>
    /// 播放是否已完成。
    /// </summary>
    public bool IsFinished
    {
        get
        {
            if (!IsAllocated)
            {
                return true;
            }

            AudioSourceState state = _backend.GetState(Source);
            return state == AudioSourceState.Stopped;
        }
    }

    /// <summary>
    /// 设置 source 的距离衰减默认值。
    /// </summary>
    /// <param name="settings">音频设置。</param>
    public void Configure(AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _backend.ConfigureSource(Source, settings);
    }

    /// <summary>
    /// 为一次播放占用 voice。若 source 正在播放，会先停止以避免叠音。
    /// </summary>
    /// <param name="priority">事件优先级。</param>
    /// <param name="eventType">事件类型。</param>
    /// <param name="position">source 位置，单位为米。</param>
    /// <param name="tick">当前逻辑 tick。</param>
    public void Reserve(byte priority, AudioEventType eventType, Vector3 position, long tick)
    {
        if (IsAllocated && !IsFinished)
        {
            _backend.Stop(Source);
        }

        Priority = priority;
        EventType = eventType;
        Position = position;
        StartedTick = tick;
        IsAllocated = true;
    }

    /// <summary>
    /// 绑定 buffer 并播放当前 voice。
    /// </summary>
    /// <param name="buffer">OpenAL buffer 句柄。</param>
    /// <param name="gain">线性增益。</param>
    /// <param name="pitch">pitch 倍率。</param>
    public void Play(uint buffer, float gain, float pitch)
    {
        if (!IsAllocated)
        {
            throw new InvalidOperationException("voice 尚未 Reserve，不能播放。");
        }

        Vector3 position = Position;
        _backend.Play(Source, buffer, in position, gain, pitch);
    }

    /// <summary>
    /// 停止并释放当前 voice。
    /// </summary>
    public void Stop()
    {
        if (!IsAllocated)
        {
            return;
        }

        _backend.Stop(Source);
        IsAllocated = false;
    }

    /// <summary>
    /// 若后端已停止播放，则释放 voice。
    /// </summary>
    /// <returns>是否已释放。</returns>
    public bool ReleaseIfFinished()
    {
        if (!IsAllocated)
        {
            return true;
        }

        if (!IsFinished)
        {
            return false;
        }

        _backend.Stop(Source);
        IsAllocated = false;
        return true;
    }

    /// <summary>
    /// 计算 voice 抢占评分，分数越高越适合被新事件抢占。
    /// </summary>
    /// <param name="requestedPriority">新事件优先级。</param>
    /// <param name="listenerPosition">listener 位置，单位为米。</param>
    /// <param name="tick">当前逻辑 tick。</param>
    /// <returns>抢占评分；负数表示不应抢占。</returns>
    public float StealScore(byte requestedPriority, Vector3 listenerPosition, long tick)
    {
        if (!IsAllocated || IsFinished)
        {
            return float.PositiveInfinity;
        }

        int priorityDelta = requestedPriority - Priority;
        if (priorityDelta < 0)
        {
            return -1f;
        }

        Vector3 delta = Position - listenerPosition;
        float distanceScore = delta.LengthSquared();
        long ageTicks = Math.Max(0L, tick - StartedTick);
        return (priorityDelta * 1_000_000f) + distanceScore + (ageTicks * 0.001f);
    }
}
