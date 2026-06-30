namespace PixelEngine.Hosting;

/// <summary>
/// 管理 Hosting 已装配子系统的初始化顺序与逆序关闭。
/// </summary>
public sealed class EngineLifecycle
{
    private readonly List<IEngineSubsystem> _subsystems = [];
    private readonly List<IEngineSubsystem> _initialized = [];
    private bool _initializationStarted;
    private bool _shutdown;

    /// <summary>
    /// 已注册子系统数量。
    /// </summary>
    public int Count => _subsystems.Count;

    /// <summary>
    /// 已成功初始化、尚未关闭的子系统数量。
    /// </summary>
    public int InitializedCount => _initialized.Count;

    /// <summary>
    /// 注册一个待装配子系统；必须在 Initialize 前调用。
    /// </summary>
    /// <param name="subsystem">子系统实例。</param>
    public void Register(IEngineSubsystem subsystem)
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        if (_initializationStarted)
        {
            throw new InvalidOperationException("EngineLifecycle 已开始初始化，不能再注册子系统。");
        }

        _subsystems.Add(subsystem);
    }

    /// <summary>
    /// 按注册顺序初始化全部子系统；若失败，已初始化子系统会逆序关闭。
    /// </summary>
    /// <param name="context">当前引擎运行上下文。</param>
    public void Initialize(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_initializationStarted)
        {
            throw new InvalidOperationException("EngineLifecycle 不能重复初始化。");
        }

        _initializationStarted = true;
        try
        {
            for (int i = 0; i < _subsystems.Count; i++)
            {
                IEngineSubsystem subsystem = _subsystems[i];
                subsystem.Initialize(context);
                _initialized.Add(subsystem);
            }
        }
        catch
        {
            ShutdownInitialized();
            throw;
        }
    }

    /// <summary>
    /// 按初始化逆序关闭全部已初始化子系统；重复调用安全。
    /// </summary>
    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        ShutdownInitialized();
    }

    private void ShutdownInitialized()
    {
        List<Exception>? failures = null;
        for (int i = _initialized.Count - 1; i >= 0; i--)
        {
            IEngineSubsystem subsystem = _initialized[i];
            try
            {
                subsystem.Shutdown();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException($"子系统 {subsystem.Name} 关闭失败。", exception));
            }
        }

        _initialized.Clear();
        if (failures is not null)
        {
            throw new AggregateException("一个或多个 Engine 子系统关闭失败。", failures);
        }
    }
}
