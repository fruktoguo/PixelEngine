# PixelEngine Noita 地图 Parity 矩阵

> 用途：`DEMO-008` 的参考事实、实现状态、允许差异与证据索引。Canonical 完成状态仍只以 `plan/tasks/` 为准。
> 参考核对日期：2026-07-22。参考资料是 Noita Wiki 的公开地图说明；正式截图 parity 仍需绑定同一目标提交的真实参考/Player capture。

## 1. 主流程拓扑

| 参考流程 | 参考事实 | 当前 Demo | 状态与差异 |
|---|---|---|---|
| Mountain / Surface | 从地表 Mountain 进入第一层，世界在主路径外仍可横向探索 | 确定性自然地表、安全出生区、无横向硬边界 | 已实现；地表固定地标与 parallel-world 入口待扩展 |
| Mines -> Coal Pits -> Snowy Depths -> Hiisi Base -> Underground Jungle -> The Vault -> Temple of the Art -> The Laboratory | 八个主路径 biome 固定纵深顺序，前七层经 Portal/Holy Mountain 过层 | `campaign.json` v4 固定身份/纵深；`biomes.json` v3 独占地形权威并声明 12 个固定地标 | 已实现拓扑、初态生成和代表性固定地标；长路线真实截图待验收 |
| Portal Deeper | 每层底部一排 Portal 指向同一个 Holy Mountain 内部；下方 Teleportatium 眼池被抽空或污染会令 Portal 失效；进入后固定传送并短暂无敌 | 每层 3 个同目的地 Portal、横向入口大厅、真实 Teleportatium 池、逐 cell 供能检查、黑屏、材质粒子、0.04 s 无敌；Sandbox 不响应 | 已实现核心地图/交互语义；参考的 10% 随机危险液体池和 polymorph 日志未实现 |
| Holy Mountain | Brickwork 房间依次容纳入口、恢复/刷新、商店、水池、Worm Crystal、Perk、训练雕像与右侧坍塌出口 | authored layout 提供 arrival、双水池、shop/perk 平台、Worm Crystal 房、训练雕像、右侧竖井和回接下一层的底部通道 | 地图已实现；恢复、商店、Perk、坍塌、诅咒与 angry gods 属于 `DEMO-011` |
| Laboratory | 最终 Holy Mountain 通向横向桥与 lava sea，Sampo 触发 Kolmisilma | Laboratory grammar、随机 bridge/lava scene 与固定 Kolmisilma bridge 双平台/lava 布局已生成 | 地图轮廓已实现；Sampo/Boss/结算属于 `DEMO-012` |

## 2. Biome 生成矩阵

| Biome | 参考地图身份 | 当前地形语法与 authored scene | 尚缺内容 |
|---|---|---|---|
| Mines | 紧凑土/岩/木矿道，水、油、毒液池与早期结构 | `mine-shafts`、木支撑矿井房；固定 Lava Lake overlook 具有 stone 洞、lava 池和木台 | Chasm 与完整结构池 |
| Coal Pits | 更开阔曲折的煤层、木结构、可燃危险；西侧 Fungal Caverns | `open-coal`、木支撑开阔房；西侧 active Fungal Caverns 与固定 fungal threshold/acid 门槛 | 煤专属材质、完整 puzzle/structure catalog |
| Snowy Depths | 大块雪/冻岩与脆冰连接的开阔空间；西侧 Magical Temple，东北 Chasm 返回 Mines | `open-snow`、Hiisi outpost；西侧秘密 Magical Temple、东侧 Mines shortcut 与固定 Buried Eye 地形 | Eye 交互、完整金属结构和爆炸冰湖 |
| Hiisi Base | 冰岩内的钢制走廊、门廊、液池和机械危险 | `industrial-grid`、钢制走廊 scene；固定 spell-shop 房与 Anvil 房 | Shop/Anvil 交互、Bar、Sauna、Hourglass |
| Underground Jungle | 高生物量洞穴与更多 dropshaft；东侧 Dragoncave，西侧 Lukki Lair | `jungle-dropshafts`、root room；固定 Dragoncave 与 Munkki statues，西侧跨 Jungle/Vault 的 Lukki Lair | Dragon/Munkki 交互与内部 fungal split |
| The Vault | 废弃工厂、金属/机械、毒液与玻璃酸池；西北接 Lukki Lair | `vault-factory`、玻璃酸 vat；固定 Fungal Altar 与 Brain vat，Lukki Lair 下段连接 | Altar/Brain 交互与更多机械结构 |
| Temple of the Art | 双层 brickwork 迷宫、沙/木阻塞、陷阱；东侧 Magical Temple，西侧 Tower，东南回 Laboratory | `temple-labyrinth`、split chamber；固定 Gate Guardian room 与 Tower portal room，东侧 Magical Temple 和 Laboratory return | Guardian/Tower 交互与完整 trap catalog |
| The Laboratory | 横向 brickwork bridge、lava sea、Boss 双平台与上方 lava vats | `laboratory-arena`、随机 bridge scene；固定 boss bridge、双平台、lava 与晶体锚点 | Sampo、Boss、终局 Portal 与战斗交互 |

## 3. 确定性与性能

| 合同 | 当前证据 |
|---|---|
| `RunSeed + global cell/chunk coordinate` 决定初态 | 负坐标、跨加载顺序、跨 seed、共享 tile edge、pixel-scene 与固定 landmark anchor 测试 |
| 修改持久化优先 | 继续复用 `DEMO-006/007` region store 与 streaming initializer 合同 |
| 主路径可达 | 普通纵深连续；Portal 行由供能入口桥接；Holy Mountain 右侧出口回接下一层；逐点地形测试 |
| 真实流送长路线 | reference seed 经正式 `WorldStreamer` 依次装载 12 个固定地标，逐站校验 authored operation 与 256-chunk cap；首站修改跨完整纵深驱逐后由 region store 恢复 |
| 稳态零托管分配 | 64 次八区 chunk、256 次 encounter/biome landmark/Holy Mountain landmark query allocation gate；BDN 诊断为 0-6 B 噪声 |
| 代表场景成本 | 2026-07-22 Release InProcess ShortRun：Surface/Mines/Fungal/Portal-Holy Mountain/Laboratory 为 185.0-535.6 us/chunk |

## 4. 参考来源

- [Holy Mountain](https://noita.wiki.gg/wiki/Holy_Mountain)
- [Portal](https://noita.wiki.gg/wiki/Portal)
- [Mines](https://noita.wiki.gg/wiki/Mines)
- [Coal Pits](https://noita.wiki.gg/wiki/Coal_Pits)
- [Snowy Depths](https://noita.wiki.gg/wiki/Snowy_Depths)
- [Hiisi Base](https://noita.wiki.gg/wiki/Hiisi_Base)
- [Underground Jungle](https://noita.wiki.gg/wiki/Underground_Jungle)
- [The Vault](https://noita.wiki.gg/wiki/The_Vault)
- [Temple of the Art](https://noita.wiki.gg/wiki/Temple_of_the_Art)
- [The Laboratory](https://noita.wiki.gg/wiki/The_Laboratory)

## 5. DEMO-008 剩余验收

当前未关闭 `DEMO-008`。12 个代表性固定地标已经数据化并经正式 WorldStreamer reference-seed 长路线、resident budget 与修改持久化验证，但其中的 Boss、商店、谜题与 Portal 交互仍分别归后续玩法任务。剩余验收是扩充 structure pool，取得八区/侧区/Portal/Holy Mountain 的同源参考与真实 PixelEngine Player 截图，再更新 evidence index、最终输出与 canonical 状态。
