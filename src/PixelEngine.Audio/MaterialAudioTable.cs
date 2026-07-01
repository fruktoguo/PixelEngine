using PixelEngine.Core.Events;
using PixelEngine.Simulation;

namespace PixelEngine.Audio;

/// <summary>
/// 材质 id 与音频事件类型到 cue 句柄的扁平化映射表。
/// </summary>
public sealed class MaterialAudioTable
{
    private readonly int[] _cueHandles;

    private MaterialAudioTable(int[] cueHandles, int materialCount)
    {
        _cueHandles = cueHandles;
        MaterialCount = materialCount;
    }

    /// <summary>
    /// 材质数量。
    /// </summary>
    public int MaterialCount { get; }

    /// <summary>
    /// 从材质定义数组构建扁平表。
    /// </summary>
    /// <param name="definitions">按 runtime id 排列的材质定义。</param>
    /// <returns>材质音频表。</returns>
    public static MaterialAudioTable FromDefinitions(ReadOnlySpan<MaterialDef> definitions)
    {
        int[] cueHandles = new int[checked(definitions.Length * AudioEventTypeTraits.TypeCount)];
        for (int i = 0; i < definitions.Length; i++)
        {
            MaterialDef definition = definitions[i];
            if (definition.Id != i)
            {
                throw new ArgumentException($"材质 {definition.Name} 的 Id={definition.Id}，但期望数组下标 {i}。", nameof(definitions));
            }

            WriteCueSet(cueHandles, i, definition.AudioCues);
        }

        return new MaterialAudioTable(cueHandles, definitions.Length);
    }

    /// <summary>
    /// 从运行时材质表构建扁平表。
    /// </summary>
    /// <param name="materials">运行时材质表。</param>
    /// <returns>材质音频表。</returns>
    public static MaterialAudioTable FromMaterialTable(MaterialTable materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        int[] cueHandles = new int[checked(materials.Count * AudioEventTypeTraits.TypeCount)];
        for (ushort i = 0; i < materials.Count; i++)
        {
            ref readonly MaterialDef definition = ref materials.Get(i);
            WriteCueSet(cueHandles, i, definition.AudioCues);
        }

        return new MaterialAudioTable(cueHandles, materials.Count);
    }

    /// <summary>
    /// 尝试把合并事件解析为材质化播放参数。
    /// </summary>
    /// <param name="audioEvent">合并事件。</param>
    /// <param name="tick">当前 tick，用于确定性 pitch 抖动。</param>
    /// <param name="playback">播放参数。</param>
    /// <returns>若材质和事件类型存在 cue 则为 <see langword="true"/>。</returns>
    public bool TryResolve(in CoalescedAudioEvent audioEvent, long tick, out MaterialAudioPlayback playback)
    {
        playback = default;
        if (audioEvent.MaterialId >= MaterialCount || !AudioEventTypeTraits.TryGetIndex(audioEvent.Type, out int typeIndex))
        {
            return false;
        }

        int cueHandle = _cueHandles[GetFlatIndex(audioEvent.MaterialId, typeIndex)];
        if (cueHandle <= 0)
        {
            return false;
        }

        float magnitude = Clamp01(audioEvent.Magnitude);
        int eventCount = Math.Max(1, (int)audioEvent.Count);
        float countBoost = MathF.Min(0.25f, MathF.Log2(eventCount) * 0.04f);
        float gain = Math.Clamp(0.35f + (magnitude * 0.65f) + countBoost, 0f, 1.25f);
        float pitchJitter = (HashToUnit(audioEvent.MaterialId, audioEvent.Type, tick) * 0.08f) - 0.04f;
        float densityDrop = MathF.Min(0.10f, MathF.Log2(eventCount) * 0.015f);
        float pitch = Math.Clamp(1f + pitchJitter - densityDrop, 0.75f, 1.25f);
        playback = new MaterialAudioPlayback(cueHandle, gain, pitch, AudioEventTypeTraits.GetPriority(audioEvent.Type));
        return true;
    }

    private static void WriteCueSet(int[] cueHandles, int materialId, AudioCueSet cues)
    {
        cueHandles[GetFlatIndex(materialId, 0)] = cues.ImpactCue;
        cueHandles[GetFlatIndex(materialId, 1)] = cues.FireCue;
        cueHandles[GetFlatIndex(materialId, 2)] = cues.SplashCue;
        cueHandles[GetFlatIndex(materialId, 3)] = cues.ExplosionCue;
        cueHandles[GetFlatIndex(materialId, 4)] = cues.ShatterCue;
        cueHandles[GetFlatIndex(materialId, 5)] = cues.AmbientCue;
    }

    private static int GetFlatIndex(int materialId, int typeIndex)
    {
        return (materialId * AudioEventTypeTraits.TypeCount) + typeIndex;
    }

    private static float Clamp01(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    private static float HashToUnit(ushort materialId, AudioEventType type, long tick)
    {
        unchecked
        {
            uint hash = materialId;
            hash = (hash * 16777619u) ^ (byte)type;
            hash = (hash * 16777619u) ^ (uint)tick;
            hash ^= hash >> 16;
            return (hash & 0xffffu) / 65535f;
        }
    }
}
