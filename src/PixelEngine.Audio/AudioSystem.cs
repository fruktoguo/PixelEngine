using System.Numerics;
using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Audio;

/// <summary>
/// 音频子系统门面，负责后端初始化、listener 更新与 voice 池生命周期。
/// </summary>
public sealed class AudioSystem : IDisposable
{
    private IAudioBackend? _backend;
    private OpenAlDevice? _device;
    private AudioVoicePool? _voices;
    private AudioClipCache? _clipCache;
    private bool _ownsBackend;
    private bool _disposed;

    /// <summary>
    /// 当前后端。
    /// </summary>
    public IAudioBackend Backend => _backend ?? throw new InvalidOperationException("AudioSystem 尚未 Initialize。");

    /// <summary>
    /// positional voice 池。
    /// </summary>
    public AudioVoicePool Voices => _voices ?? throw new InvalidOperationException("AudioSystem 尚未 Initialize。");

    /// <summary>
    /// ambient loop 管理器；未挂接时为 <see langword="null"/>。
    /// </summary>
    public AmbientLoopManager? AmbientLoops { get; private set; }

    /// <summary>
    /// OpenAL 初始化失败原因；成功或使用显式 backend 时为 <see langword="null"/>。
    /// </summary>
    public string? InitializationWarning { get; private set; }

    /// <summary>
    /// 当前诊断快照。
    /// </summary>
    public AudioDiagnostics Diagnostics { get; private set; }

    /// <summary>
    /// 当前运行时音频设置快照。
    /// </summary>
    public AudioSettings Settings { get; private set; } = new();

    /// <summary>
    /// 最近一次 listener 状态。
    /// </summary>
    public AudioListenerState CurrentListener { get; private set; }

    /// <summary>
    /// 初始化音频系统。未提供 backend 时优先 OpenAL，失败则静默降级为 <see cref="NullAudioBackend"/>。
    /// </summary>
    /// <param name="settings">音频设置。</param>
    /// <param name="backend">可选显式后端。</param>
    public void Initialize(AudioSettings settings, IAudioBackend? backend = null)
    {
        ThrowIfDisposed();
        if (_backend is not null)
        {
            throw new InvalidOperationException("AudioSystem 已初始化。");
        }

        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings.Validate();
        if (backend is not null)
        {
            _backend = backend;
            _ownsBackend = false;
            InitializationWarning = null;
        }
        else if (OpenAlDevice.TryInitialize(Settings, out OpenAlDevice? device, out string? failureReason))
        {
            _device = device!;
            _backend = _device.Backend;
            _ownsBackend = true;
            InitializationWarning = null;
        }
        else
        {
            _backend = new NullAudioBackend();
            _ownsBackend = true;
            InitializationWarning = failureReason ?? "OpenAL 初始化失败，已启用无声后端。";
        }

        _voices = new AudioVoicePool(_backend, Settings);
        CurrentListener = new AudioListenerState(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, Settings.MasterVolume);
        RefreshDiagnostics(default);
    }

    /// <summary>
    /// 挂接 clip cache，供公开播放 API 使用。
    /// </summary>
    /// <param name="clipCache">clip cache。</param>
    public void AttachClipCache(AudioClipCache clipCache)
    {
        ThrowIfDisposed();
        _clipCache = clipCache ?? throw new ArgumentNullException(nameof(clipCache));
        RefreshDiagnostics(default);
    }

    /// <summary>
    /// 挂接 ambient loop 管理器，所有权转移给音频系统。
    /// </summary>
    /// <param name="ambientLoops">ambient loop 管理器。</param>
    public void AttachAmbientLoopManager(AmbientLoopManager ambientLoops)
    {
        ThrowIfDisposed();
        if (AmbientLoops is not null)
        {
            throw new InvalidOperationException("AudioSystem 已挂接 AmbientLoopManager。");
        }

        AmbientLoops = ambientLoops ?? throw new ArgumentNullException(nameof(ambientLoops));
        RefreshDiagnostics(default);
    }

