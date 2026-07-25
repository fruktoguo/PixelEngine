# DEMO-008 forcefield generator marker 证据

## 来源

- 参考 Build：Noita `17130612`。
- 隔离数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`。
- `entities/props/forcefield_generator.xml`：`d0ae634c900ce2bb27dfe7db43a5fb1a46c838bfd5ce3cf9f06d6f2bfdabeead`。
- `scripts/props/forcefield_generator.lua`：`e12522cd46595d498042edcb1befda92040c3845201f7fa25e0da8b3d60b9d6d`。
- `scripts/biomes/snowcastle.lua`：`e430ca0bb48e37b96ead0dba9b2af3039d8d0fbe8c497d871204d761d094b8e7`。

正式仓库和 Player 不读取上述文件或执行 Lua；运行时行为全部在 Demo 侧 C# 中实现。

## 来源语义

- `g_forcefield_generator` 的空项权重为 `1.0`、实体权重为 `0.5`，因此 marker 以 `2/3` 概率为空、`1/3` 概率生成装置。
- `safe(x,y)` 排除 Snowcastle 入口矩形 `x=125..249 / y=5118..5259`，并排除 `y>6100` 的 portal 邻域。
- 实体生成位置为 marker `y-2`；本体为静态 steel、30 HP，可被 fire/lava/acid 破坏，死亡触发半径 16 的电爆炸。
- shield 中心相对本体为 `(8,-8)`，半径 `20`、sector `320`、初始能量 `2`、最大能量 `3`、回充速度 `0.25/s`，光半径 `60`。
- 每 8 帧探测最近 enemy/player；距离小于等于 `38` 时关闭 shield，远离后恢复并回充。320 度 shield 在朝下方向保留 40 度缺口。

## 实现与验证

- `NoitaMarkerForcefieldGenerator` 负责确定性权重、safe 区、30 HP 本体、近距开关、能量盾、320 度碰撞/绘制与死亡爆炸。
- Wand projectile 先以固定容量数组检查 forcefield，再检查敌人、ghost crystal 和 solid；盾面命中只消耗能量，缺口或关闭状态才允许命中本体。
- 空权重结果不发射通用 materialize burst，并写入本轮 resolved 集。
- `NoitaMarker*` 快速测试：48 passed / 0 failed，覆盖专用 profile、确定性权重、safe 区、盾能量、本体伤害和 40 度缺口。
- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warning / 0 error。

本节点仍未接入来源 generator sprite、plasma_fading 粒子、electricity damage immunity 与 thunderball 死亡载荷，不宣称装置视觉和全部伤害类型 parity 完成。
