namespace PixelEngine.Audio;

/// <summary>
/// 音频子系统诊断快照。
/// </summary>
/// <param name="LastDispatch">最近一次事件派发统计。</param>
/// <param name="ActiveVoices">活跃 positional voice 数。</param>
/// <param name="VoiceSteals">累计 voice 抢占次数。</param>
/// <param name="LoadedClips">已加载 clip 数。</param>
/// <param name="LoadingClips">加载中 clip 数。</param>
public readonly record struct AudioDiagnostics(
    AudioDispatchStats LastDispatch,
    int ActiveVoices,
    long VoiceSteals,
    int LoadedClips,
    int LoadingClips);
