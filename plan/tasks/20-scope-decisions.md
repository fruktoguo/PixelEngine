# 范围与完成口径

本文件消除旧计划中反复出现的产品范围矛盾。`SCOPE-*` 是已经定稿的主线规则；`OPT-*` 是真实但非阻塞的可选 backlog。

## 已定稿规则

- [x] `SCOPE-001` 1.0 采用 Windows-first：`win-x64` 是必需发行目标，`win-arm64` 条件激活；linux/osx 保留一等代码兼容和 CI 矩阵位，但 dormant RID 不要求在 Windows-first 1.0 同时发布。若激活 macOS，再启用签名/公证硬门禁。
- [x] `SCOPE-002` 生产 GPU 基线是 OpenGL/GL compute；ComputeSharp 仅为显式启用的实验后端。没有 D3D12 render graph 或合法 GL-DX12 shared-resource/fence 前，必须保持不可执行，且不阻塞 Windows-first 1.0。
- [x] `SCOPE-003` 生产 UI 基线是 RmlUi 主后端 + ManagedFallback 必备回退；Ultralight 是许可与 native SDK 驱动的可选高保真 profile，默认 inactive，不阻塞 Windows-first 1.0。
- [x] `SCOPE-004` M14 表示必需产品能力和自动化链闭合；M15 表示真实窗口人工体验、目标硬件、native leak、远端 CI 和发行证据闭合。M14 条目不得再被 M15 的可选后端或外部发行证据重复阻塞。
- [x] `SCOPE-005` 证据状态严格分层：自动化测试证明契约，scripted probe 证明入口，人工材料证明体验，目标硬件报告证明性能，tag workflow 证明发行。任何较低层证据不得冒充较高层完成态。
- [x] `SCOPE-006` Showcase Demo 的唯一正式验收路线曾定为“横向熔岩矿洞逃生”：从左侧出生点穿越熔岩坑和可拆障碍到达右侧出口；该历史范围先由 `DEMO-006` 的 Infinite Sandbox、再由 `SCOPE-007` 的双模式战役目标取代，仅用于解释旧证据。
- [x] `SCOPE-007` Showcase Demo 的当前产品目标是“原创像素 Roguelite Campaign + 可选 Infinite Sandbox”。Campaign 借鉴《Noita》公开的下降探索、层间整备、构筑、永久死亡与终局结构，但所有名称、地图、剧情、角色、敌人、法术、数值和资产必须原创；Sandbox 保留 `DEMO-006` 的无终点世界。Demo 只经公开 API 实现，Engine Core 不硬编码玩法专属类型。设计依据：`docs/PixelEngine-原创Roguelite战役设计.md`。
- [x] `SCOPE-008` Showcase Demo 的当前产品目标调整为“Noita 高保真复刻 Campaign + 可选 Infinite Sandbox”，取代 `SCOPE-007` 的原创内容限制。Campaign 允许复现 Noita 公开可观察的名称、主路径/侧区拓扑、Holy Mountain、Wand/Spell、敌人/物品角色、Perk、材料交互、UI 布局和数值语义；正式仓库与玩家包不得依赖本机 Noita 安装或构建时抽取外部二进制资源，资产必须可独立构建并记录 provenance。所有 Noita 专属内容只能存在于 `demo/PixelEngine.Demo`，Engine Core 继续无玩法、无专属类型，Demo 只经公开 API 实现。用户于 2026-07-22 明确授权本次产品转向；设计依据：`docs/PixelEngine-原创Roguelite战役设计.md`（历史文件名）。

## 当前能力声明

`ARCH-005` 的当前产品状态为 `deferred_not_enabled`：GPU air/smoke 仅保留独立 pass、资源和 shader 契约测试，不属于 Windows-first 1.0 的运行时能力。生产 `RenderPipeline` 尚未接入其 seed、dispatch、density 合成和回退链；任何诊断、plan 或支持矩阵都不得把现有源码存在或直接 pass 测试写成已启用能力。

## 可选 backlog

- [ ] `OPT-001` 实现可执行 ComputeSharp 后端。优先级：P3。前置：正式选择 D3D12 render graph，或实现带非零 device/queue/resource/fence 的 GL-DX12 shared-resource 契约。阻塞主线：否。
- [ ] `OPT-002` 实现 Ultralight 真后端与商业发行 gate。优先级：P3。前置：native SDK/provenance、redistribution license、surface/dirty rect、JS bridge、字体、RID binaries、SHA256/NOTICE。阻塞主线：否。
- [ ] `OPT-003` 激活非 Windows 平台原生 IME 事件源和候选窗集成。优先级：P3。前置：对应 RID 从 dormant 转为 active 产品目标。阻塞 Windows-first 主线：否。
- [ ] `OPT-004` 激活 dormant Linux/macOS 玩家包发行、对应真机 smoke、签名和公证。优先级：P3。前置：修改 `tools/release-rids.json` 并取得对应 runner/凭据。阻塞 Windows-first 主线：否。

## 重新打开规则

- 只有产品定位文档明确把某可选 profile 升为 1.0 必需能力时，`OPT-*` 才能进入主关键路径。
- 激活 RID 或 native profile 时，必须在同一提交更新本文件、`tools/release-rids.json`、相关详细 plan、CI/release matrix 和证据要求。
- 不允许仅因为代码文件存在、package 可还原或本地 load-only 成功，就把可选 profile 标为完成。
