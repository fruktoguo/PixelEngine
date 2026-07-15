using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 在声明 safe phase 同步执行的 semantic operation。
/// </summary>
/// <param name="context">已认证请求与取消上下文。</param>
/// <param name="payload">请求 payload。</param>
/// <returns>payload、Undo action 与受影响资源。</returns>
public delegate AutomationOperationResult AutomationScheduledOperation(
    AutomationScheduledContext context,
    JsonElement? payload);

/// <summary>
/// 在权威 safe phase 冻结后台 preparation 输入。返回的 worker 不得访问 Editor、ImGui、GL
/// 或 Engine 权威对象，也不得发布权威状态；最终 semantic operation 会在原 safe phase 重验后提交。
/// </summary>
/// <param name="context">已认证请求与冻结点上下文。</param>
/// <param name="payload">不可变请求 payload。</param>
/// <returns>有界后台 preparation。</returns>
public delegate AutomationBackgroundPreparation? AutomationScheduledPreparation(
    AutomationScheduledContext context,
    JsonElement? payload);

/// <summary>后台 preparation worker。</summary>
/// <param name="cancellationToken">request deadline、显式 cancel 或连接关闭令牌。</param>
/// <returns>只由最终 safe-phase operation 消费的 opaque immutable state。</returns>
public delegate ValueTask<object?> AutomationBackgroundPreparationFactory(
    CancellationToken cancellationToken);

/// <summary>safe phase 冻结的一次后台 preparation。</summary>
public sealed record AutomationBackgroundPreparation
{
    /// <summary>
    /// 在线程池执行的 I/O、解析、hash 或编码 worker。实现必须响应 cancellation，
    /// 并自行清理尚未发布的临时 worker 输出。
    /// </summary>
    public required AutomationBackgroundPreparationFactory PrepareAsync { get; init; }

    /// <summary>
    /// preparation 失败、取消或 scheduler 关闭后在 EditorIngress/owner thread 调用的可选清理；
    /// 只能释放冻结/事务状态，不得与 worker 共享可变临时文件或 buffer，也不得发布未提交结果。
    /// </summary>
    public Action? AbortAtEditorIngress { get; init; }
}

/// <summary>
/// 一次 direct request 或 transaction.commit 冻结阶段共享的有界 preparation workspace。
/// registration 可用稳定 key 复用同一虚拟文件系统/解析缓存；scope 在全部 worker 结束后统一释放。
/// </summary>
public sealed class AutomationPreparationScope
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _disposed;

    /// <summary>取得既有 workspace，或在 owner safe phase 创建并登记一个新 workspace。</summary>
    /// <typeparam name="T">workspace reference type。</typeparam>
    /// <param name="key">registration 间约定的稳定作用域 key。</param>
    /// <param name="factory">只在 key 首次出现时同步执行的轻量 freeze factory。</param>
    /// <returns>同一 scope/key 的唯一实例。</returns>
    public T GetOrAdd<T>(string key, Func<T> factory)
        where T : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Automation preparation scope 只能在冻结它的 owner safe phase 访问。");
        }

        if (_values.TryGetValue(key, out object? existing))
        {
            return existing as T ?? throw new InvalidOperationException(
                $"Automation preparation scope key '{key}' 已由其他 workspace 类型占用。");
        }

        T created = factory() ?? throw new InvalidOperationException(
            $"Automation preparation scope factory '{key}' 返回了 null。");
        _values.Add(key, created);
        return created;
    }

    internal void DisposeResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? failures = null;
        foreach (object value in _values.Values.Reverse())
        {
            if (value is not IDisposable disposable)
            {
                continue;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _values.Clear();
        if (failures is not null)
        {
            throw new AggregateException("Automation preparation workspace 清理失败。", failures);
        }
    }
}

