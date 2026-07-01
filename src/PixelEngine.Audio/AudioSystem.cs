namespace PixelEngine.Audio;

/// <summary>
/// 音频子系统门面，负责后端初始化、listener 更新与 voice 池生命周期。
/// </summary>
public sealed class AudioSystem : IDisposable
{
    private IAudioBackend? _backend;
    private OpenAlDevice? _device;
    private AudioVoicePool? _voices;
    private AudioSettings _settings = new();
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
    /// OpenAL 初始化失败原因；成功或使用显式 backend 时为 <see langword="null"/>。
    /// </summary>
    public string? InitializationWarning { get; private set; }

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
        _settings = settings.Validate();
        if (backend is not null)
        {
            _backend = backend;
            _ownsBackend = false;
            InitializationWarning = null;
        }
        else if (OpenAlDevice.TryInitialize(_settings, out OpenAlDevice? device, out string? failureReason))
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

        _voices = new AudioVoicePool(_backend, _settings);
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
        AudioListenerState listenerState = AudioListenerState.FromView(in view, _settings);
        backend.SetListener(in listenerState);
        Voices.RefreshFinishedVoices();
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
}
