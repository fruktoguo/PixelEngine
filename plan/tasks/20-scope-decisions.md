# 范围与完成口径

本文件消除旧计划中反复出现的产品范围矛盾。`SCOPE-*` 是已经定稿的主线规则；`OPT-*` 是真实但非阻塞的可选 backlog。

## 已定稿规则

- [x] `SCOPE-001` 1.0 采用 Windows-first：`win-x64` 是必需发行目标，`win-arm64` 条件激活；linux/osx 保留一等代码兼容和 CI 矩阵位，但 dormant RID 不要求在 Windows-first 1.0 同时发布。若激活 macOS，再启用签名/公证硬门禁。
- [x] `SCOPE-002` 生产 GPU 基线是 OpenGL/GL compute；ComputeSharp 仅为显式启用的实验后端。没有 D3D12 render graph 或合法 GL-DX12 shared-resource/fence 前，必须保持不可执行，且不阻塞 Windows-first 1.0。
- [x] `SCOPE-003` 生产 UI 基线是 RmlUi 主后端 + ManagedFallback 必备回退；Ultralight 是许可与 native SDK 驱动的可选高保真 profile，默认 inactive，不阻塞 Windows-first 1.0。
- [x] `SCOPE-004` M14 表示必需产品能力和自动化链闭合；M15 表示真实窗口人工体验、目标硬件、native leak、远端 CI 和发行证据闭合。M14 条目不得再被 M15 的可选后端或外部发行证据重复阻塞。
- [x] `SCOPE-005` 证据状态严格分层：自动化测试证明契约，scripted probe 证明入口，人工材料证明体验，目标硬件报告证明性能，tag workflow 证明发行。任何较低层证据不得冒充较高层完成态。
- [x] `SCOPE-006` Showcase Demo 的唯一正式验收路线是“横向熔岩矿洞逃生”：从左侧出生点穿越熔岩坑和可拆障碍到达右侧出口；旧“引水成石桥/坍塌木桥”仅保留为历史机制测试，不再作为产品完成条件。

## 可选 backlog

- [ ] `OPT-001` 实现可执行 ComputeSharp 后端。优先级：P3。前置：正式选择 D3D12 render graph，或实现带非零 device/queue/resource/fence 的 GL-DX12 shared-resource 契约。阻塞主线：否。
- [ ] `OPT-002` 实现 Ultralight 真后端与商业发行 gate。优先级：P3。前置：native SDK/provenance、redistribution license、surface/dirty rect、JS bridge、字体、RID binaries、SHA256/NOTICE。阻塞主线：否。
- [ ] `OPT-003` 激活非 Windows 平台原生 IME 事件源和候选窗集成。优先级：P3。前置：对应 RID 从 dormant 转为 active 产品目标。阻塞 Windows-first 主线：否。
- [ ] `OPT-004` 激活 dormant Linux/macOS 玩家包发行、对应真机 smoke、签名和公证。优先级：P3。前置：修改 `tools/release-rids.json` 并取得对应 runner/凭据。阻塞 Windows-first 主线：否。

## 重新打开规则

- 只有产品定位文档明确把某可选 profile 升为 1.0 必需能力时，`OPT-*` 才能进入主关键路径。
- 激活 RID 或 native profile 时，必须在同一提交更新本文件、`tools/release-rids.json`、相关详细 plan、CI/release matrix 和证据要求。
- 不允许仅因为代码文件存在、package 可还原或本地 load-only 成功，就把可选 profile 标为完成。
