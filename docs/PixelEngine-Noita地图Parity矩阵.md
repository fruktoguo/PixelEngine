# PixelEngine Noita 地图 Parity 矩阵

> 用途：`DEMO-008` 的参考事实、实现状态、允许差异与证据索引。Canonical 完成状态仍只以 `plan/tasks/` 为准。
> 参考核对日期：2026-07-22。除公开 Wiki 外，已核对用户本机 Steam Build `17130612` 的官方解包数据；版本、哈希、加载链和坐标基线见 [`reference-noita-build-17130612-map-topology.md`](reference-noita-build-17130612-map-topology.md)。正式截图 parity 仍需绑定同一目标提交的真实参考/Player capture。

## 1. 主流程拓扑

| 参考流程 | 参考事实 | 当前 Demo | 状态与差异 |
|---|---|---|---|
| Mountain / Surface | 从地表 Mountain 进入第一层，世界在主路径外仍可横向探索 | 确定性自然地表、安全出生区、无横向硬边界 | 已实现；地表固定地标与 parallel-world 入口待扩展 |
| Mines -> Coal Pits -> Snowy Depths -> Hiisi Base -> Underground Jungle -> The Vault -> Temple of the Art -> The Laboratory | 70×48 宏观 biome map 先做二维区域分类；主区高度依次为 1024/1024/1536/1024/1536/1536/2048，层间占完整 512-cell 宏格；Laboratory 是东南侧固定 2600×1600 拼接场景 | `campaign.json` v5 已恢复可变纵深与完整 512-cell 层间跨度；`biomes.json` v4 用 98 个运行段投影主区、三类侧区、Holy Mountain、lava 与固定 Laboratory，共覆盖参考色图中本阶段支持的 479 个宏格；生成器装配时编译为 70×48 O(1) 查询表 | **宏观主路径轮廓已实现，内部细节仍不完整**；剩余 2881 个宏格暂按 solid 基线处理，特殊地表/空域/秘密区仍需逐类建模，biome 内部 Wang/BitmapCaves 也尚非参考实现 |
| Portal Deeper | 每层底部一排 Portal 指向同一个 Holy Mountain 内部；下方 Teleportatium 眼池被抽空或污染会令 Portal 失效；进入后固定传送并短暂无敌 | 每层 3 个同目的地 Portal、横向入口大厅、真实 Teleportatium 池、逐 cell 供能检查、黑屏、材质粒子、0.04 s 无敌；Sandbox 不响应 | 已实现核心地图/交互语义；参考的 10% 随机危险液体池和 polymorph 日志未实现 |
| Holy Mountain | 每层入口落在完整 512-cell 宏格；altar/left/right 参考材质图各为 512×282，并由 temple wall 包围 | 七层已恢复 512-cell 纵深并只在色图标记的 61 个宏格生成 Holy Mountain；现有设施房仍为自有矩形 operation | **比例与二维位置已恢复，组合结构仍不匹配**；精确 altar/left/right 语义拼片及交互归后续节点 / `DEMO-011` |
| Laboratory | Final Holy Mountain 通向东南侧固定 boss arena；参考由 5×4 个 512-cell 分片拼成 2600×1600 场景 | 固定边界为 `X=1536..4135`、参考深度 `12288..13887`，生成独立外壳、左入口、上下桥、支撑、双 vat、平台、lava sea 与 Boss 地标；不再运行随机 Laboratory pixel-scene | **外形尺寸与终局身份已恢复，逐像素拼片尚未复现**；Sampo/Boss/结算归 `DEMO-012` |

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
| The Laboratory | 横向 brickwork bridge、lava sea、Boss 双平台与上方 lava vats | 固定 2600×1600 场景选择器；外壳、入口、断桥、支撑、双 vat、双平台、lava sea 与 Boss bridge 地标均为确定性实体地形，随机 encounter 被禁止 | 精确 5×4 材质拼片、Sampo、Boss、终局 Portal 与战斗交互 |

## 3. 确定性与性能

| 合同 | 当前证据 |
|---|---|
| `RunSeed + global cell/chunk coordinate` 决定初态 | 负坐标、跨加载顺序、跨 seed、共享 tile edge、pixel-scene 与固定 landmark anchor 测试 |
| 修改持久化优先 | 继续复用 `DEMO-006/007` region store 与 streaming initializer 合同 |
| 主路径可达 | 七个程序化主区的中心访问通道连续；Portal 行由供能入口桥接；Holy Mountain 右侧出口回接下一层；Temple 到东侧固定 Laboratory 有独立纵向回程通道；固定场景逐点材质测试 |
| 真实流送长路线 | reference seed 经正式 `WorldStreamer` 依次装载 12 个固定地标，逐站校验 authored operation 与 256-chunk cap；首站修改跨完整纵深驱逐后由 region store 恢复 |
| 稳态零托管分配 | 64 次八区 chunk、256 次程序化 encounter/biome landmark/Holy Mountain landmark query allocation gate；固定 Laboratory 不产生随机 encounter；既有 BDN 诊断为 0-6 B 噪声，二维拓扑节点仍需补跑新基准 |
| 代表场景成本 | 2026-07-22 Release InProcess ShortRun：Surface/Mines/Fungal/Portal-Holy Mountain/Laboratory 为 185.0-535.6 us/chunk |

## 4. 参考来源

- [本机 Build 17130612 地图生成参考基线](reference-noita-build-17130612-map-topology.md)
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

当前未关闭 `DEMO-008`。可变主区跨度、70×48/512-cell 二维查询、98 个语义运行段、完整高度 Holy Mountain 分类、横向主/侧区/lava/solid 边界和固定 2600×1600 Laboratory 已进入实现与回归测试；独立对照 Build `17130612` 色图时，本阶段支持的 479 个语义宏格为 `Missing=0 / Wrong=0`。这仍不是完整地图复刻：2881 个剩余宏格尚未细分地表、空域、秘密区与特殊场景，Holy Mountain 仍缺 altar/left/right 精确组合，各 biome 仍需扩充 Wang/BitmapCaves 与 pixel-scene structure pool，并取得同 seed 真实截图矩阵、性能和长路线证据。Boss、商店、谜题与 Portal 玩法交互仍分别归后续任务。
