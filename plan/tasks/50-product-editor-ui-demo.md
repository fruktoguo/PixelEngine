# Editor、UI 与 Demo 产品闭合

本轨道不重复已经由自动化证明的能力。每项状态代表“真实用户工作流是否完整闭合”，因此当前多数任务等待真实设备和人工 reviewer，而不是继续堆 scripted probe。

## Unity-like Editor

- [!] `EDITOR-001` 完成 Project Window 真实工作流验收。阻塞：需要真实窗口 reviewer 和当前 HEAD 录屏/报告。
  - 优先级：P1。
  - 设计来源：`plan/19-standalone-editor-app.md`。
  - 验收：文件夹浏览、创建、导入、搜索/过滤、选择→Inspector、drag/drop、move/rename、引用重写、删除确认、只读预览和错误恢复全部使用真实鼠标键盘走通。

- [!] `EDITOR-002` 完成 Game View 的 viewport、DPI、输入、透明 UI capture/pass-through 和 IME 坐标真实窗口验收。阻塞：需要真实 DPI/输入法环境和 reviewer。
  - 优先级：P1。
  - 验收：resize/DPI 切换无一帧旧坐标；面板空白区阻断；图像透明区透传；交互区捕获；IME caret/candidate 锚点位于实际控件。

- [!] `EDITOR-003` 完成默认工作台 author→play→edit→build→run 产品验收。阻塞：需要真实窗口 reviewer。
  - 优先级：P1。
  - 验收：Hierarchy/Inspector/Scene View/gizmo/Undo/Redo/Console/Prefab/Settings/外部脚本编辑/Build And Run 可理解、可恢复；720p 和高 DPI 无标签截断或工具区溢出。

## Web-first 透明 UI

- [!] `UI-001` 完成 RmlUi/ManagedFallback 透明 UI 产品面验收。阻塞：需要真实窗口视频和 reviewer。
  - 优先级：P1。
  - 验收：world→game UI→editor overlay 顺序正确；透明区 alpha 正确；capture/pass-through 正确；HUD/menu/settings/pause/result 实际点击闭环。

- [!] `UI-002` 完成 Windows IMM32 composition、preedit、selection、candidate window 和 focus 清理产品验收。阻塞：需要真实中文输入法和 reviewer。
  - 优先级：P1。
  - 验收：输入、确认、取消、切焦点、resize/DPI、Game View 嵌入场景均无丢字、错位或残留；ManagedFallback/RmlUi 行为一致。

- [!] `UI-003` 完成 RmlUi desktop GL 与 ANGLE/GLES3 产品级透明合成、状态恢复和发行 smoke。阻塞：需要 ANGLE/GLES 真实窗口和稳定 native 产物。
  - 优先级：P2。
  - 验收：不是 create-renderer/load-only；真实 UI 文档能交互、合成、unload，GL 状态恢复且 fallback gate 正确。

## Showcase Demo Game

- [!] `DEMO-001` 完成角色控制、沙堆/RigidOwned 承载、刚体推动和武器输入手感验收。阻塞：需要真实键鼠/手柄体验 reviewer。
  - 优先级：P1。
  - 验收：跑跳/墙跳/coyote/buffer 可控；不穿不陷；刚体交互可预测；输入延迟和相机不造成眩晕或误操作。

- [!] `DEMO-002` 完成材质反应、地形破坏、刚体碎裂、粒子、fog、光照和 bloom 的完整路线视觉验收。阻塞：依赖 `PERF-002`/`PERF-003` 后的稳定帧率和 reviewer。
  - 优先级：P1。
  - 验收：展示点在实际路线中可见且可复现；无悬空静态岛、残影、调试污染、粒子泄漏或不可读反馈。

- [!] `DEMO-003` 完成材质音效、ambient、定位、限频和爆音/泄漏听感验收。阻塞：需要真实音频设备和 reviewer。
  - 优先级：P1。
  - 验收：impact/fire/splash/explosion/shatter/sizzle/corrosion 可辨识；空间定位合理；高密度事件不爆音；长跑无 source/buffer 泄漏。

- [!] `DEMO-004` 完成独立玩家包的主菜单→设置→开始→完整右出口路线→胜负→重开/退出闭环。阻塞：依赖 `REL-001` 当前 HEAD 玩家包和 reviewer。
  - 优先级：P1。
  - 验收：不启动 Editor；七个 UI screen 全部可用；正式 `lava-mine.scene` 可完成/失败；重开恢复任务、武器、刚体和 UI 基线。

- [!] `DEMO-005` 完成开发态真实窗口脚本热重载验收。阻塞：需要真实编辑器、外部代码编辑器和 reviewer。
  - 优先级：P2。
  - 验收：Play 中修改 Behaviour 源码，Roslyn+ALC 重载成功；场景、世界和公开字段状态按契约保留；编译错误可见且修复后恢复；旧 ALC 可回收。

## 对外文档

- [ ] `DOC-001` 新增根 README、Getting Started 和一个从新建工程到 build-player 的最小完整教程。
  - 优先级：P1。
  - 依赖：`EDITOR-003`、`REL-001` 的命令和界面稳定后收口；可先写骨架。
  - 验收：全新用户能安装前置、启动 Editor、创建脚本/场景、Play、构建并运行玩家包；命令在干净目录复验。

- [~] `DOC-002` 清理旧计划中的过时项目数、旧 Demo 路线、失效 artifacts/scratch 链接和“CI 已完成”等错误声明。
  - 优先级：P2。
  - 依赖：`PLAN-001`。
  - 验收：旧设计仍可追溯，但不再显示为 live 状态；历史结果标 commit/date/evidence level；失效路径改为可重跑命令或稳定报告。
  - 证据：`docs/evidence-2026-07-10-doc-002-legacy-plan-audit.md`；21 份旧计划均含冻结快照 / 迁移 commit / evidence-level 标记，1692 条旧 checkbox SHA256 未漂移，32/32 工程清单无差异，当前 Demo 路线合同 3/3 通过。
