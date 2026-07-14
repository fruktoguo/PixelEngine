namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// Editor automation wire protocol 的稳定常量。
/// </summary>
public static class AutomationProtocolConstants
{
    /// <summary>当前协议 major 版本。</summary>
    public const int CurrentMajor = 1;

    /// <summary>当前协议 minor 版本。</summary>
    public const int CurrentMinor = 0;

    /// <summary>v1 wire DTO 的显式 schema version。</summary>
    public const int WireSchemaVersion = 1;

    /// <summary>frame header 版本。</summary>
    public const ushort FrameHeaderVersion = 1;

    /// <summary>frame header 字节数。</summary>
    public const int FrameHeaderSize = 16;

    /// <summary>默认控制面最大 payload 字节数。</summary>
    public const int DefaultMaxFrameBytes = 1024 * 1024;

    /// <summary>
    /// v1 控制面允许配置的绝对 frame 上限。更大的截图、快照与构建数据必须写入 artifact，
    /// 防止错误配置或恶意对端诱导单帧巨量分配。
    /// </summary>
    public const int AbsoluteMaxFrameBytes = 16 * 1024 * 1024;

    /// <summary>单个 discovery descriptor 的最大字节数。</summary>
    public const int MaxDiscoveryDescriptorBytes = 64 * 1024;

    /// <summary>base64 credential 文件的最大字节数。</summary>
    public const int MaxCredentialFileBytes = 4096;

    /// <summary>单个 revision snapshot/precondition 最多包含的资源数。</summary>
    public const int MaxRevisionResources = 4096;

    /// <summary>稳定 resource id 的最大字符数。</summary>
    public const int MaxResourceIdLength = 256;

    /// <summary>单个 event subscription 最多声明的 event type filter 数。</summary>
    public const int MaxEventFilterTypes = 256;

    /// <summary>实例 descriptor schema id。</summary>
    public const string InstanceDescriptorSchema = "pixelengine.editor-automation-instance/v1";

    /// <summary>HMAC 算法标识。</summary>
    public const string AuthenticationAlgorithm = "hmac-sha256";

    /// <summary>初始版本协商方法。</summary>
    public const string HelloMethod = "system.hello";

    /// <summary>会话认证方法。</summary>
    public const string AuthenticateMethod = "system.authenticate";

    /// <summary>请求取消方法。</summary>
    public const string CancelMethod = "system.cancel";

    /// <summary>连通性探测方法。</summary>
    public const string PingMethod = "system.ping";

    /// <summary>实例描述读取方法。</summary>
    public const string DescribeMethod = "system.describe";

    /// <summary>分页读取真实 semantic registry。</summary>
    public const string CapabilityListMethod = "automation.capabilities.list";

    /// <summary>读取当前 workspace。</summary>
    public const string WorkspaceGetMethod = "workspace.get";

    /// <summary>读取等待中的 Scene/Project 转场。</summary>
    public const string WorkspaceTransitionGetMethod = "workspace.transition.get";

    /// <summary>解决等待中的 Scene/Project 转场。</summary>
    public const string WorkspaceTransitionResolveMethod = "workspace.transition.resolve";

    /// <summary>读取 Editor 顶层窗口。</summary>
    public const string WindowGetMethod = "window.get";

    /// <summary>调整 Editor 顶层窗口尺寸。</summary>
    public const string WindowResizeMethod = "window.resize";

    /// <summary>原子修改 Editor 顶层窗口 placement、状态与焦点。</summary>
    public const string WindowSetMethod = "window.set";

    /// <summary>分页读取 panel registry。</summary>
    public const string WindowPanelListMethod = "window.panels.list";

    /// <summary>显示、隐藏或聚焦 panel。</summary>
    public const string WindowPanelSetMethod = "window.panel.set";

    /// <summary>按稳定 panel ID 进行 Tab、四向拆分或 undock。</summary>
    public const string WindowPanelDockMethod = "window.panel.dock";

    /// <summary>读取可无损导入的完整 dock layout。</summary>
    public const string WindowLayoutGetMethod = "window.layout.get";

    /// <summary>原子导入完整 dock layout。</summary>
    public const string WindowLayoutSetMethod = "window.layout.set";

    /// <summary>恢复默认 dock layout。</summary>
    public const string WindowLayoutResetMethod = "window.layout.reset";

    /// <summary>读取当前 Scene 元数据。</summary>
    public const string SceneGetMethod = "scene.get";

    /// <summary>保存当前 Scene。</summary>
    public const string SceneSaveMethod = "scene.save";

    /// <summary>另存当前 Scene。</summary>
    public const string SceneSaveAsMethod = "scene.saveAs";

