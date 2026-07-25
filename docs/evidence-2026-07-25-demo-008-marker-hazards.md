# DEMO-008 危险 marker 运行时证据

## 已实现

- 七类已确认危险来源 marker 生成 Demo 侧纯 C# `NoitaMarkerHazard`，不再复用统一 Spark/Material emitter 占位。
- 水平激光和纵向 laser gate 使用公开 `ISolidSampler.Raycast` 在首个固体前截断，并按光束厚度与玩家 AABB 交叠持续施加伤害。
- 电击陷阱以固定周期产生电弧粒子，仅在 30 cell 半径且视线未被固体遮挡时伤害玩家。
- 酸液、cloud trap 与燃烧桶按固定周期向权威 cell 网格写入 `acid` 或 `fire`，危险材料继续由现有 `PlayerHealth` 材料采样链处理。
- 危险实体使用 overlay、点光与自由粒子提供即时反馈，并跟随 marker streaming 生命周期启停。

## 验证

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaMarkerHazardTests|FullyQualifiedName~NoitaMarkerLootTests|FullyQualifiedName~NoitaMarkerEnemyTests"
```

结果：`23 passed / 0 failed`。覆盖七类危险 profile、水平/垂直光束 AABB、以及相邻 Enemy/Loot marker 分支回归。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore
```

结果：`0 warning / 0 error`。

## 未完成

- 当前装置仍以几何 overlay 表达，真实 sprite、动画、破坏后残骸和刚体行为尚未闭合。
- laser gate 暂按单向光束实现，来源中的成对发射器和开关逻辑仍需专属拓扑数据。
- 电击只实现装置到玩家的视线脉冲，尚未接材料电导、液体传播和连锁放电。
- 本证据不替代真实 Player 画面、输入及全区域陷阱矩阵。
