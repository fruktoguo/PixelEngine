# PixelEngine Noita 高保真复刻 Demo 设计

> 状态：2026-07-22 产品转向定稿。文件名为历史兼容；本文是 Showcase Demo 从原创 Noita-like 战役调整为 Noita 高保真复刻后的权威产品设计。技术不变式仍以 `PixelEngine-架构与需求设计.md` 为准，执行状态只以 `plan/tasks/` 为准。
>
> 分层边界：`src/PixelEngine.*` 始终是通用、无玩法的引擎；Noita 专属名称、流程、地图、Wand/Spell、敌人、Perk、UI 和平衡数据只能存在于 `demo/PixelEngine.Demo`，并且只能经引擎公开 API 驱动。

## 1. 复刻目标与事实来源

本 Demo 的目标是尽可能完整、忠实地复现《Noita》公开可观察的游戏体验，而不再只抽象借鉴其结构。复刻对象包括：

- Mountain 地表起步、向下探索、八个主路径 biome、七个 Holy Mountain、The Laboratory、Kolmisilma、Sampo、死亡与下一轮；
- Wand/Spell 的牌组求值语义、法杖属性、库存编辑、Spell refresh、商店和 Perk；
- 敌人角色、金币、掉落、药水、stain/status、材料识别、地形破坏和环境反应；
- HUD、快捷栏、库存、Wand 编辑、拾取提示、商店、Perk、暂停和死亡结算的信息层级、布局密度与操作节奏；
- 主路径、侧区、秘密连接、地表区域、平行世界与后续 New Game+ 的世界拓扑语义。

参考来源按可信度使用：官方页面和公开演示定义产品意图与视觉基线；社区 Wiki 用于核对可观察流程、目录、数值和边界；最终行为必须以实际参考游戏画面与输入复核，不靠记忆补细节。

- Noita 官方页面：<https://noitagame.com/>
- 基础流程：<https://noita.wiki.gg/wiki/Guide:_How_to_Play>
- Biomes：<https://noita.wiki.gg/wiki/Biomes>
- Holy Mountain：<https://noita.wiki.gg/wiki/Holy_Mountain>
- Wands / Wand Mechanics：<https://noita.wiki.gg/wiki/Wands>、<https://noita.wiki.gg/wiki/Guide:_Wand_Mechanics>
- Spells / Perks / Enemies / Materials：<https://noita.wiki.gg/wiki/Spells>、<https://noita.wiki.gg/wiki/Perks>、<https://noita.wiki.gg/wiki/Enemies>、<https://noita.wiki.gg/wiki/Materials>

复刻完成度由版本化 parity matrix 管理。每一项必须记录 reference、PixelEngine 实现、自动化断言、真实 Player 证据和差异说明；“看起来差不多”或只有目录项不算完成。

正式仓库和玩家包不得依赖本机 Noita 安装、运行时读取其进程或构建时抽取外部二进制资源。进入仓库的资产必须可独立构建，并在 provenance 清单中记录来源、生成方式或用户提供记录。

## 2. 核心游戏流程

默认流程固定为：

```text
Main Menu
  -> Mountain / 初始 Wand 与 Flask
  -> Biome 探索、战斗、金币、Wand/Spell/物品搜集
  -> Portal
  -> Holy Mountain：恢复、Spell refresh、购买、Wand 编辑、Perk
  -> 更深 Biome
  -> The Laboratory / Sampo / Kolmisilma
  -> 胜利、死亡或后续世界路线
  -> Run Summary
  -> 新 seed 开始下一轮
```

游戏性来自三个同时闭合的循环：

1. **秒级循环**：移动、有限悬浮、瞄准、施法、换 Wand/物品、喷洒/投掷 Flask、踢击和利用材料。
2. **区域循环**：探索未知空间，在生命不可随意恢复的压力下对抗敌人、搜集金币和构筑资源，并决定继续冒险还是进入 Portal。
3. **整局循环**：Holy Mountain 把恢复、消费、构筑和 Perk 选择集中成阶段性决策；永久死亡清空本轮资源，玩家主要以知识和解锁推进。

`InfiniteSandbox` 继续作为独立引擎展示模式存在，不改变 Campaign 的永久死亡和结算语义。

## 3. 世界与地图拓扑

主路径使用 Noita 的八个 biome 身份与顺序：

