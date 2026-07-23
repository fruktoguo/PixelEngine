# Noita Build 17130612 Wand / Spell 参考基线

> 用途：为 `DEMO-009` 提供可复核的本机参考事实和版本化 parity matrix。Noita 原始 Lua、图片、文本、音频和二进制资源不进入 PixelEngine 仓库或玩家包；Demo 使用独立编写的数据、代码和表现资源。

## 1. 来源身份

2026-07-23 只读检查用户的 Steam Noita 安装及隔离解包副本：

| 项目 | 值 |
|---|---|
| 安装目录 | `D:\SteamLibrary\steamapps\common\Noita` |
| 隔离数据目录 | `D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data` |
| Steam build id | `17130612` |
| `_version_hash.txt` | `9dbd52ced019a643169a2db02f46c77f8766c6e5` |
| `data/scripts/gun/gun.lua` SHA-256 | `30b1cfae774978950ce4a8b595d98e7475799689df42ff2d165ccb6257b3b96c` |
| `data/scripts/gun/gun_actions.lua` SHA-256 | `09a034b16fbacbe330b76f47bf66a05919d8d3301c1030263851ab95b5c8c532` |
| `data/scripts/gun/gun_enums.lua` SHA-256 | `31ddde97d0ad8a3ec33e5922ca51c663431b882d52090696721610eca6c9c469` |
| `data/scripts/gun/gunaction_generated.lua` SHA-256 | `13d8e7825acb61610e8f742af856cbb559ad5cfee3a35980b6c3712ba842e460` |
| `data/scripts/gun/gun_extra_modifiers.lua` SHA-256 | `9ed222afa98ea98d81953209afee1594285936e81a4c97e54d03e8bc7af66f66` |

正式构建只读取 `demo/PixelEngine.Demo/content/wand-spells.json`，不探测上述本机路径。

## 2. Action 目录清点

对 Build `17130612` 的 action 定义按参考枚举和行为入口清点得到 491 项：

| 参考类别 | 数量 |
|---|---:|
| projectile | 144 |
| static projectile | 46 |
| modifier | 179 |
| draw many | 14 |
| material | 35 |
| other | 43 |
| utility | 25 |
| passive | 5 |
| 合计 | 491 |

这些数字和五个来源哈希作为 `wand-spells.json` 的 reference identity 入盘，目录加载会校验 build id、40 位 commit hash、SHA-256、分类合计和重复路径。

## 3. 当前 Demo parity matrix

| 参考语义 | PixelEngine Demo 检查点 | 状态 |
|---|---|---|
| ordered / shuffle deck | 每把 Wand 独立 `DeckOrder`、cursor、cycle 与 seed 确定性 shuffle | 已实现 |
| spells per cast | 有界 `DrawMany`，draw operation 上限 64 | 已实现 |
| cast delay / recharge | 每把 Wand 独立冷却与 deck refill recharge | 已实现 |
| mana max / charge speed | 独立 mana、逐帧恢复和 passive multiplier | 已实现 |
| capacity / spread / speed multiplier | 加载期范围校验，确定性角度散射与速度合并 | 已实现 |
| always cast | 最多 4 张，独立有限次数与 mana 支付 | 已实现 |
| modifier / multicast | modifier accumulator、自动 draw、2/3 发与 scatter draw | 已实现 |
| hit / timer / death trigger | parent index payload 树，运行时延迟激活 | 已实现 |
| material / utility / passive / limited-use / special | 25 个原创 Spell 覆盖九类；材质、挖掘、传送、光照、mana、次数与 repeat 均有实现 | 已实现 |
| 世界效果安全相位 | 仅经公开 `Cells`、`World`、`Particles`、`Lighting`、`Audio` API | 已实现 |
| 稳态求值与 projectile 生命周期 | evaluator 零托管分配；256 槽预热 pool；越出 resident world 立即回收 | 已实现 |
| 四 Wand inventory 与编辑 | Web-first inventory v2、HUD、数字键/滚轮选择、16 槽点击循环编辑 | 已实现 |
| 四 item、拾取、交换、拖放、丢弃 | 尚未进入本检查点 | 未实现 |
| Wand/Spell run save/load | 尚未进入本检查点 | 未实现 |
| 491 个参考 action 的逐项行为与数值 | 当前只完成 25 个原创代表 Spell，不宣称逐项覆盖 | 未实现 |

## 4. 有界求值合同

`wand-spells.json` 当前固定预算为：Wand capacity 16、always-cast 4、每次 draw 64、递归深度 8、每次 projectile 32。任一预算或可用 projectile pool 容量不足都会恢复 mana、冷却、deck order/cursor/cycle、cast sequence 和有限次数，并返回明确失败状态；不会留下半个 payload tree。

四把当前 Wand 为 Apprentice、Trigger、Chaos 和 Geomancer。它们各自保存 mana、冷却、牌序和次数；重复 Spell slot 合法且有限次数按 slot 隔离。这个目录用于验证引擎公开 API 与 Noita-like 牌组机制，不表示 491 个参考 action 已全部复刻。

## 5. 后续闭合条件

`DEMO-009` 保持 active，下一检查点必须补齐四个 item slot、世界拾取/交换/丢弃、明确的 Wand/Spell 移动操作、权威 run state 存取和真实 Player 输入证据。随后才能扩展 action parity matrix；未实现的参考 action 不能用同名空配置或无效果 stub 冒充。
