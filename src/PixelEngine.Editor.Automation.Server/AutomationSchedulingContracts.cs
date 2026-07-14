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
/// 主线程 operation 可用的不可变上下文。
/// </summary>
public sealed class AutomationScheduledContext
{
    internal AutomationScheduledContext(
        AutomationRequestContext request,
        CancellationToken cancellationToken,
        AutomationRevisionStore revisions)
    {
        Request = request;
        CancellationToken = cancellationToken;
        Revisions = revisions;
    }

    /// <summary>认证请求上下文。</summary>
    public AutomationRequestContext Request { get; }

    /// <summary>deadline、显式 cancel 或连接关闭令牌。</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>权威 revision store；operation 通常只需 Capture，推进由 scheduler 统一完成。</summary>
    public AutomationRevisionStore Revisions { get; }
}

/// <summary>
/// semantic operation 的同步结果。
/// </summary>
public sealed record AutomationOperationResult
{
    /// <summary>小型 response payload。</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>已经执行、可供统一 Undo/Redo 或 transaction rollback 的 action。</summary>
    public IAutomationUndoAction? UndoAction { get; init; }

    /// <summary>被写入或被 snapshot 观察的稳定资源 ids。</summary>
    public string[] ResourceIds { get; init; } = [];

    /// <summary>system coordinator 已经产生的显式 revision；普通 capability 由 scheduler 生成。</summary>
    public AutomationRevisionSnapshot? RevisionOverride { get; init; }
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