| 顺序 | Biome | 主要玩法压力 |
|---|---|---|
| 1 | Mines | 基础材料教学、火、爆炸物、早期敌人和首次 Wand 选择 |
| 2 | Coal Pits | 可燃煤层、密集洞穴、Fungal Caverns 侧区与更高构筑收益 |
| 3 | Snowy Depths | 开阔垂直空间、远程敌人、冰雪材料与返回上层的路线关系 |
| 4 | Hiisi Base | 人工结构、门廊、密集远程火力、爆炸物与机械危险 |
| 5 | Underground Jungle | 高生物量、毒与酸、近距离包围、坚韧敌人与高阶 Wands |
| 6 | The Vault | 高能材料、机械结构、危险反应和高密度混合敌群 |
| 7 | Temple of the Art | 坚硬结构、黑暗、传送与高阶法术威胁 |
| 8 | The Laboratory | Sampo、Kolmisilma、终局战斗和结局入口 |

区域 1 到 7 的 Portal 后进入 Holy Mountain。Holy Mountain 包含完整恢复、Spell refresher、Spell/Wand 商店、Wand 编辑、Perk altar、reroll、训练雕像、水池、出口坍塌及破坏保护区的后果。它不是普通菜单房间，所有设施必须作为真实世界对象和权威 run state 交互。

世界生成采用 PixelEngine 的 chunk/seed 基础设施复现参考游戏的 Wang-tile + authored pixel-scene 观感：主路径始终可达，局部结构、地标、商店、宝箱、Portal、危险材料和遭遇点由 seed 决定；同 seed 初态与加载顺序无关。地表、侧区、秘密连接、捷径与 parallel worlds 按 parity 阶段逐步纳入，不得用一条纯垂直噪声走廊冒充地图复刻。

原 `Shattered Lode` 等八个原创区域名只作为旧存档/测试迁移 alias 保留，不再作为默认玩家可见内容。

## 4. Run 状态与生命周期

Campaign 状态机固定为：

```text
MainMenu
  -> StartingRun
  -> Exploring <-> HolyMountain
  -> Laboratory
  -> Completed -> RunSummary -> StartingRun
  -> Dead      -> RunSummary -> StartingRun
```

- `RunSeed` 决定初始世界、商店、掉落、敌人、Perk 候选和 shuffle；相同 seed 的初始内容可复现。
- 实时 CA 与多线程 physics 仍允许非 bit 级确定，不宣称完整 replay determinism。
- run state 记录 seed、biome、深度、生命、金币、四个 Wand、四个 item、Spell、Perk、Orb/quest 状态、Boss/结局状态和统计。
- Campaign 死亡必须以新 seed 原子替换 world/script/entity/UI/physics/particle/audio/event 生命周期；不得只传送玩家继续旧世界。
- Sandbox 死亡沿用安全出生区重生，不进入 Campaign 结算。

## 5. Wand / Spell 系统

Wand 是有序或 shuffle 的 Spell 容器，完整属性至少包括：Shuffle、Spells/Cast、Cast Delay、Recharge Time、Mana Max、Mana Charge Speed、Capacity、Spread、Always Cast 和 projectile speed multiplier。

求值器必须复现以下语义：

- non-shuffle 从左到右抽取，deck 耗尽后 recharge；shuffle 按 run RNG 产生可复现抽取顺序；
- projectile modifier 改变当前 cast state 并继续抽取目标 Spell；multicast 增加当前 cast 的 draw；
- trigger、timer 和 death trigger 建立有界 payload 子序列；Spell 可修改 mana、cast delay、recharge、spread、damage、speed、lifetime 和材料效果；
- limited-use、passive、utility、material、summon、teleport 和特殊 draw 行为进入同一版本化 catalog；
- evaluator 对 draw 次数、递归深度、payload、projectile 和 world effect 设硬预算，fail-closed；稳态 cast 零托管分配，使用预分配 command buffer；
- 所有世界写入只经公开延迟命令 API 在安全相位执行。

库存支持四个 Wand 与四个 item，具备拾取、交换、拖放、丢弃、Spell 移动、Wand 编辑限制、Holy Mountain/Perk 例外和存取。UI 必须让玩家看到 Spell 顺序、Wand stats、mana/cast/recharge 实时状态和每个 Spell 的效果说明。

## 6. 敌人、经济、材料与 Holy Mountain

- 每个 biome 按参考敌人角色和遭遇密度建立数据目录：地面/飞行、近战/远程、爆炸、召唤、机械、法师、Boss 与 faction/friendly-fire 关系。
- 玩家与敌人共享伤害、projectile、explosion、status、stain、材料和世界效果合同；敌人尸体、血液、火、毒、酸与电必须进入材料世界，而不是只播动画。
- 敌人掉落金币；金币消失节奏、拾取风险、商店价格、Spell/Wand tier、chest、Flask 和 Perk 候选由 run seed 与 biome tier 决定。
- 材料必须通过准星/hover 名称、Flask 标签、stain/status 图标和说明被玩家识别；不能要求玩家从颜色猜用途。
- Holy Mountain 完整提供恢复、Spell refresh、购买、Wand 编辑、Perk 三选一、reroll、出口坍塌及保护区破坏后果。所有交互立即写入权威 run state，不存在未接线按钮或假交易。