    /// <summary>请求新建 Scene。</summary>
    public const string SceneNewMethod = "scene.new";

    /// <summary>请求打开 Scene。</summary>
    public const string SceneOpenMethod = "scene.open";

    /// <summary>分页读取 authoring Hierarchy。</summary>
    public const string HierarchyListMethod = "hierarchy.list";

    /// <summary>读取一个 GameObject/Inspector 快照。</summary>
    public const string HierarchyGetMethod = "hierarchy.get";

    /// <summary>读取当前 GameObject selection。</summary>
    public const string HierarchySelectionGetMethod = "hierarchy.selection.get";

    /// <summary>设置当前 GameObject selection。</summary>
    public const string HierarchySelectionSetMethod = "hierarchy.selection.set";

    /// <summary>创建 GameObject。</summary>
    public const string GameObjectCreateMethod = "hierarchy.gameObject.create";

    /// <summary>删除 GameObject subtree。</summary>
    public const string GameObjectDeleteMethod = "hierarchy.gameObject.delete";

    /// <summary>复制 GameObject subtree。</summary>
    public const string GameObjectDuplicateMethod = "hierarchy.gameObject.duplicate";

    /// <summary>重命名 GameObject。</summary>
    public const string GameObjectRenameMethod = "hierarchy.gameObject.rename";

    /// <summary>设置 GameObject active。</summary>
    public const string GameObjectSetEnabledMethod = "hierarchy.gameObject.setEnabled";

    /// <summary>重父 GameObject。</summary>
    public const string GameObjectReparentMethod = "hierarchy.gameObject.reparent";

    /// <summary>设置 GameObject Scene View 可见性。</summary>
    public const string GameObjectSetSceneVisibleMethod = "hierarchy.gameObject.setSceneVisible";

    /// <summary>设置 GameObject Scene View 可拾取性。</summary>
    public const string GameObjectSetScenePickableMethod = "hierarchy.gameObject.setScenePickable";

    /// <summary>批量设置全部 GameObject Scene View 可见性。</summary>
    public const string HierarchySetAllSceneVisibleMethod = "hierarchy.setAllSceneVisible";

    /// <summary>批量设置全部 GameObject Scene View 可拾取性。</summary>
    public const string HierarchySetAllScenePickableMethod = "hierarchy.setAllScenePickable";

    /// <summary>读取选中或指定 GameObject 的 Inspector 快照。</summary>
    public const string InspectorGetMethod = "inspector.get";

    /// <summary>设置 GameObject local Transform。</summary>
    public const string InspectorTransformSetMethod = "inspector.transform.set";

    /// <summary>添加 Behaviour component。</summary>
    public const string InspectorComponentAddMethod = "inspector.component.add";

    /// <summary>移除 Behaviour component。</summary>
    public const string InspectorComponentRemoveMethod = "inspector.component.remove";

    /// <summary>设置 Behaviour component Enabled。</summary>
    public const string InspectorComponentSetEnabledMethod = "inspector.component.setEnabled";

    /// <summary>移动 Behaviour component。</summary>
    public const string InspectorComponentMoveMethod = "inspector.component.move";

    /// <summary>设置 Behaviour 序列化字段。</summary>
    public const string InspectorComponentSetFieldMethod = "inspector.component.setField";

    /// <summary>原子替换内建 Canvas (Web) 与 CanvasScaler。</summary>
    public const string InspectorCanvasSetMethod = "inspector.canvas.set";

    /// <summary>设置 Canvas (Web) 的互斥 Primary 状态。</summary>
    public const string InspectorCanvasSetPrimaryMethod = "inspector.canvas.setPrimary";

    /// <summary>读取唯一 Editor Undo/Redo history。</summary>
    public const string EditorHistoryGetMethod = "editor.history.get";

    /// <summary>执行 Editor Undo。</summary>
    public const string EditorHistoryUndoMethod = "editor.history.undo";

    /// <summary>执行 Editor Redo。</summary>
    public const string EditorHistoryRedoMethod = "editor.history.redo";

    /// <summary>从 GameObject subtree 创建 Prefab asset。</summary>
    public const string PrefabCreateMethod = "hierarchy.prefab.create";

    /// <summary>实例化 Prefab asset。</summary>
    public const string PrefabInstantiateMethod = "hierarchy.prefab.instantiate";

    /// <summary>回退 Prefab overrides。</summary>
    public const string PrefabRevertOverridesMethod = "inspector.prefab.revertOverrides";

    /// <summary>读取 Scene View 工具状态。</summary>
    public const string SceneToolGetMethod = "tool.scene.get";

    /// <summary>设置 Scene View 工具状态。</summary>
    public const string SceneToolSetMethod = "tool.scene.set";

