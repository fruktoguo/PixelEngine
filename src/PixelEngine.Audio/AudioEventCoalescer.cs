using System.Numerics;
using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 固定容量的音频事件合并器，按事件类型限频并把近坐标事件合并到空间桶。
/// </summary>
public sealed class AudioEventCoalescer
{
    private readonly Entry[] _entries;
    private readonly int[] _usedSlots;
    private readonly int[] _perTypeAccepted;
    private readonly int[] _perTypeCaps;
    private readonly int _mask;
    private readonly int _bucketSize;
    private int _usedSlotCount;

    /// <summary>
    /// 创建合并器。
    /// </summary>
    /// <param name="settings">音频设置。</param>
    public AudioEventCoalescer(AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _bucketSize = validated.CoalesceBucketSize;
        _perTypeAccepted = new int[AudioEventTypeTraits.TypeCount];
        _perTypeCaps = new int[AudioEventTypeTraits.TypeCount];
        _perTypeCaps[0] = validated.MaxParticleImpactEventsPerFrame;
        _perTypeCaps[1] = validated.MaxFireCrackleEventsPerFrame;
        _perTypeCaps[2] = validated.MaxLiquidSplashEventsPerFrame;
        _perTypeCaps[3] = validated.MaxExplosionEventsPerFrame;
        _perTypeCaps[4] = validated.MaxRigidbodyShatterEventsPerFrame;
        _perTypeCaps[5] = validated.MaxAmbientRegionEventsPerFrame;

        int outputCapacity = 0;
        for (int i = 0; i < _perTypeCaps.Length; i++)
        {
            outputCapacity += _perTypeCaps[i];
        }

        int hashCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(2, outputCapacity * 2));
        _entries = new Entry[hashCapacity];
        _usedSlots = new int[outputCapacity];
        _mask = hashCapacity - 1;
    }

    /// <summary>
    /// 本帧因近坐标合并而未形成独立播放项的事件数。
    /// </summary>
    public int CoalescedCount { get; private set; }

    /// <summary>
    /// 本帧因限频或未知类型丢弃的事件数。
    /// </summary>
    public int DroppedCount { get; private set; }

    /// <summary>
    /// 开始一帧合并，清空上一帧状态。
    /// </summary>
    public void BeginFrame()
    {
        for (int i = 0; i < _usedSlotCount; i++)
        {
            _entries[_usedSlots[i]] = default;
        }

        _usedSlotCount = 0;
        Array.Clear(_perTypeAccepted);
        CoalescedCount = 0;
        DroppedCount = 0;
    }

    /// <summary>
    /// 添加原始音频事件。
    /// </summary>
    /// <param name="audioEvent">原始音频事件。</param>
    public void Add(in AudioEvent audioEvent)
    {
        if (!AudioEventTypeTraits.TryGetIndex(audioEvent.Type, out int typeIndex))
        {
            DroppedCount++;
            return;
        }

        int bucketX = FloorDiv(audioEvent.CellX, _bucketSize);
        int bucketY = FloorDiv(audioEvent.CellY, _bucketSize);
        int slot = FindSlot(audioEvent.Type, bucketX, bucketY);
        ref Entry entry = ref _entries[slot];
        if (entry.Occupied)
        {
            Merge(ref entry, in audioEvent, typeIndex);
            return;
        }

        if (_perTypeAccepted[typeIndex] >= _perTypeCaps[typeIndex])
        {
            DroppedCount++;
            return;
        }

        _perTypeAccepted[typeIndex]++;
        entry = new Entry
        {
            Occupied = true,
            Type = audioEvent.Type,
            BucketX = bucketX,
            BucketY = bucketY,
            CellX = BucketCenter(bucketX),
            CellY = BucketCenter(bucketY),
            MaterialId = audioEvent.MaterialId,
            Magnitude = SanitizeMagnitude(audioEvent.Magnitude),
            Count = audioEvent.Count == 0 ? (ushort)1 : audioEvent.Count,
        };
        _usedSlots[_usedSlotCount++] = slot;
    }

    /// <summary>
    /// 把本帧合并结果写入目标缓冲。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>写入的合并事件数。</returns>
    public int Flush(Span<CoalescedAudioEvent> destination)
    {
        int written = Math.Min(destination.Length, _usedSlotCount);
        for (int i = 0; i < written; i++)
        {
            Entry entry = _entries[_usedSlots[i]];
            destination[i] = new CoalescedAudioEvent(
                entry.Type,
                entry.CellX,
                entry.CellY,
                entry.MaterialId,
                entry.Magnitude,
                entry.Count);
        }

        DroppedCount += _usedSlotCount - written;
        return written;
    }

    private void Merge(ref Entry entry, in AudioEvent audioEvent, int typeIndex)
    {
        if (_perTypeAccepted[typeIndex] >= _perTypeCaps[typeIndex])
        {
            DroppedCount++;
            float cappedMagnitude = SanitizeMagnitude(audioEvent.Magnitude);
            if (cappedMagnitude > entry.Magnitude)
            {
                entry.Magnitude = cappedMagnitude;
                entry.MaterialId = audioEvent.MaterialId;
            }

            return;
        }

        _perTypeAccepted[typeIndex]++;
        CoalescedCount++;
        ushort addCount = audioEvent.Count == 0 ? (ushort)1 : audioEvent.Count;
        int nextCount = entry.Count + addCount;
        entry.Count = nextCount > ushort.MaxValue ? ushort.MaxValue : (ushort)nextCount;
        float magnitude = SanitizeMagnitude(audioEvent.Magnitude);
        if (magnitude > entry.Magnitude)
        {
            entry.Magnitude = magnitude;
            entry.MaterialId = audioEvent.MaterialId;
        }
    }

    private int FindSlot(AudioEventType type, int bucketX, int bucketY)
    {
        int slot = Hash(type, bucketX, bucketY) & _mask;
        while (true)
        {
            Entry entry = _entries[slot];
            if (!entry.Occupied || (entry.Type == type && entry.BucketX == bucketX && entry.BucketY == bucketY))
            {
                return slot;
            }

            slot = (slot + 1) & _mask;
        }
    }

    private int BucketCenter(int bucket)
    {
        return (bucket * _bucketSize) + (_bucketSize / 2);
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int Hash(AudioEventType type, int bucketX, int bucketY)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + (int)type;
            hash = (hash * 31) + bucketX;
            hash = (hash * 31) + bucketY;
            return hash;
        }
    }

    private static float SanitizeMagnitude(float magnitude)
    {
        return float.IsFinite(magnitude) && magnitude > 0f ? magnitude : 0f;
    }

    private struct Entry
    {
        public bool Occupied;
        public AudioEventType Type;
        public int BucketX;
        public int BucketY;
        public int CellX;
        public int CellY;
        public ushort MaterialId;
        public float Magnitude;
        public ushort Count;
    }
}