## 7. UI parity

UI 使用 PixelEngine Web-first 透明 Canvas 实现，但目标是复现 Noita 的信息密度和布局节奏，而不是沿用当前两个大型诊断面板。

- Gameplay HUD：生命/最大生命、金币、有限悬浮、选中 Wand/item、mana/cast/recharge、charges、stain/status 与 Boss 状态。
- Quick inventory：四个 Wand + 四个 item，数字键、滚轮和鼠标选择只有一个权威 input owner；选中态、耗尽态和不可用态清楚可见。
- Context feedback：准星目标材质名、拾取名称/价格、Wand/Spell/Flask tooltip、危险与交互提示。
- Inventory/Wand editor：Wand 列表、完整 stats、Spell slots、拖放、对比、容量与非法组合反馈；编辑权限严格服从 run 状态。
- Holy Mountain：世界内商店和 Perk altar 配合紧凑 tooltip，不使用遮挡游戏的大型运营式 dashboard。
- Pause/Settings/Death/Run Summary：布局、导航、输入和可读性与参考流程对应，并提供键鼠/手柄等价路径。

参考 UI 截图、目标截图、viewport、像素差异和允许差异必须进入 parity matrix；正式证据只接受真实 PixelEngine Editor/Player capture。

## 8. 终局与扩展世界

The Laboratory 包含 Sampo 与 Kolmisilma。取得 Sampo 后触发真实 Boss 战；Boss 攻击必须作用于 projectile、材料、场地和玩家状态。胜利、死亡和继续探索路线都进入真实 run 结果，并可靠清理旧生命周期。

主路径闭合后继续扩展地表区域、侧区、Orb/quest、parallel worlds、New Game+ 和替代结局。扩展内容不得反向延迟主路径、Wand、敌人、Holy Mountain 和 UI 的首次完整闭环。

## 9. Canonical 实施顺序

1. `DEMO-007`：Campaign/Sandbox、run lifecycle、Noita 主路径拓扑、输入所有权、悬空地形正确性和基础 HUD。
2. `DEMO-008`：八个主路径 biome、Holy Mountain 锚点、程序化遭遇点、侧区、秘密连接和地图 parity。
3. `DEMO-009`：Wand/Spell 数据、求值器、施法、库存、Wand 编辑和对应 UI。
4. `DEMO-010`：敌人 AI、统一伤害/status/stain、掉落、金币与 biome 生态。
5. `DEMO-011`：Holy Mountain 恢复、Spell refresh、商店、Wand 编辑、Perk 与保护区后果。
6. `DEMO-012`：The Laboratory、Sampo、Kolmisilma、完成/死亡结算与下一轮。
7. `DEMO-013`：全流程平衡、参考 parity matrix、侧区/秘密/parallel-world 扩展、可访问性、性能、真实窗口证据与最终输出。

发现通用能力缺口时，先在对应 canonical task 中设计带 XML 文档和测试的引擎公开 API，再由 Demo 消费；禁止 internals、反射或 friend assembly 后门。

## 10. 完成定义

完整 goal 只有在以下条件同时满足后才算完成：

- Campaign 可从 Mountain 开始，连续完成八个 biome、七个 Holy Mountain、Wand 构筑、战斗、Kolmisilma 和结算；死亡可开始干净新 run。
- Infinite Sandbox 仍可选择，保留无终点探索、修改持久化和安全重生。
- parity matrix 中主循环、地图、Wand/Spell、敌人、材料、UI、Holy Mountain、Boss 和生命周期均有自动化与真实 Player 证据，未实现差异被显式记录而非隐藏。
- Demo 专属内容只存在于 Demo 层；引擎保持通用公开 API，稳态热路径符合零分配与预算纪律。
- 仓库和玩家包不依赖外部 Noita 安装；资产 provenance 完整，构建可复现。
- 定向单元/性质/集成测试、Release solution、性能场景、真实 PixelEngine Editor/Player 输入、截图/日志和 clean final-output 全部绑定同一提交通过。
- `plan/tasks/`、详细设计、evidence index、parity matrix、最终输出 manifest 与安装包身份同步。
