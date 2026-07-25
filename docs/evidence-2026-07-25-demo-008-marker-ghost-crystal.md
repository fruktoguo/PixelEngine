# DEMO-008 ghost crystal marker 证据

## 来源

- 参考 Build：Noita `17130612`。
- 隔离数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`。
- `entities/buildings/ghost_crystal.xml`：`6668086051ff472b4063a6a3474c24c6f233dc591a5a775dd4e992b5ebd29f5a`。
- `scripts/buildings/ghost_crystal.lua`：`9dbc088a21e999b9277b5daf08744965fef9346a60067d8cc2bc6eabdcd05fab`。
- `scripts/buildings/ghost_crystal_death.lua`：`1160518941c8884418ce6f3591eab4547ef822c6547848af1b8f320c31b3c4a9`。
- `entities/projectiles/ice.xml`：`a3343c922d4cf0b6b46d8077a819f69a3c14d8cb271c10411b0e174366151f23`。
- `scripts/biomes/crypt.lua`：`1f601e8a98801a877897fbed5ce24b9a0f887fb06f460048e2e1e786eb3eea17`。

正式仓库和 Player 不读取这些文件，也不执行 Lua；以上文件只用于离线核对，行为已写成 Demo 侧 C#。

## 来源语义

- `g_ghost_crystal` 的空项权重为 `0.5`，实体组权重为 `1.0`。
- 实体组先生成 `1..3` 个 `ghost.xml`，再生成一座 crystal；crystal 的 added 脚本再生成并绑定一个 ghost，因此 populated 结果为 `2..4` 个幽灵。
- crystal 为 `20 HP`，hitbox 为 `x=-6..6 / y=-20..0`，紫色光半径 `96`。
- crystal 死亡时按 12 等分角、速度 `100` 的来源调用发射 `ice.xml`；ice 实体自身速度固定为 `50`、重力 `10`、空气阻力 `0.05`、寿命 `80` 帧、爆炸半径 `6`。C# 运行时按最终 projectile 速度实现 12 向冰弹，并保留重力、阻力、寿命、半径和玩家/地形伤害。
- crystal 被销毁时，其绑定幽灵组同时销毁；marker 写入 resolved 集，离开并返回视口不会重生。
- 权重选中空项时不创建可见内容、不发射 materialize burst，并直接记为本轮已解析，避免空 marker 占用活动槽或反复闪烁。

## 实现与验证

- `NoitaMarkerGhostCrystal` 负责确定性权重选择、2..4 幽灵组、20 HP、法术线段命中、96px 光源和死亡状态。
- `NoitaMarkerIceShard` 负责 12 向冰弹移动、solid raycast、玩家伤害、地形伤害与 80 帧寿命。
- `WandProjectile` 通过固定容量数组收集 crystal 目标，不在逐帧命中路径分配。
- 快速测试：`NoitaMarkerGhostCrystalTests`、`NoitaMarkerEnemyTests`、`NoitaMarkerEffectTests`，16 passed / 0 failed。
- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warning / 0 error。

本节点只证明行为接线；来源 sprite、冻结状态效果、逐 ghost AI 与真实 Player framebuffer 仍未闭合，不能宣称视觉 parity 完成。
