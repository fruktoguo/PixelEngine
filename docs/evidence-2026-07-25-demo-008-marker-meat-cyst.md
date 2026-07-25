# DEMO-008 meat cyst marker 证据

## 来源

- 参考 Build：Noita `17130612`。
- 隔离数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`。
- `entities/props/meat_cyst.xml`：`6fdfda4ab23ff1a72db144619587012d17e01a61d518584a837dd8d36b66cd89`。
- `scripts/props/cyst_init.lua`：`70ca616f2e9ebf67484cb45e7e3f906e78fcf1d8ee11dbf7001938bb9818613c`。
- `entities/projectiles/pusblob.xml`：`39dda0d6ea062912d4dc5b70018957a79c7c96821521bba136b7d1515a65e8b7`。
- `scripts/biomes/meat.lua`：`3fb43239c65a434085ab3f3a6663da1e06f649e4c53c987c26aaa72d7af8db0f`。

正式仓库和 Player 不读取这些文件或执行 Lua；运行时行为全部为 Demo 侧 C#。

## 来源语义

- `spawn_cyst` 在 `ProceduralRandom(x,y) < 0.3` 时为空，否则在 `(x+5,y+5)` 生成并按坐标随机旋转一周。
- cyst hitbox 为 `x=-6..6 / y=-10..4`，1 HP，orange light 半径 100；acid/lava 与 fire 可造成持续伤害。
- 死亡必定触发半径 22、伤害 1.3 的 slime explosion，并通过 `load_this_entity` 生成一个 `pusblob.xml`。
- pusblob 速度为 `90..115`、重力 `10`、无空气阻力、寿命 `130` 帧；命中或寿命结束时以半径 14 爆炸并生成 pus。

## 实现与验证

- `NoitaMarkerMeatCyst` 负责确定性 30% 空权重、`(+5,+5)` 偏移、随机朝向、1 HP/hitbox、100px light、死亡爆裂与 pusblob 创建。
- `NoitaMarkerPusBlob` 负责 90..115 初速、重力、solid raycast、玩家伤害、130 帧寿命、14px 地形伤害与 pus 写入。
- Wand projectile 通过固定容量数组命中 cyst；死亡和空权重结果进入 resolved 集，流送后不重复生成。
- `NoitaMarker*` 快速测试：51 passed / 0 failed，覆盖 profile、确定性、偏移、hitbox 与死亡状态。
- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warning / 0 error。

当前未把 fire/acid/lava 持续材质伤害和 20% 非致死受击爆裂接入统一 entity damage-type tick；来源 cyst sprite、pusblob trail/audio 也未接入，因此本节点不宣称全部视觉与伤害类型 parity 完成。
