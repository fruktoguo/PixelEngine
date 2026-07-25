# DEMO-008 fungus marker 敌人证据

## 来源

- 参考 Build：Noita `17130612`。
- `scripts/biomes/coalmine.lua`：`d0a01489a17798cd638b6d1a2636962e0a528f1d86fa3f2c255cb59306d67269`。
- `scripts/biomes/coalmine_alt.lua`：`b8775ac16adba2fd5f5ec8bd686c59a4b85d47ed80237edcf832ea7219e2a3a1`。
- `entities/animals/fungus.xml`：`55b37752b27066134667e921f097065d9e39764f83ef519aea68a5f3d9a40903`。
- `entities/animals/fungus_big.xml`：`dfbb89f1b48e134260d96cb034819eac34e32bee074cc06fe3fb6eb682d93d6f`。
- `entities/projectiles/fungus_explosion.xml`：`62ff5df40cd9540d359c2eb69d3f699c51241efe1487e72e39f7b3fa04bdee00`。
- `entities/projectiles/fungus_big_explosion.xml`：`887bd40824b7e66bafd87e1c495db2fc95ec0dc6f9ca0f00db9bd39a6453301c`。

正式 Player 不读取上述文件或执行 Lua。`tools/extract-noita-marker-enemy-sprites.ps1` 只在开发时从隔离参考树确定性切帧，产物和逐帧 SHA256 固化在 Demo 内容目录。

## 来源语义

- 普通 Coal Mine 的 `g_fungi` 权重为 `0.5 empty / 0.5 fungus / 0.05 fungus_big`；Coal Mine Alt 为 `0.5 empty / 0.5 fungus`。
- 小菌 2.6 HP、run velocity 9、hitbox `x=-6..6/y=-10..4`，stand 为 12 帧 `16x16 / 0.12s`。
- 大菌 7.6 HP、run velocity 14、hitbox `x=-6..6/y=-16..4`，stand 为 8 帧 `34x34 / 0.18s`。
- 两者持续产生真实 `acid_gas`；接触 mortal 后分别等待 20/40 帧触发爆裂。
- 死亡载荷使用 50/60px 爆炸半径，生成 `fungi` 与 pink plasma 粒子。

## 实现与验证

- `spawn_fungi` 从 Vegetation 总抑制中拆出，进入 `NoitaMarkerEnemy` 的 fungus 专属分支。
- 按 reference biome、world seed 和 marker 坐标确定性选择空/小/大结果；空结果进入 resolved 集，流送后不重复抽取。
- 运行时通过 `IScriptContext.WorldSprites` 播放真实 stand 帧，保留来源 sprite offset；不再绘制通用方块身体。
- 小/大菌使用 65/190 显示 HP，对应来源 2.6/7.6 的 25 倍可观察生命值；持续排放 `acid_gas`，近身 fuse 和死亡均触发来源尺度菌爆。
- `NoitaMarkerEnemyTests`：10 passed / 0 failed，覆盖 profile、空/小/大权重、alt 无大菌、生命值、命中与 20 帧资产 SHA256。

当前专属分支仍复用引擎现有直线追踪和 solid AABB 阻挡，不宣称 Noita pathfinding grid、dash lob、火焰 damage multiplier、掉落箱和 ragdoll 已完全复刻；这些差异继续留在 `DEMO-008`。
