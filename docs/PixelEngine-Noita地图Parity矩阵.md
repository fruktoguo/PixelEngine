# PixelEngine Noita 地图 Parity 矩阵

> 用途：`DEMO-008` 的参考事实、实现状态、允许差异与证据索引。Canonical 完成状态仍只以 `plan/tasks/` 为准。
> 参考核对日期：2026-07-22。除公开 Wiki 外，已核对用户本机 Steam Build `17130612` 的官方解包数据；版本、哈希、加载链和坐标基线见 [`reference-noita-build-17130612-map-topology.md`](reference-noita-build-17130612-map-topology.md)。正式截图 parity 仍需绑定同一目标提交的真实参考/Player capture。

## 1. 主流程拓扑

| 参考流程 | 参考事实 | 当前 Demo | 状态与差异 |
|---|---|---|---|
| Mountain / Surface | 从固定出生山体进入第一层，世界在主路径外仍可横向探索 | 出生坐标恢复为参考 `(227,-85)`；left entrance、hall、right、top 与 floating-island 以来源 hash 锁定的 512×512 四分类语义掩码生成，不再使用平地加直墙 | **固定出生山体轮廓已实现**；树木、背景、教学 marker、其它地表固定场景与 parallel-world 入口待扩展 |
| Mines -> Coal Pits -> Snowy Depths -> Hiisi Base -> Underground Jungle -> The Vault -> Temple of the Art -> The Laboratory | 70×48 宏观 biome map 先做二维区域分类；主区高度依次为 1024/1024/1536/1024/1536/1536/2048，层间占完整 512-cell 宏格；Laboratory 是东南侧固定 2600×1600 拼接场景 | `campaign.json` v5 保持参考纵深；`biomes.json` v6 记录 129 种颜色和全部 3360 个宏格；15 组来源 hash 锁定的 STB Wang 模板按 20 个 main/side reference biome 绑定运行 | **宏图与 Wang 模板库已实现，biome 生成仍未闭合**；BitmapCaves、完整 pixel-scene/marker、背景、实体及 Noita RNG 序列仍需逐类复刻 |
| Portal Deeper | 每层底部一排 Portal 指向同一个 Holy Mountain 内部；下方 Teleportatium 眼池被抽空或污染会令 Portal 失效；进入后固定传送并短暂无敌 | 每层 3 个同目的地 Portal、横向入口大厅、真实 Teleportatium 池、逐 cell 供能检查、黑屏、材质粒子、0.04 s 无敌；Sandbox 不响应 | 已实现核心地图/交互语义；参考的 10% 随机危险液体池和 polymorph 日志未实现 |
| Holy Mountain | 每层入口落在完整 512-cell 宏格；altar/left/right 参考材质图各为 512×282，并由 temple wall 包围 | 色图中的 temple wall / altar-left / altar / altar-right 位置逐格保留；四个 altar 变体按 `y+260` 语义归一为 512×512 掩码，原有 Portal 供能池与设施 operation 继续作为玩法覆盖层 | **大尺度组合轮廓已实现，覆盖层仍有差异**；背景、spawn marker、完整设施实体和保护区交互归后续节点 / `DEMO-011` |
| Laboratory | Final Holy Mountain 通向东南侧固定 boss arena；参考由 5×4 个 512-cell 分片拼成 2600×1600 场景 | 固定边界为 `X=1536..4135`、参考深度 `12288..13887`；`boss_arena.png` 的 416 万像素被压成 4-bit/8 类 Demo 材质掩码，保留左右结构、桥、洞室、水、lava sea 与下方空腔，不再使用手写矩形替代 | **固定地形轮廓已实现**；视觉背景、spawn marker、Sampo、Boss、终局 Portal 与战斗交互归 `DEMO-012` |

## 2. Biome 生成矩阵

