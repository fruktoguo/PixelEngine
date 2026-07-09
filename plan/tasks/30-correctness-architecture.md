# 正确性与架构任务

本轨道处理本轮深度审计发现、但旧 checkbox 未准确表达的真实代码风险。优先级高于继续扩展 Editor/UI 功能。

## P1：引擎边界

- [x] `ARCH-001` 移除 Core/Editor 中的 Demo 专属 `LavaActiveAreaCells` 语义，改为通用、无玩法含义的自定义 metric channel。
  - 优先级：P1。
  - 依赖：`BASE-002`、`BASE-012`。
  - 设计来源：`AGENTS.md §0`；`plan/02-core-infrastructure.md`；`plan/13-demo-game.md`。
  - 验收：Core、Hosting、Editor 不出现 `lava`/任务/关卡专属诊断字段；Demo 通过公开注册 API发布同等诊断；现有 HUD/探针测试迁移并全绿。

- [x] `ARCH-002` 收紧 `Chunk`/`CellGrid` 的公开可写数组和 `ref` 返回，确保外部调用不能绕过 dirty、parity、KeepAlive 与 rigid-damage 通知。
  - 优先级：P1。
  - 依赖：`BASE-003`、`BASE-006`。
  - 设计来源：`plan/03-simulation-kernel.md`；架构 §5；不变式 #1–#4。
  - 验收：不安全写入口降为 internal 或受控 edit API；Simulation/Hosting/Demo 不依赖裸写；新增 API discipline 与行为回归测试。

- [x] `ARCH-003` 让 Box2D task callback 的 JobSystem 异常可靠传播到当前 physics tick，并阻止继续消费未完整推进的物理状态。
  - 优先级：P1。
  - 依赖：`BASE-006`、`BASE-010`。
  - 设计来源：`plan/06-physics-collision-rigidbody.md`；`plan/18-hosting-runtime.md`。
  - 验收：callback 首异常被保留；`b2World_Step` 返回后立即检查并抛出明确异常或失败结果；异常计数不再是唯一处理；故障注入测试覆盖。

## P2：公开 API 与生产接线

- [x] `ARCH-004` 收敛 Demo 启动器/benchmark probe 对 `PhysicsSystem`、`RenderPipeline` 等具体服务的直接解析，补齐稳定 Hosting/Scripting probe facade。
  - 优先级：P2。
  - 依赖：`BASE-010`、`BASE-011`、`BASE-015`。
  - 设计来源：产品定位 §8；`plan/11-scripting-system.md`；`plan/13-demo-game.md`。
  - 验收：玩法和发行入口只依赖公开稳定 facade；低层服务解析仅允许在引擎内部或专用诊断程序集；项目纪律测试锁定边界。

- [x] `ARCH-005` 将 GPU air/smoke 明确接入生产 RenderPipeline/Hosting，或从基线能力中降级为未启用组件。
  - 优先级：P2。
  - 依赖：`BASE-008`、`SCOPE-002`。
  - 设计来源：`plan/09-gpu-compute.md`。
  - 验收：二选一并写入支持矩阵；若接入，配置能够创建、执行、合成和降级该 pass；若不接入，计划和产品声明不得称其为运行时能力。
  - 完成结果：选择 `deferred_not_enabled`；生产 gate 强制关闭未接入的 air/smoke 能力位，计划、产品范围和硬件功能支持矩阵已同步；证据：`docs/evidence-2026-07-10-arch-005-air-smoke-status.md`。

## 提交边界

- `ARCH-001`、`ARCH-002`、`ARCH-003` 分别独立提交，避免跨子系统回滚困难。
- `ARCH-004` 需同时更新 Demo discipline tests。
- `ARCH-005` 若改变产品 baseline，必须与 `20-scope-decisions.md` 同提交更新。
