# DEMO-008 marker 植被视觉证据

## 来源

- 参考 Build：Noita `17130612`。
- 隔离数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`。
- `scripts/biomes/mountain/mountain_left_entrance.lua`：`eebbf161d805ca90646208bce970121c11fcb689551af5636afe2e1909e3a5e9`。
- `entities/props/mountain_left_entrance_grass.xml`：`ad62f80af73aaae13e00d9133e44ae9124eda4eb4ef04b749941437b81cf0327`。
- `biome_impl/mountain/left_entrance_grass.png`：`c2d7f79fcabe51ce0e6f3d1bc9b3f230ec8c0566234b3572ae3398eb20cc5f7a`。

正式仓库和 Player 只读取 Demo 自有 PNG 与纯 C# 目录，不读取参考目录或执行 Lua。

## 来源语义

- `spawn_big_bushes(x,y)` 调用 `spawn(g_big_bushes,x,y+12,0,0)`。
- `g_big_bushes` 由 7 个等权实体组成：5 个 `plant_bush_big` 与 2 个 `plant_bush`。
- 大灌木使用 `lush_bush_01..05`，anchor 为各自 XML 的 `(24|16,46)`；两个小灌木使用 `lush_bush_small_01..02`，anchor 为 `(16,30)`。
- `spawn_grass(x,y)` 创建 `mountain_left_entrance_grass.xml`；其 PixelSprite 为 `397x94`，anchor `(198,40)`。

## 实现与验证

- `NoitaMarkerVegetationCatalog` 将 marker 植被与 `VegetationComponent` 的 317 层目录分离，避免把两条来源链混为一种随机生态。
- 视口世界层直接按 Wang marker、world seed 和来源 anchor 组合真实 PNG，不经过通用绿色 overlay，也不产生逐帧集合分配。
- 大灌木从 7 个来源实体中确定性等权选择；入口草保持来源尺寸和锚点。
- 未实现的 vines、hanger、nest、fungitrap、root grower 等 marker 继续 fail-closed。
- 植被相关快速测试：7 passed / 0 failed。
- Demo Release build：0 warning / 0 error。

当前节点闭合静态视觉来源，不把来源 `SimplePhysicsComponent` 冒充为已实现。灌木/入口草的脱落、下坠、像素清理，以及其余动态植被种类仍需后续 C# 物理实体节点完成。
