# DEMO-008 encounter marker 敌人运行时证据

## 已实现

- `Scene.CollectComponents<T>(Span<T>)` 直接从同类型稠密 bucket 复制，容量截断且稳态零托管分配。
- `Encounter` marker 不再创建 `SparkEmitter`，而是生成 Demo 侧 `NoitaMarkerEnemy`。
- 来源函数按角色归并为 `large`、`robot`、`swarm`、`aquatic`、`standard` 五类压力语义，并分别配置生命、速度、碰撞半径与接触伤害。
- 敌人追踪玩家时使用公开 `ISolidSampler.SampleSolidAabb` 阻挡，不穿过权威固体地形。
- Wand projectile 在 solid raycast 命中点之前对本帧飞行线段执行敌人命中，施加 spell damage，并保持 hit/death trigger payload 与 bounce 行为。
- 击杀 marker 进入当前 run 固定容量 resolved 集，视口流送后不会重复 materialize。

## 验证

```pwsh
dotnet test tests/PixelEngine.Scripting.Tests/PixelEngine.Scripting.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ScriptDispatchAllocationTests"
```

结果：`5 passed / 0 failed`。覆盖稠密 bucket 突变、容量截断和 1024 次稳态收集零分配。

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaMarkerEnemyTests|FullyQualifiedName~CampaignWorldTests.NoitaWangMarkerProfilesMapReferenceFunctionsToGameplayEntities"
```

结果：`13 passed / 0 failed`。覆盖 encounter profile、五种原型和法术线段击杀。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore
```

结果：`0 warning / 0 error`。

## 未完成

- 当前是角色压力语义闭环，不是 Noita 全部敌人逐实体复刻。
- 尚缺来源 sprite、动画、远程攻击、材料沾染/燃烧、掉落表和真实 Player 战斗截图。
- Loot、商店、机关与动态物理 prop 仍需从通用 marker profile 继续拆分。
