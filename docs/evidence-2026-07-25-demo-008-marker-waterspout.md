# DEMO-008 waterspout Effect marker 证据

## 来源

- `scripts/biomes/mountain/mountain_hall.lua`：SHA256 `b3787dc364b2445137870c23862b0ec599cb7913027b1c2f4ddd401f777a90d8`。
- `entities/props/dripping_water.xml`：SHA256 `49522abbe7d128d384c48f1e6c0f5830eef62995e8ddfd2825674b1a6cf7fe6b`。
- `entities/base_dripping_liquid.xml`：SHA256 `fa5b2de77ac05fe3d3a842f55a2cf4ed41c865ca66bd8d9b531ee369d4a51f43`。
- 参考身份：Noita Build `17130612`。

## 已实现

- `spawn_waterspout` 生成 Demo 侧纯 C# `NoitaMarkerDrippingLiquid`，运行时不执行 Lua。
- 装饰滴落间隔按来源 `20..60` frame，真实 water 粒子间隔按 `70..100` frame。
- 真实粒子数量保留来源 `0..1`，位置、速度和 `0.6..1.3s` lifetime 使用 marker 坐标确定性序列。
- water 粒子通过公开 `IParticleSpawner` 进入实际材料模拟，不直接涂抹静态水块。
- 未实现的 Effect marker 在 profile 阶段 fail-closed，不再统一映射到 fire `SparkEmitter`。

## 验证

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaMarkerEffectTests|FullyQualifiedName~NoitaMarkerPortalTests|FullyQualifiedName~NoitaMarkerHazardTests|FullyQualifiedName~NoitaMarkerLootTests|FullyQualifiedName~NoitaMarkerEnemyTests"
```

结果：`39 passed / 0 failed`。覆盖 waterspout gameplay profile、确定性初态、四类未实现 Effect 抑制和相邻 marker 分支。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore
```

结果：`0 warning / 0 error`。

## 未完成

- 来源实体的 0.2 HP、受击/燃烧销毁和 water blood 行为尚未接入。
- 来源 sprite 与真实 Player 滴落画面尚未取得。