/// <summary>
/// 在权威 safe phase 已冻结小型 immutable snapshot 后，于后台完成文件读取、编码、hash
/// 或 artifact 发布。实现不得再访问 Editor、ImGui、GL 或 Engine 权威对象。
/// </summary>
/// <param name="sourceRevision">冻结 snapshot 时捕获的 revision。</param>
/// <param name="cancellationToken">request deadline、显式 cancel 或连接关闭令牌。</param>
/// <returns>最终 response payload。</returns>
public delegate ValueTask<JsonElement?> AutomationDeferredPayloadFactory(
    AutomationRevisionSnapshot sourceRevision,
    CancellationToken cancellationToken);

/// <summary>
/// 主线程 operation 可用的不可变上下文。
/// </summary>
public sealed class AutomationScheduledContext
{
    internal AutomationScheduledContext(
        AutomationRequestContext request,
        CancellationToken cancellationToken,
        AutomationRevisionStore revisions,
        object? preparedState = null,
        bool hasPreparedState = false,
        AutomationPreparationScope? preparationScope = null)
    {
        Request = request;
        CancellationToken = cancellationToken;
        Revisions = revisions;
        PreparedState = preparedState;
        HasPreparedState = hasPreparedState;
        PreparationScope = preparationScope;
    }

    /// <summary>认证请求上下文。</summary>
    public AutomationRequestContext Request { get; }

    /// <summary>deadline、显式 cancel 或连接关闭令牌。</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>权威 revision store；operation 通常只需 Capture，推进由 scheduler 统一完成。</summary>
    public AutomationRevisionStore Revisions { get; }

    /// <summary>后台 preparation 结果；未声明 preparation 的 capability 为 null。</summary>
    public object? PreparedState { get; }

    /// <summary>当前 operation 是否已经完成其声明的后台 preparation。</summary>
    public bool HasPreparedState { get; }

    /// <summary>仅在 safe-phase preparation delegate 冻结期间可用的共享 workspace scope。</summary>
    public AutomationPreparationScope? PreparationScope { get; }

    /// <summary>以强类型取得后台 preparation 结果。</summary>
    /// <typeparam name="T">registration 约定的 immutable state 类型。</typeparam>
    /// <returns>强类型结果。</returns>
    public T RequirePreparedState<T>()
    {
        return HasPreparedState && PreparedState is T value
            ? value
            : throw new InvalidOperationException(
                $"Automation operation 缺少预期的 prepared state {typeof(T).FullName}。");
    }
}

/// <summary>
/// semantic operation 的同步结果。
/// </summary>
public sealed record AutomationOperationResult
{
    /// <summary>小型 response payload。</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>
    /// 可选后台 response producer。只读 capability 与不修改 Editor 状态的 Command 可使用；
    /// scheduler 捕获 revision 后由 Server I/O 路径异步执行，避免在主线程做编码、hash 或文件 I/O。
    /// </summary>
    public AutomationDeferredPayloadFactory? DeferredPayloadFactory { get; init; }

    /// <summary>已经执行、可供统一 Undo/Redo 或 transaction rollback 的 action。</summary>
    public IAutomationUndoAction? UndoAction { get; init; }

    /// <summary>被写入或被 snapshot 观察的稳定资源 ids。</summary>
    public string[] ResourceIds { get; init; } = [];

    /// <summary>
    /// Write operation 是否实际改变了权威状态。显式为 false 时不得携带 Undo action，
    /// scheduler 只捕获当前 revision，不推进 revision、登记 Undo 或发布 state event。
    /// </summary>
    public bool WriteStateChanged { get; init; } = true;

    /// <summary>system coordinator 已经产生的显式 revision；普通 capability 由 scheduler 生成。</summary>
    public AutomationRevisionSnapshot? RevisionOverride { get; init; }

    /// <summary>command 是否实际改变了权威状态；false 时不产生自动 state event。</summary>
    public bool StateChanged { get; init; }

