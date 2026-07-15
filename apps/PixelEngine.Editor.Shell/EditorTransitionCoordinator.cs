namespace PixelEngine.Editor.Shell;

/// <summary>
/// 需要经过未保存场景保护的 Editor 转场类型。
/// </summary>
internal enum EditorTransitionKind
{
    NewScene,
    OpenScene,
    OpenProject,
    CreateProject,
    CloseProject,
    Exit,
}

/// <summary>
/// 用户对未保存场景确认框的选择。
/// </summary>
internal enum EditorTransitionDecision
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// Editor 转场协调结果状态。
/// </summary>
internal enum EditorTransitionStatus
{
    Executed,
    ConfirmationRequired,
    PendingTransitionExists,
    Cancelled,
    SaveFailed,
    NoPendingTransition,
}

/// <summary>
/// 场景保存结果。保存实现负责把可恢复的失败转换为 <see cref="Failure"/>；意外异常保持向上传播。
/// </summary>
/// <param name="Succeeded">是否保存成功。</param>
/// <param name="Diagnostic">失败诊断；成功时为空。</param>
internal readonly record struct EditorTransitionSaveResult(bool Succeeded, string Diagnostic)
{
    public static EditorTransitionSaveResult Success()
    {
        return new EditorTransitionSaveResult(true, string.Empty);
    }

    public static EditorTransitionSaveResult Failure(string diagnostic)
    {
        return new EditorTransitionSaveResult(
            false,
            string.IsNullOrWhiteSpace(diagnostic) ? "场景保存失败。" : diagnostic.Trim());
    }
}

/// <summary>
/// 可直接供 ImGui 确认框读取的待处理转场快照；不暴露执行委托。
/// </summary>
/// <param name="Kind">转场类型。</param>
/// <param name="Target">可选目标，例如 scene logical path 或 project path。</param>
internal readonly record struct EditorTransitionPrompt(EditorTransitionKind Kind, string? Target);

/// <summary>
/// Editor 转场请求或确认处理结果。
/// </summary>
/// <param name="Status">结果状态。</param>
/// <param name="Diagnostic">面向 UI / Console 的简短诊断。</param>
internal readonly record struct EditorTransitionResult(EditorTransitionStatus Status, string Diagnostic)
{
    public bool Executed => Status == EditorTransitionStatus.Executed;
}

/// <summary>
/// UI-independent 的 Editor 未保存场景转场协调器。
/// </summary>
/// <remarks>
/// 调用方只提供当前 dirty 状态、保存实现与真正的转场 action。协调器不依赖 ImGui，
/// 也不直接操作 Editor session；UI 可读取 <see cref="Pending"/> 绘制 Save / Discard / Cancel 确认框。
/// </remarks>
internal sealed class EditorTransitionCoordinator(
    Func<bool> isDirty,
    Func<EditorTransitionSaveResult> save)
{
    private readonly Func<bool> _isDirty = isDirty ?? throw new ArgumentNullException(nameof(isDirty));
    private readonly Func<EditorTransitionSaveResult> _save = save ?? throw new ArgumentNullException(nameof(save));
    private PendingTransition? _pending;

    /// <summary>
    /// 当前待确认转场；没有待处理请求时为 null。
    /// </summary>
    public EditorTransitionPrompt? Pending => _pending is null
        ? null
        : new EditorTransitionPrompt(_pending.Kind, _pending.Target);

    public bool HasPendingTransition => _pending is not null;

    /// <summary>Editor 生命周期结束时释放 pending transition 持有的未提交 preparation。</summary>
    public void ReleasePendingPreparation()
    {
        PendingTransition? pending = _pending;
        _pending = null;
        pending?.Cancel?.Invoke();
    }

    /// <summary>
    /// 请求执行一个受 dirty guard 保护的转场。
    /// </summary>
    /// <param name="kind">转场类型。</param>
    /// <param name="action">真正执行转场的委托。</param>
    /// <param name="target">可选目标，仅供 UI 展示。</param>
    /// <param name="cancel">等待确认后被取消或协调器关闭时释放未提交 preparation 的可选回调。</param>
    /// <returns>立即执行、需要确认或已有待处理转场。</returns>
    public EditorTransitionResult Request(
        EditorTransitionKind kind,
        Action action,
        string? target = null,
        Action? cancel = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 Editor 转场类型。");
        }

        ArgumentNullException.ThrowIfNull(action);
        if (_pending is not null)
        {
            return new EditorTransitionResult(
                EditorTransitionStatus.PendingTransitionExists,
                "已有等待确认的 Editor 转场，当前请求未执行。");
        }

        string? normalizedTarget = string.IsNullOrWhiteSpace(target) ? null : target.Trim();
        if (!_isDirty())
        {
            action();
            return new EditorTransitionResult(EditorTransitionStatus.Executed, "Editor 转场已执行。");
        }

        _pending = new PendingTransition(kind, normalizedTarget, action, cancel);
        return new EditorTransitionResult(
            EditorTransitionStatus.ConfirmationRequired,
            "当前场景有未保存修改，请选择 Save、Discard 或 Cancel。");
    }

    /// <summary>
    /// 处理当前待确认转场。
    /// </summary>
    /// <param name="decision">用户选择。</param>
    /// <returns>转场执行、取消、保存失败或没有待处理请求。</returns>
    public EditorTransitionResult Resolve(EditorTransitionDecision decision)
    {
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "未知 Editor 转场确认选择。");
        }

        if (_pending is not { } pending)
        {
            return new EditorTransitionResult(
                EditorTransitionStatus.NoPendingTransition,
                "没有等待确认的 Editor 转场。");
        }

        if (decision == EditorTransitionDecision.Cancel)
        {
            _pending = null;
            pending.Cancel?.Invoke();
            return new EditorTransitionResult(EditorTransitionStatus.Cancelled, "Editor 转场已取消。");
        }

        if (decision == EditorTransitionDecision.Save)
        {
            EditorTransitionSaveResult saveResult = _save();
            if (!saveResult.Succeeded)
            {
                string diagnostic = string.IsNullOrWhiteSpace(saveResult.Diagnostic)
                    ? "场景保存失败，Editor 转场未执行。"
                    : saveResult.Diagnostic.Trim();
                return new EditorTransitionResult(EditorTransitionStatus.SaveFailed, diagnostic);
            }
        }

        _pending = null;
        pending.Action();
        return new EditorTransitionResult(EditorTransitionStatus.Executed, "Editor 转场已执行。");
    }

    private sealed record PendingTransition(
        EditorTransitionKind Kind,
        string? Target,
        Action Action,
        Action? Cancel);
}
