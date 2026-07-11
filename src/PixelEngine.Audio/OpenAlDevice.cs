using Silk.NET.OpenAL;

namespace PixelEngine.Audio;

/// <summary>
/// OpenAL 设备与上下文生命周期封装。设备不可用时由 <see cref="TryInitialize"/> 返回软失败。
/// </summary>
public sealed unsafe class OpenAlDevice : IDisposable
{
    private readonly ALContext _contextApi;
    private readonly Device* _device;
    private readonly Context* _context;
    private bool _disposed;

    private OpenAlDevice(ALContext contextApi, AL al, Device* device, Context* context)
    {
        _contextApi = contextApi;
        _device = device;
        _context = context;
        Backend = new OpenAlBackend(al);
    }

    /// <summary>
    /// OpenAL 后端。
    /// </summary>
    public OpenAlBackend Backend { get; }

    /// <summary>
    /// 尝试打开默认 OpenAL 设备并创建当前上下文。
    /// </summary>
    /// <param name="settings">音频设置。</param>
    /// <param name="device">成功时返回设备封装。</param>
    /// <param name="failureReason">失败原因。</param>
    /// <returns>初始化是否成功。</returns>
    public static bool TryInitialize(AudioSettings settings, out OpenAlDevice? device, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        device = null;
        failureReason = null;

        try
        {
            ALContext contextApi = ALContext.GetApi();
            AL al = AL.GetApi();
            Device* rawDevice = contextApi.OpenDevice(null);
            if (rawDevice == null)
            {
                failureReason = "OpenAL 默认设备不可用。";
                return false;
            }

            Context* rawContext = contextApi.CreateContext(rawDevice, null);
            if (rawContext == null)
            {
                _ = contextApi.CloseDevice(rawDevice);
                failureReason = "OpenAL 上下文创建失败。";
                return false;
            }

            if (!contextApi.MakeContextCurrent(rawContext))
            {
                contextApi.DestroyContext(rawContext);
                _ = contextApi.CloseDevice(rawDevice);
                failureReason = "OpenAL 上下文 make-current 失败。";
                return false;
            }

            OpenAlDevice openAlDevice = new(contextApi, al, rawDevice, rawContext);
            al.DistanceModel(DistanceModel.InverseDistanceClamped);
            openAlDevice.Backend.SetListener(
                new AudioListenerState(
                    new System.Numerics.Vector3(0f, 0f, validated.ListenerDepth / validated.PixelsPerMeter),
                    -System.Numerics.Vector3.UnitZ,
                    System.Numerics.Vector3.UnitY,
                    validated.MasterVolume));
            device = openAlDevice;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or BadImageFormatException)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Backend.Dispose();
        _ = _contextApi.MakeContextCurrent(null);
        _contextApi.DestroyContext(_context);
        _ = _contextApi.CloseDevice(_device);
    }
}