    /// <summary>
    /// 应用新的运行时设置并刷新 listener / voice / ambient 配置。
    /// </summary>
    /// <param name="settings">新的音频设置。</param>
    public void ApplySettings(AudioSettings settings)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        Settings = validated;
        Voices.ApplySettings(validated);
        AmbientLoops?.ApplySettings(validated);
        CurrentListener = CurrentListener with { Gain = validated.MasterVolume };
        AudioListenerState listener = CurrentListener;
        Backend.SetListener(in listener);
        RefreshDiagnostics(Diagnostics.LastDispatch);
    }

    /// <summary>
    /// 更新 listener 与 voice 完成状态。render-only 帧也必须调用以保持声场连续。
    /// </summary>
    /// <param name="view">listener 视口。</param>
    /// <param name="simTick">当前模拟 tick。</param>
    /// <param name="simSteppedThisFrame">本帧是否执行了模拟 tick。</param>
    public void Update(in AudioListenerView view, long simTick, bool simSteppedThisFrame)
    {
        _ = simTick;
        _ = simSteppedThisFrame;
        ThrowIfDisposed();
        IAudioBackend backend = Backend;
        AudioListenerState listenerState = AudioListenerState.FromView(in view, Settings);
        CurrentListener = listenerState;
        backend.SetListener(in listenerState);
        Voices.RefreshFinishedVoices();
        RefreshDiagnostics(default);
    }

    /// <summary>
    /// 播放一个世界定位 one-shot clip。
    /// </summary>
    /// <param name="clip">已加载 clip。</param>
    /// <param name="worldPos">世界 cell 坐标。</param>
    /// <param name="volume">音量。</param>
    /// <param name="pitch">音高。</param>
    /// <returns>是否成功取到 voice 并播放。</returns>
    public bool PlayOneShot(AudioClip clip, in Vector2 worldPos, float volume = 1f, float pitch = 1f)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(clip);
        AudioSpace space = new(Settings.PixelsPerMeter);
        Vector3 position = space.ToMeters(worldPos.X, worldPos.Y);
        AudioVoice? voice = Voices.Acquire(128, PixelEngine.Core.Events.AudioEventType.ParticleImpact, position, CurrentListener.Position, 0);
        if (voice is null)
        {
            RefreshDiagnostics(default);
            return false;
        }

        voice.Play(clip.Buffer.Handle, volume * Settings.SfxVolume, pitch);
        RefreshDiagnostics(default);
        return true;
    }

    /// <summary>
    /// 播放一个非定位 UI clip。当前实现把 source 放在 listener 位置，避免左右声像偏移。
    /// </summary>
    /// <param name="clip">已加载 clip。</param>
    /// <param name="volume">音量。</param>
    /// <returns>是否成功播放。</returns>
    public bool PlayUi(AudioClip clip, float volume = 1f)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(clip);
        AudioVoice? voice = Voices.Acquire(byte.MaxValue, PixelEngine.Core.Events.AudioEventType.AmbientRegion, CurrentListener.Position, CurrentListener.Position, 0);
        if (voice is null)
        {
            RefreshDiagnostics(default);
            return false;
        }

        voice.Play(clip.Buffer.Handle, volume * Settings.UiVolume, 1f);
        RefreshDiagnostics(default);
        return true;
    }

    /// <summary>
    /// 尝试播放已加载的 asset id；未加载时静默返回 false，不阻塞主线程。
    /// </summary>
    /// <param name="assetId">资产 id。</param>
    /// <param name="worldPos">世界 cell 坐标。</param>
    /// <param name="volume">音量。</param>
    /// <param name="pitch">音高。</param>
    /// <returns>是否成功播放。</returns>
    public bool TryPlayLoadedOneShot(string assetId, in Vector2 worldPos, float volume = 1f, float pitch = 1f)
    {
        ThrowIfDisposed();
        return _clipCache is not null
            && _clipCache.TryGetLoaded(assetId, out AudioClip? clip)
            && clip is not null
            && PlayOneShot(clip, in worldPos, volume, pitch);
    }

    /// <summary>
    /// 发布当前音频诊断到 Core 计数器。
    /// </summary>
    /// <param name="counters">Core 诊断计数器。</param>
    public void PublishDiagnostics(EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        AudioDiagnostics diagnostics = Diagnostics;
        counters.SetAudioDiagnostics(
            diagnostics.LastDispatch.Drained,
            diagnostics.LastDispatch.Coalesced,
            diagnostics.LastDispatch.Dropped,
            diagnostics.LastDispatch.Played,
            diagnostics.ActiveVoices,
            diagnostics.ActiveAmbientVoices,
            diagnostics.VoiceSteals,
            diagnostics.LoadedClips,
            diagnostics.LoadingClips,
            diagnostics.LastDispatch.DispatchMilliseconds);
    }

    /// <summary>
    /// 记录本帧事件派发统计并刷新诊断快照。
    /// </summary>
    public void RecordDispatchStats(in AudioDispatchStats dispatch)
    {
        ThrowIfDisposed();
        RefreshDiagnostics(dispatch);
    }

    /// <summary>
    /// 推进 ambient loop 管理器；render-only 帧空事件也必须调用以完成淡出。
    /// </summary>
    /// <param name="events">本帧合并后的音频事件。</param>
    public void UpdateAmbient(ReadOnlySpan<CoalescedAudioEvent> events)
    {
        ThrowIfDisposed();
        AmbientLoops?.Update(events);
        RefreshDiagnostics(Diagnostics.LastDispatch);
    }

    /// <summary>
    /// 关闭音频系统并释放后端资源。
    /// </summary>
    public void Shutdown()
    {
        if (_backend is null)
        {
            return;
        }

        _voices?.Dispose();
        _voices = null;
        AmbientLoops?.Dispose();
        AmbientLoops = null;

        if (_device is not null)
        {
            _device.Dispose();
            _device = null;
        }
        else if (_ownsBackend)
        {
            _backend.Dispose();
        }

        _backend = null;
        _ownsBackend = false;
        _clipCache = null;
        Diagnostics = default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void RefreshDiagnostics(AudioDispatchStats dispatch)
    {
        AudioVoicePool? voices = _voices;
        Diagnostics = new AudioDiagnostics(
            dispatch,
            voices?.ActiveVoiceCount ?? 0,
            AmbientLoops?.ActiveVoiceCount ?? 0,
            voices?.StealCount ?? 0,
            _clipCache?.LoadedCount ?? 0,
            _clipCache?.LoadingCount ?? 0);
    }
}
