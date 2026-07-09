# Canonical Task 模板

新任务只有在无法归入现有 task 时才创建。先更新 `source-coverage.json` 的 `followUpTaskIds` 或 `catalogOnlyTaskIds`；必做任务还必须进入 `requiredExecutionStages`。随后按以下结构写入职责匹配的轨道文件。

```markdown
- [STATE] `AREA-NNN` 简明、可验收的结果描述。
  - 优先级：P0/P1/P2。
  - 依赖：任务 ID；无依赖时写“无”。
  - 设计来源：`plan/NN-name.md` §章节。
  - 阻塞：仅 `[!]` 使用，写清外部条件和解除方式。
  - 验收：实现结果；自动测试；性能/产品/发行证据。
```

规则：

- `STATE` 只能在实际任务中替换为 ` `、`~`、`!` 或 `x`。
- 一个 ID 只能出现一个状态 checkbox；验收条件使用普通文本，不再建立子 checkbox。
- ID 按稳定职责域命名，不按里程碑或文件编号命名。
- 已有任务的范围扩充优先更新原任务，避免拆出仅用于重复汇报的状态项。
- 新增后必须运行 `./tools/validate-task-catalog.ps1`。