| Biome | 参考地图身份 | 当前地形语法与 authored scene | 尚缺内容 |
|---|---|---|---|
| Mines | 紧凑土/岩/木矿道，水、油、毒液池与早期结构 | `coalmine` / `coalmine-alt` 原始 Wang 轮廓和材质语义；固定 Lava Lake overlook 继续覆盖 | BitmapCaves dangerroom、Chasm 与完整结构池 |
| Coal Pits | 更开阔曲折的煤层、木结构、可燃危险；西侧 Fungal Caverns | 主区 `excavationsite` Wang；西侧 `fungicave`，其它 fungal 区使用 `fungiforest`；固定 fungal threshold 保留 | BitmapCaves、完整 puzzle/structure catalog 与实体 marker |
| Snowy Depths | 大块雪/冻岩与脆冰连接的开阔空间；西侧 Magical Temple，东北 Chasm 返回 Mines | 主区/秘密房使用 `snowcave` Wang；西侧 `wandcave`；东侧 shortcut 与固定 Buried Eye 保留 | BitmapCaves、Eye 交互、完整金属结构和爆炸冰湖 |
| Hiisi Base | 冰岩内的钢制走廊、门廊、液池和机械危险 | `snowcastle` Wang 轮廓；固定 spell-shop 房与 Anvil 房继续覆盖 | BitmapCaves、Shop/Anvil、Bar、Sauna、Hourglass 与 marker 实体 |
| Underground Jungle | 高生物量洞穴与更多 dropshaft；东侧 Dragoncave，西侧 Lukki Lair | `rainforest` / `rainforest-open` / `rainforest-dark` Wang；固定 Dragoncave、Munkki statues 与 Lukki Lair 保留 | BitmapCaves、Dragon/Munkki 交互与内部 fungal split |
| The Vault | 废弃工厂、金属/机械、毒液与玻璃酸池；西北接 Lukki Lair | `vault` / `vault-frozen` Wang；固定 Fungal Altar、Brain vat 与 Lukki Lair 下段保留 | BitmapCaves、Altar/Brain 交互与更多机械结构 |
| Temple of the Art | 双层 brickwork 迷宫、沙/木阻塞、陷阱；东侧 Magical Temple，西侧 Tower，东南回 Laboratory | 主区 `crypt`，侧区 `wizardcave` / `wandcave` Wang；固定 Gate/Tower room 和 return 保留 | BitmapCaves、Guardian/Tower 交互与完整 trap/marker catalog |
| The Laboratory | 横向 brickwork bridge、lava sea、Boss 双平台与上方 lava vats | 固定 2600×1600 参考掩码；rock/templebrick/lava/water/mud/glowstone/box2d rock 映射到 Demo 八类材质，随机 encounter 被禁止 | 视觉层、spawn marker、Sampo、Boss、终局 Portal 与战斗交互 |

## 3. 确定性与性能

| 合同 | 当前证据 |
|---|---|
| `RunSeed + global cell/chunk coordinate` 决定初态 | 15 组 Wang 的负坐标、反向访问、跨 seed、共享 constraint、65,536 次热采样，以及 coalmine/fungicave 实际 chunk 掩码相关性测试 |
| 修改持久化优先 | 继续复用 `DEMO-006/007` region store 与 streaming initializer 合同 |
| 主路径可达 | 旧人工贯穿直井已删除；Portal 供能入口、Holy Mountain 设施覆盖和固定 Laboratory 入口保留自动化测试。Wang 已接入，但 BitmapCaves 组合后的完整无软锁长路线仍待重验，当前不冒充已闭合 |
| 真实流送长路线 | reference seed 经正式 `WorldStreamer` 依次装载 12 个固定地标，逐站校验 authored operation 与 256-chunk cap；首站修改跨完整纵深驱逐后由 region store 恢复 |
| 稳态零托管分配 | 64 次八区 chunk、256 次 encounter/landmark query 和 65,536 次 Wang 热采样 allocation gate；clean BDN 诊断为 0-7 B 舍入噪声 |
| 代表场景成本 | `d2abcfd6` detached clean Release InProcess ShortRun：SurfaceWest 73.42、spawn 224.31、SurfaceEast 657.33、Mines 308.21、Fungal 369.04、Portal/Holy Mountain 198.18、fixed Laboratory 103.06 us/chunk；MemoryDiagnoser 0-7 B |

## 4. 参考来源

- [本机 Build 17130612 地图生成参考基线](reference-noita-build-17130612-map-topology.md)
- [Build 17130612 Wang 地形检查点](evidence-2026-07-23-demo-008-noita-wang-terrain.md)
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

当前未关闭 `DEMO-008`。可变主区跨度、70×48/512-cell 全量二维查询、129 种实际颜色、3360 个宏格、固定出生山体、Holy Mountain 组合掩码、2600×1600 Laboratory，以及 15 组 Wang/20 个 reference biome 绑定已进入实现与回归测试；独立对照 Build `17130612` 色图为 `3360/3360`、`Mismatches=0`。这仍不是完整地图复刻：BitmapCaves、随机结构池、背景层、spawn marker 实体处理、未接入的 fixed/random pixel scene、完整材料/生态、Noita 原始 RNG 序列，以及同 seed 全区域截图矩阵和长路线证据仍待闭合。Boss、商店、谜题与 Portal 玩法交互仍分别归后续任务。
