# CODEX-HANDOFF — 新计划已就绪信号

> **本文件的存在即信号**：`plan/` 根目录发现本文件 → 新一轮计划补充已制定完成并落盘，Codex 可据 `plan/` 继续后续开发。
> **未发现本文件** → 计划尚未就绪，**等待 10 分钟后再检查**，不要凭旧计划抢跑。

---

## 版本戳（用于对照）

- 修订标识：`plan-supplement-2026-07-04`
- 制定日期：2026-07-04
- 触发需求：用户新增 5 项确认需求（独立编辑器应用 / 编辑器内打包 / Windows 优先打包+目录整理 / HTML 交互 UI / Demo 真实可玩化）
- 制定方式：多智能体 Workflow（并行设计 → 对抗校验 → 跨计划一致性综合 → 逐文件落盘），已做跨文件一致性收口。

若 Codex 记录的对照标识已是 `plan-supplement-2026-07-04`，说明本轮计划已被读取，无需重复接手。

---

## 本轮计划变更清单

**新增文档（2 个）**
- `plan/19-standalone-editor-app.md` — 独立编辑器应用 `apps/PixelEngine.Editor.Shell`（独立 EXE/单窗口/单进程/单 GL 上下文），含 §0 GUI 宿主中性化重构入口门、Project 模型、类 Unity GameObject authoring（层级/Inspector/变换 gizmo/点选拾取/prefab 含嵌套+override）、`.scene` v2 保存往返、复用 plan/12 面板、§5 编辑器内打包（BuildSettings 面板经子进程调 build-player 出**不含编辑器**的玩家包）。
- `plan/20-interactive-html-ui.md` — 游戏内 HTML 大 UI 模块 `PixelEngine.UI`（RmlUi 子集主后端 + Ultralight 可选 + `ManagedFallbackBackend` 纯托管基线）。

**修订文档**
- `plan/00`（结构/依赖方向定稿/选型表加 UI 后端与门控依赖/RID Windows 优先）
- `plan/03`（per-cell `Damage(byte)` SoA 平面 + `ApplyStructuralDamage` + RigidOwned 路由 + `DamageCircle`/`DamageBeam`）
- `plan/04`（MaterialDef 破坏/视觉字段 Integrity/DestroyedTarget/RenderStyle/EdgeColor/Opacity/Highlight + flowRate + materials.json）
- `plan/05`（破坏碎屑/火花粒子）· `plan/06`（RigidOwned 破坏经 `IRigidDamageSink` 复用形状重建）
- `plan/07`（Damage 平面入存档：ChunkSnapshot/ChunkCodec RLE 段 + bump SaveFormatVersion + 旧档迁移 + 预算 16KB→20KB）
- `plan/08`（RenderStyle 差异化着色 + Damage 裂纹 + `RegisterUiLayer` 带序号 UI 层合成）
- `plan/11`（IWorldEffects `DamageCircle`/`DamageBeam`+DamageKind、MaterialInfo 扩展、`.scene` v2、`IGameUiService` 中性契约）
- `plan/12`（编辑器锁定决策措辞调和 + 输入多级仲裁 + MaterialLegendPreview；未改动已完成节点 1–9）
- `plan/13`（Demo 收敛纯玩家运行时 + 数据驱动武器系统六类 + 熔岩矿洞逃生可玩循环）
- `plan/14`（新增测试：.scene v1/v2 往返、Damage 存档、破坏路由、weapons 加载、任务胜负、build-player NDJSON、player-only 审计）
- `plan/15`（发行拆玩家包/编辑器工具包 + Windows 优先 + `app/` 子目录布局契约 + build-player 入口 + 审计规则修正）
- `plan/16`（per-cell 字节预算治理 4B→5B + UI 相位计时 + 可玩循环 profiling 负载）
- `plan/17`（新增里程碑 **M13/M14** + 硬顺序约束表 + 提交节点 32+ + M12 退出标准改为 win-x64 优先）
- `plan/18`（相位[10] 子序 + GUI 宿主中性化 §3.7/§4.1 + 窗口/GL 所有权解耦 bootstrap + `.scene` 保存往返 API + HTML UI 装配）
- `plan/README.md`（文档清单/进度总览/执行顺序/模块归属/证据索引更新）
- `AGENTS.md`（不变式 **#10** 措辞修订：Box2D 仍是唯一 sim-native/dual-build 内核；可选、带纯托管回退、按 RID 动态分发的 UI 内核归门控类，不计入单 native 硬约束）

---

## Codex 从哪继续（执行顺序，务必遵守强前置）

新增工作全部编织进 **M13 → M14**（见 `plan/17` §3 里程碑表、§4.1 硬顺序约束表、§5 提交节点总表第 32 条起）。

1. **M13 入口门（最先，阻塞其余一切编辑器/玩家包/UI 工作）**：GUI 宿主中性化重构 —— 抽出 `PixelEngine.Gui`（下沉 HexaImGuiBackend/IGuiContext 运行时适配/GuiFontManager 含 CJK）、`Hosting` 删除对 `PixelEngine.Editor` 的硬 `ProjectReference` 改暴露抽象 GUI/相位[10] 钩子接口、`DemoProgram` 改用中性 host（提交节点 32–33）。
2. **M13 其余**：窗口/GL 所有权解耦 + 编辑态 bootstrap（34）→ 独立编辑器壳（35）→ GameObject 层级/Inspector/gizmo（36）→ prefab + `.scene` v2 往返（37）→ build-player 编排器（38）→ 编辑器内 BuildSettings 面板（39）→ Windows-first RID 门控 + `app/` 布局 + player-only 审计（40）。
3. **M14（可与 M13 部分并行，但 Damage 平面须先 bump SaveFormatVersion 再动存档）**：Damage 平面（41）→ MaterialDef 破坏/视觉字段（42）→ 差异化着色（43）→ 武器系统与熔岩矿洞逃生循环 → `plan/20` PixelEngine.UI（RmlUi）。

**硬前置提醒**：`plan/20` 的 UI 基线后端依赖 M13 交付的 `PixelEngine.Gui`；player-only 审计依赖 GUI 中性化落地；三处打包触点收敛到同一 `app/` 目录 + `build-result.json` 契约。细节以 `plan/17 §4.1` 表为准。

遵守 `AGENTS.md`：一步到位、无 MVP；完成每个提交节点即用中文 git 提交（§6 格式）；计划与不变式冲突先改计划再改代码；阻塞标 `- [!]` 上报，不写假实现。