    /// <summary>执行 Frame All/Selected。</summary>
    public const string SceneToolFrameMethod = "tool.scene.frame";

    /// <summary>在世界坐标应用当前画刷。</summary>
    public const string BrushApplyMethod = "tool.brush.apply";

    /// <summary>沿控制点折线执行连续世界画刷 stroke。</summary>
    public const string BrushStrokeMethod = "tool.brush.stroke";

    /// <summary>开始可逆 transaction。</summary>
    public const string TransactionBeginMethod = "transaction.begin";

    /// <summary>提交 transaction 并合并为一个 Undo item。</summary>
    public const string TransactionCommitMethod = "transaction.commit";

    /// <summary>回滚 transaction。</summary>
    public const string TransactionRollbackMethod = "transaction.rollback";

    /// <summary>读取 transaction 状态。</summary>
    public const string TransactionStatusMethod = "transaction.status";

    /// <summary>创建或恢复 event subscription。</summary>
    public const string EventSubscribeMethod = "event.subscribe";

    /// <summary>确认事件 sequence。</summary>
    public const string EventAckMethod = "event.ack";

    /// <summary>删除 event subscription。</summary>
    public const string EventUnsubscribeMethod = "event.unsubscribe";

    /// <summary>Server→Client 的 event envelope method。</summary>
    public const string EventNotificationMethod = "event.notification";

    /// <summary>任意已提交权威状态写入后的通用事件类型。</summary>
    public const string StateChangedEventType = "editor.state.changed";

    /// <summary>transaction commit/rollback/expiry 事件类型。</summary>
    public const string TransactionChangedEventType = "editor.transaction.changed";

    /// <summary>当前协议版本。</summary>
    public static AutomationProtocolVersion CurrentVersion { get; } = new(CurrentMajor, CurrentMinor);
}

/// <summary>
/// 稳定结构化错误码。
/// </summary>
public static class AutomationErrorCodes
{
    /// <summary>请求格式或字段无效。</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>frame 或协议无效。</summary>
    public const string InvalidProtocol = "invalid_protocol";

    /// <summary>没有共同协议版本。</summary>
    public const string ProtocolVersionUnsupported = "protocol_version_unsupported";

    /// <summary>会话尚未认证。</summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>认证 proof 无效。</summary>
    public const string AuthenticationFailed = "authentication_failed";

    /// <summary>scope 不允许该操作。</summary>
    public const string PermissionDenied = "permission_denied";

    /// <summary>请求 deadline 已过。</summary>
    public const string DeadlineExceeded = "deadline_exceeded";

    /// <summary>请求已被取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>请求队列或并发额度已满。</summary>
    public const string Busy = "busy";

    /// <summary>方法不存在。</summary>
    public const string MethodNotFound = "method_not_found";

    /// <summary>服务端执行失败。</summary>
    public const string Internal = "internal_error";

    /// <summary>optimistic concurrency 前置 revision 已过期。</summary>
    public const string RevisionConflict = "revision_conflict";

    /// <summary>幂等 key 被不同请求复用。</summary>
    public const string IdempotencyConflict = "idempotency_conflict";

    /// <summary>transaction 不存在、不属于当前 session 或已结束。</summary>
    public const string TransactionInvalid = "transaction_invalid";

    /// <summary>另一个 transaction 正持有互斥写租约。</summary>
    public const string TransactionConflict = "transaction_conflict";

    /// <summary>transaction commit 的预校验或某个 staged operation 失败且已回滚。</summary>
    public const string TransactionFailed = "transaction_failed";

    /// <summary>transaction commit 失败后无法完整恢复 before image。</summary>
    public const string TransactionRollbackFailed = "transaction_rollback_failed";

    /// <summary>event replay window 已淘汰所需 sequence。</summary>
    public const string ResyncRequired = "resync_required";

    /// <summary>慢消费者超过订阅 backlog。</summary>
    public const string EventOverflow = "event_overflow";

    /// <summary>artifact session 或单文件配额不足。</summary>
    public const string ArtifactQuotaExceeded = "artifact_quota_exceeded";

    /// <summary>canonical path 越过获准 root 或包含链接逃逸。</summary>
    public const string PathNotAllowed = "path_not_allowed";

    /// <summary>请求在当前 Editor/项目/Play 状态不可执行。</summary>
    public const string StateUnavailable = "state_unavailable";

    /// <summary>稳定 ID 指向的 Editor、Scene、asset 或 runtime 资源不存在。</summary>
    public const string ResourceNotFound = "resource_not_found";

    /// <summary>semantic handler 返回了本应写入 artifact 的超限控制面响应。</summary>
    public const string ResponseTooLarge = "response_too_large";
}