    /// <summary>
    /// command 调用的既有协调器是否已经发布 descriptor 声明的 state-changed event。
    /// 仅用于 project/scene 转场这类必须复用同一手动协调器的操作，防止重复事件。
    /// </summary>
    public bool StateEventAlreadyPublished { get; init; }
}

/// <summary>
/// 一个已经执行、可逆且不依赖 UI widget 的 semantic action。
/// </summary>
public interface IAutomationUndoAction
{
    /// <summary>Undo history 显示名称。</summary>
    string Name { get; }

    /// <summary>恢复 before state。</summary>
    void Undo();

    /// <summary>重新应用 after state。</summary>
    void Redo();
}

/// <summary>已由 scheduler 绑定稳定资源集合的 Undo action。</summary>
public interface IAutomationResourceScopedUndoAction : IAutomationUndoAction
{
    /// <summary>Undo/Redo 后由 action 自身推进的稳定资源 IDs。</summary>
    IReadOnlyList<string> ResourceIds { get; }
}

/// <summary>
/// Editor 唯一 Undo/Redo history 的适配边界。
/// </summary>
public interface IAutomationUndoSink
{
    /// <summary>记录一个已经执行的 action，不得再次执行。</summary>
    /// <param name="action">已执行 action。</param>
    void RecordExecuted(IAutomationUndoAction action);
}

/// <summary>
/// Transaction commit/Undo/Redo 时捕获和恢复 selection、dirty 等补充状态。
/// </summary>
public interface IAutomationTransactionParticipant
{
    /// <summary>在 EditorIngress 冻结当前补充状态；begin 也借此收口 pending authoring edits。</summary>
    /// <returns>opaque before state。</returns>
    object CaptureState();

    /// <summary>在所有 operation Undo 后恢复补充 before state。</summary>
    /// <param name="state">由 <see cref="CaptureState" /> 返回的对象。</param>
    void RestoreState(object state);
}

/// <summary>
/// 一条 capability registration；descriptor 与真实 delegate 同源。
/// </summary>
public sealed record AutomationMethodRegistration
{
    /// <summary>机器可读 descriptor。</summary>
    public required AutomationMethodDescriptor Descriptor { get; init; }

    /// <summary>真实 semantic delegate。</summary>
    public required AutomationScheduledOperation Operation { get; init; }

    /// <summary>可选 safe-point freeze → background prepare 阶段。</summary>
    public AutomationScheduledPreparation? Preparation { get; init; }
}

/// <summary>
/// 主线程 scheduler 容量与时钟配置。
/// </summary>
public sealed record AutomationMainThreadSchedulerOptions
{
    /// <summary>所有 phase 合计最大 queued work item。</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>一次 safe point 最多执行的 item 数，防止 UI 饥饿。</summary>
    public int MaxItemsPerDrain { get; init; } = 64;

    /// <summary>常驻 background preparation worker 数；只处理冻结后的 I/O、解析、hash 与编码。</summary>
    public int BackgroundPreparationWorkerCount { get; init; } =
        Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>关闭 scheduler 时等待 preparation worker 合作取消的总时限；最大一分钟。</summary>
    public TimeSpan BackgroundPreparationShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>transaction 默认 lease。</summary>
    public TimeSpan DefaultTransactionLease { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>transaction 最大 lease。</summary>
    public TimeSpan MaxTransactionLease { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>单 transaction 最多 staging 的 semantic write 数。</summary>
    public int MaxTransactionOperations { get; init; } = 256;

    /// <summary>单 transaction staging payload/precondition 的近似 UTF-8 字节上限。</summary>
    public int MaxTransactionStagedBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>幂等成功结果保留时间。</summary>
    public TimeSpan IdempotencyRetention { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>幂等 in-flight + completed entry 上限。</summary>
    public int IdempotencyCapacity { get; init; } = 4096;

    /// <summary>可测试时钟。</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>队列由空变为非空时触发的窗口唤醒 signal。</summary>
    public Action? Wake { get; init; }
}
