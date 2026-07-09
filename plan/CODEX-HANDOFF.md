# CODEX-HANDOFF 历史交接记录

> 本文件记录 `2026-07-04` 的计划补充批次，已被 `2026-07-10` 建立的 [`tasks/README.md`](tasks/README.md) 取代。它不再是“等待/继续开发”信号，也不维护当前执行顺序。

## 当前恢复入口

中断后恢复工作时：

1. 打开 [`tasks/README.md`](tasks/README.md)，确认唯一 `[~]` 任务及其依赖。
2. 进入该任务所在轨道，读取验收条件和设计来源。
3. 按引用回到 `plan/00-*.md` 至 `plan/20-*.md` 查看详细设计。
4. 运行 `./tools/validate-task-catalog.ps1`，确认任务目录与旧计划覆盖没有漂移。

若没有 `[~]` 任务，则严格按主任务目录的阶段顺序选择下一个 `[ ]`；不得仅凭本文件或 Plan 17 的历史 checkbox 抢跑。

## 历史批次

- 修订标识：`plan-supplement-2026-07-04`
- 制定日期：`2026-07-04`
- 原始范围：Unity-like Editor、编辑器内打包、Windows-first 发行、Web-first 透明 HTML UI Runtime、Showcase Demo 产品化。
- 主要新增设计：`plan/19-standalone-editor-app.md`、`plan/20-interactive-html-ui.md`。
- 主要跨域设计：GUI 宿主中性化、Damage SoA 与存档版本、玩家包隔离、RmlUi/ManagedFallback、M13/M14 里程碑。

这些设计仍是有效的历史和架构上下文，但实现状态已归并到 canonical task。Editor/UI/Demo 的剩余工作见 `tasks/50-product-editor-ui-demo.md`，性能和发行证据分别见 `tasks/40-performance.md` 与 `tasks/60-validation-release.md`。
