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

    /// <summary>在指定位置原子创建并打开 PixelEngine 工程。</summary>
    public const string WorkspaceProjectCreateMethod = "workspace.project.create";

    /// <summary>从 canonical path 打开已有 PixelEngine 工程。</summary>
    public const string WorkspaceProjectOpenMethod = "workspace.project.open";

    /// <summary>关闭当前 PixelEngine 工程并返回 Project Picker。</summary>
    public const string WorkspaceProjectCloseMethod = "workspace.project.close";

    /// <summary>经 dirty guard 请求退出 Editor。</summary>
    public const string WorkspaceExitMethod = "workspace.exit";

    /// <summary>分页读取 Project Picker 最近工程。</summary>
    public const string WorkspaceRecentListMethod = "workspace.recent.list";

    /// <summary>按稳定工程 ID 原子设置最近工程收藏状态。</summary>
    public const string WorkspaceRecentFavoriteSetMethod = "workspace.recent.favorite.set";

    /// <summary>按稳定工程 ID 原子移除最近工程。</summary>
    public const string WorkspaceRecentRemoveMethod = "workspace.recent.remove";

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

    /// <summary>读取当前工程与 Asset Database 摘要。</summary>
    public const string ProjectGetMethod = "project.get";

    /// <summary>分页读取全部稳定资产。</summary>
    public const string ProjectAssetListMethod = "project.assets.list";

    /// <summary>后台完整刷新 Project Window 资产数据库并原子发布。</summary>
    public const string ProjectAssetRefreshMethod = "project.assets.refresh";

    /// <summary>分页读取 Project Window 文件夹。</summary>
    public const string ProjectFolderListMethod = "project.folders.list";

    /// <summary>按 stable asset id 读取资产。</summary>
    public const string ProjectAssetGetMethod = "project.asset.get";

    /// <summary>读取 Project Window 稳定选择。</summary>
    public const string ProjectSelectionGetMethod = "project.selection.get";

    /// <summary>设置或清除 Project Window 稳定选择。</summary>
    public const string ProjectSelectionSetMethod = "project.selection.set";

    /// <summary>读取 Project Window 搜索、过滤、排序与展示状态。</summary>
    public const string ProjectWindowGetMethod = "project.window.get";

    /// <summary>原子更新 Project Window 搜索、过滤、排序或展示状态。</summary>
    public const string ProjectWindowSetMethod = "project.window.set";

    /// <summary>读取资产详细预览并把大内容发布为制品。</summary>
    public const string ProjectAssetPreviewMethod = "project.asset.preview";

    /// <summary>使用已配置的外部编辑器打开脚本资产。</summary>
    public const string ProjectAssetScriptOpenMethod = "project.asset.script.open";

    /// <summary>使用已配置的 IDE 打开当前工程的完整 C# workspace/solution。</summary>
    public const string ProjectCodeOpenMethod = "project.code.open";

    /// <summary>通过 Editor 音频预览服务试听音频资产。</summary>
    public const string ProjectAssetAudioPreviewMethod = "project.asset.audio.preview";

    /// <summary>分页读取一个稳定资产的引用位置。</summary>
    public const string ProjectAssetReferencesMethod = "project.asset.references.list";

    /// <summary>创建 Project 资产或文件夹。</summary>
    public const string ProjectAssetCreateMethod = "project.asset.create";

    /// <summary>从获准 import root 导入外部文件。</summary>
    public const string ProjectAssetImportMethod = "project.asset.import";

    /// <summary>从获准 import root 原子替换已有资产内容并保留 stable ID。</summary>
    public const string ProjectAssetReplaceMethod = "project.asset.replace";

    /// <summary>移动或重命名 Project 资产。</summary>
    public const string ProjectAssetMoveMethod = "project.asset.move";

    /// <summary>安全删除 Project 资产。</summary>
    public const string ProjectAssetDeleteMethod = "project.asset.delete";

    /// <summary>移动或重命名 Project 文件夹。</summary>
    public const string ProjectFolderMoveMethod = "project.folder.move";

    /// <summary>安全递归删除 Project 文件夹。</summary>
    public const string ProjectFolderDeleteMethod = "project.folder.delete";

    /// <summary>读取 UI Manifest screens 与 preload 状态。</summary>
    public const string ProjectUiManifestGetMethod = "project.ui-manifest.get";

    /// <summary>把已发现 UI screen 同步到 UI Manifest。</summary>
    public const string ProjectUiManifestSyncMethod = "project.ui-manifest.sync";

    /// <summary>设置 UI Manifest screen preload。</summary>
    public const string ProjectUiManifestPreloadSetMethod = "project.ui-manifest.preload.set";

    /// <summary>分页读取 Console 原始日志。</summary>
    public const string ConsoleListMethod = "console.entries.list";

    /// <summary>读取 Console 严重度计数。</summary>
    public const string ConsoleCountsGetMethod = "console.counts.get";

    /// <summary>读取 Console 工具栏与 Play 联动选项。</summary>
    public const string ConsoleOptionsGetMethod = "console.options.get";

    /// <summary>原子替换 Console 工具栏与 Play 联动选项。</summary>
    public const string ConsoleOptionsSetMethod = "console.options.set";

    /// <summary>清空 Console 环形缓冲。</summary>
    public const string ConsoleClearMethod = "console.clear";

    /// <summary>把 Console 全量快照导出为 JSON 制品。</summary>
    public const string ConsoleExportMethod = "console.export";

    /// <summary>读取 Console 当前选中行与详情。</summary>
    public const string ConsoleSelectionGetMethod = "console.selection.get";

    /// <summary>选择或清除一个稳定 Console entry。</summary>
    public const string ConsoleSelectionSetMethod = "console.selection.set";

    /// <summary>生成与 Console Copy 上下文动作相同的文本。</summary>
    public const string ConsoleEntryCopyMethod = "console.entry.copy";

    /// <summary>使用配置的脚本编辑器打开 Console entry 源码位置。</summary>
    public const string ConsoleEntryOpenSourceMethod = "console.entry.open-source";

    /// <summary>读取当前 Play session。</summary>
    public const string PlayGetMethod = "play.get";

    /// <summary>进入新的 Play session。</summary>
    public const string PlayEnterMethod = "play.enter";

    /// <summary>暂停当前 Play session。</summary>
    public const string PlayPauseMethod = "play.pause";

    /// <summary>恢复当前 Play session。</summary>
    public const string PlayResumeMethod = "play.resume";

    /// <summary>执行恰好一个暂停帧 step。</summary>
    public const string PlayStepMethod = "play.step";

    /// <summary>停止 Play 并恢复 authoring before-image。</summary>
    public const string PlayStopMethod = "play.stop";

    /// <summary>读取当前 runtime world 摘要。</summary>
    public const string RuntimeWorldGetMethod = "runtime.world.get";

    /// <summary>分页读取当前 play-session scoped runtime entities。</summary>
    public const string RuntimeEntityListMethod = "runtime.entities.list";

    /// <summary>读取一个 play-session scoped runtime entity。</summary>
    public const string RuntimeEntityGetMethod = "runtime.entity.get";

    /// <summary>分页读取 play-session scoped runtime rigid bodies。</summary>
    public const string RuntimeBodyListMethod = "runtime.bodies.list";

    /// <summary>读取一个 play-session scoped runtime rigid body。</summary>
    public const string RuntimeBodyGetMethod = "runtime.body.get";

    /// <summary>临时修改 Play/Paused runtime entity Transform。</summary>
    public const string RuntimeEntityTransformSetMethod = "runtime.entity.transform.set";

    /// <summary>临时修改 Play/Paused runtime Behaviour Inspector 字段。</summary>
    public const string RuntimeComponentFieldSetMethod = "runtime.component.field.set";

    /// <summary>读取当前 Engine simulation 控制状态。</summary>
    public const string RuntimeSimulationGetMethod = "runtime.simulation.get";

    /// <summary>设置当前 Engine 请求的 simulation 频率。</summary>
    public const string RuntimeSimulationSetMethod = "runtime.simulation.set";

    /// <summary>读取指定世界坐标的 runtime cell 诊断快照。</summary>
    public const string RuntimeCellInspectMethod = "runtime.cell.inspect";

    /// <summary>读取 World Inspector 面板跟随/锁定状态与最近显示结果。</summary>
    public const string RuntimeWorldInspectorGetMethod = "runtime.world-inspector.get";

    /// <summary>设置 World Inspector 跟随模式与锁定坐标。</summary>
    public const string RuntimeWorldInspectorSetMethod = "runtime.world-inspector.set";

    /// <summary>分页读取当前 Engine 的稳定 material catalog。</summary>
    public const string RuntimeMaterialListMethod = "runtime.materials.list";

    /// <summary>按稳定名称读取一个完整 runtime material 定义。</summary>
    public const string RuntimeMaterialGetMethod = "runtime.material.get";

    /// <summary>读取 Materials/Reactions 面板完整草稿与 runtime bindings。</summary>
    public const string MaterialEditorGetMethod = "materials.editor.get";

    /// <summary>原子替换 Materials/Reactions 面板完整草稿。</summary>
    public const string MaterialEditorSetMethod = "materials.editor.set";

    /// <summary>从 materials.json 与 reactions.json 重新加载面板草稿。</summary>
    public const string MaterialEditorReloadMethod = "materials.editor.reload";

    /// <summary>预览 tag 展开与 packed reaction 数量。</summary>
    public const string MaterialEditorPreviewMethod = "materials.editor.preview";

    /// <summary>原子持久化双文件并稳定热重载运行时材质、反应与 live grid。</summary>
    public const string MaterialEditorApplyMethod = "materials.editor.apply";

    /// <summary>读取 Physics 运行时调参与统计。</summary>
    public const string RuntimePhysicsGetMethod = "runtime.physics.get";

    /// <summary>设置 Physics 运行时调参。</summary>
    public const string RuntimePhysicsSetMethod = "runtime.physics.set";

    /// <summary>读取自由粒子运行时调参与统计。</summary>
    public const string RuntimeParticlesGetMethod = "runtime.particles.get";

    /// <summary>设置自由粒子运行时调参。</summary>
    public const string RuntimeParticlesSetMethod = "runtime.particles.set";

    /// <summary>读取 Lighting 运行时调参。</summary>
    public const string RuntimeLightingGetMethod = "runtime.lighting.get";

    /// <summary>设置 Lighting 运行时调参。</summary>
    public const string RuntimeLightingSetMethod = "runtime.lighting.set";

    /// <summary>分页列出 Editor 世界存档 slots。</summary>
    public const string RuntimeSaveSlotListMethod = "runtime.saves.list";

    /// <summary>在 Engine world 安全点冻结并原子发布一个粗粒度存档。</summary>
    public const string RuntimeSaveSlotSaveMethod = "runtime.saves.save";

    /// <summary>后台完整解码后在 Engine world 安全点原子加载一个粗粒度存档。</summary>
    public const string RuntimeSaveSlotLoadMethod = "runtime.saves.load";

    /// <summary>读取 Game View presentation、preset 与显示状态。</summary>
    public const string GamePresentationGetMethod = "game.presentation.get";

    /// <summary>原子替换 Game View presentation、preset 与显示状态。</summary>
    public const string GamePresentationSetMethod = "game.presentation.set";

    /// <summary>捕获 Scene View 可见 authoring viewport 为图片制品。</summary>
    public const string SceneCaptureMethod = "scene.capture";

    /// <summary>捕获最新完整 Game presentation 为图片制品。</summary>
    public const string GameCaptureMethod = "game.capture";

    /// <summary>读取 Editor Preferences。</summary>
    public const string PreferencesGetMethod = "settings.preferences.get";

    /// <summary>原子写入 Editor Preferences。</summary>
    public const string PreferencesSetMethod = "settings.preferences.set";

    /// <summary>分页读取菜单、调度器与 Preferences 共用的快捷键目录。</summary>
    public const string ShortcutListMethod = "settings.shortcuts.list";

    /// <summary>读取 Project Settings。</summary>
    public const string ProjectSettingsGetMethod = "settings.project.get";

    /// <summary>原子写入 Project Settings。</summary>
    public const string ProjectSettingsSetMethod = "settings.project.set";

    /// <summary>读取 Player Settings。</summary>
    public const string PlayerSettingsGetMethod = "settings.player.get";

    /// <summary>原子写入 Player Settings。</summary>
    public const string PlayerSettingsSetMethod = "settings.player.set";

    /// <summary>读取 Build Settings profile。</summary>
    public const string BuildSettingsGetMethod = "settings.build.get";

    /// <summary>原子写入 Build Settings profile。</summary>
    public const string BuildSettingsSetMethod = "settings.build.set";

    /// <summary>读取上一帧 Profiler 与运行诊断。</summary>
    public const string ProfilerGetMethod = "profiler.get";

    /// <summary>把 Profiler 快照导出为 JSON 制品。</summary>
    public const string ProfilerExportMethod = "profiler.export";

    /// <summary>通过真实 present 控制器切换 Profiler VSync。</summary>
    public const string ProfilerVSyncSetMethod = "profiler.vsync.set";

    /// <summary>读取 debug overlay flags。</summary>
    public const string DebugOverlayGetMethod = "debug.overlay.get";

    /// <summary>设置一个 debug overlay flag。</summary>
    public const string DebugOverlaySetMethod = "debug.overlay.set";

    /// <summary>重新执行 build tool preflight 并返回结构化结果。</summary>
    public const string BuildPreflightMethod = "build.preflight";

    /// <summary>使用当前 Build Settings 启动异步玩家构建。</summary>
    public const string BuildStartMethod = "build.start";

    /// <summary>分页读取当前 Editor 实例保留的 build jobs。</summary>
    public const string BuildListMethod = "build.list";

    /// <summary>读取一个 build job。</summary>
    public const string BuildGetMethod = "build.get";

    /// <summary>等待一个 build job 到达终态。</summary>
    public const string BuildWaitMethod = "build.wait";

    /// <summary>请求取消一个仍在运行的 build job。</summary>
    public const string BuildCancelMethod = "build.cancel";

    /// <summary>把一个 build job 的有界日志导出为 JSON 制品。</summary>
    public const string BuildLogExportMethod = "build.logs.export";

    /// <summary>从成功 build 的受信 launcher 启动玩家进程。</summary>
    public const string PlayerLaunchMethod = "player.launch";

    /// <summary>分页读取由当前 Editor 启动并保留的玩家进程。</summary>
    public const string PlayerListMethod = "player.list";

    /// <summary>读取一个玩家进程。</summary>
    public const string PlayerGetMethod = "player.get";

    /// <summary>等待一个玩家进程退出。</summary>
    public const string PlayerWaitMethod = "player.wait";

    /// <summary>终止一个玩家进程及其进程树。</summary>
    public const string PlayerTerminateMethod = "player.terminate";

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

    /// <summary>分页读取当前 session 拥有的 artifact。</summary>
    public const string ArtifactListMethod = "artifact.list";

    /// <summary>从磁盘重新校验 artifact 长度与 SHA256。</summary>
    public const string ArtifactVerifyMethod = "artifact.verify";

    /// <summary>删除当前 session 拥有的 artifact。</summary>
    public const string ArtifactDeleteMethod = "artifact.delete";

    /// <summary>Server→Client 的 event envelope method。</summary>
    public const string EventNotificationMethod = "event.notification";

    /// <summary>任意已提交权威状态写入后的通用事件类型。</summary>
    public const string StateChangedEventType = "editor.state.changed";

    /// <summary>transaction commit/rollback/expiry 事件类型。</summary>
    public const string TransactionChangedEventType = "editor.transaction.changed";

    /// <summary>workspace、工程或转场状态发生变化。</summary>
    public const string WorkspaceChangedEventType = "editor.workspace.changed";

    /// <summary>平台窗口状态发生变化。</summary>
    public const string WindowChangedEventType = "editor.window.changed";

    /// <summary>panel、dock 或布局状态发生变化。</summary>
    public const string LayoutChangedEventType = "editor.layout.changed";

    /// <summary>Scene authoring 状态发生变化。</summary>
    public const string SceneChangedEventType = "editor.scene.changed";

    /// <summary>Hierarchy 内容发生变化。</summary>
    public const string HierarchyChangedEventType = "editor.hierarchy.changed";

    /// <summary>编辑器 selection 发生变化。</summary>
    public const string SelectionChangedEventType = "editor.selection.changed";

    /// <summary>Inspector 可观察内容发生变化。</summary>
    public const string InspectorChangedEventType = "editor.inspector.changed";

    /// <summary>Scene tool、gizmo、grid、snap 或 brush 状态发生变化。</summary>
    public const string ToolChangedEventType = "editor.tool.changed";

    /// <summary>Project asset/folder 数据库发生变化。</summary>
    public const string AssetsChangedEventType = "editor.assets.changed";

    /// <summary>Console 内容或计数发生变化。</summary>
    public const string ConsoleChangedEventType = "editor.console.changed";

    /// <summary>Play/Pause/Step/Stop 状态发生变化。</summary>
    public const string PlayChangedEventType = "editor.play.changed";

    /// <summary>运行时 world/entity/component snapshot 失效。</summary>
    public const string RuntimeChangedEventType = "editor.runtime.changed";

    /// <summary>Game View presentation 状态发生变化。</summary>
    public const string GameChangedEventType = "editor.game.changed";

    /// <summary>Preferences、Project、Player 或 Build settings 发生变化。</summary>
    public const string SettingsChangedEventType = "editor.settings.changed";

    /// <summary>Profiler snapshot 或采集状态发生变化。</summary>
    public const string ProfilerChangedEventType = "editor.profiler.changed";

    /// <summary>debug overlay 状态发生变化。</summary>
    public const string DebugChangedEventType = "editor.debug.changed";

    /// <summary>build job 或 build output 状态发生变化。</summary>
    public const string BuildChangedEventType = "editor.build.changed";

    /// <summary>由 Editor 启动的 player process 状态发生变化。</summary>
    public const string PlayerChangedEventType = "editor.player.changed";

    /// <summary>session artifact 集合发生变化。</summary>
    public const string ArtifactChangedEventType = "editor.artifact.changed";

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
