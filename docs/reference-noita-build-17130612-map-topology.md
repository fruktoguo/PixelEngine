# Noita Build 17130612 地图生成参考基线

> 用途：为 `DEMO-008` 提供可复核的本机参考事实。本文只记录结构、坐标、数量与哈希；Noita 原始图片、脚本、音频和二进制资源不进入 PixelEngine 仓库或玩家包。

## 1. 来源与完整性

2026-07-22 使用用户合法安装的 Steam Noita（AppID `881100`）及游戏自带 `noita.exe -wizard_unpak` 建立隔离参考副本：

| 项目 | 值 |
|---|---|
| Steam build id | `17130612` |
| branch | `master` |
| `_version_hash.txt` | `9dbd52ced019a643169a2db02f46c77f8766c6e5` |
| `data/data.wak` SHA-256 | `C95A0C01A55EC29267AFEF6BBEC8A0CAE0BA2B350638E2203674ED4DFB9227C3` |
| 解包文件数 | `14,745` |
| 解包字节数 | `41,654,429` |
| 解包树聚合 SHA-256 | `B97CC3884FA6BD17A1A14634ECBA60BE42148554F94DE4B572D52220F61B62B3` |

解包器在用户目录额外创建两个 0 字节警示文件；排除这两个新增文件与解包 `data/` 后，原有 696 个用户文件的聚合 SHA-256 仍为 `D37A4630A6D439DB22A3092DA9836FE24396268F18316F4D7F71376032361DE5`，与运行前一致。

## 2. 权威加载链

主世界不是“按深度选择一组噪声参数”的一维带状地图，而是分为四层：

1. `magic_numbers.xml` 的 `BIOME_MAP` 指向 `data/scripts/biome_map.lua`。
2. `biome_map.lua` 把地图尺寸固定为 `70×48`，再加载 `data/biome_impl/biome_map.png`。
3. `data/biome/_biomes_all.xml` 以 `biome_offset_y="14"` 将颜色映射到 biome XML；文件声明 175 项映射、174 个唯一颜色/biome，主图实际使用 129 种颜色。
4. 每个 biome XML 再指定 Wang template、Lua spawn script、BitmapCaves 参数、材料层与植被；全局 `_pixel_scenes.xml` 和各 biome Lua 叠加固定场景、随机 pixel scene、实体和地标。

`70×48` 色图中的一个宏格对应 `512×512` 世界 cell；中心世界原点位于 map X `35`，地表基准位于 map Y `14`。因此世界 X 可由 `(mapX - 35) × 512` 推导，世界 Y 可由 `(mapY - 14) × 512` 推导，单个平行世界的横向周期为 `70 × 512 = 35,840` cell。

## 3. 中央主路径纵深

下表坐标是 Noita 参考世界坐标。PixelEngine 保留安全地表 `SurfaceY=224` 时，目标绝对 Y 为表中数值统一加 `224`，不能再把所有区域压成等高 512-cell 条带。

| 阶段 | 参考 Y | 高度 | 主要宏观范围 |
|---|---:|---:|---|
| Mines | `0..1023` | 1024 | 主体约 `X=-512..2047`，西侧含 Collapsed Mines |
| Holy Mountain 1 | `1024..1535` | 512 | temple wall + left/altar/right 组合 |
| Coal Pits | `1536..2559` | 1024 | 主体约 `X=-2048..2047`，西侧 Fungal Caverns |
| Holy Mountain 2 | `2560..3071` | 512 | temple 组合 |
| Snowy Depths | `3072..4607` | 1536 | 主体约 `X=-2560..2559`，东北 Chasm 回 Mines |
| Holy Mountain 3 | `4608..5119` | 512 | snowcave 右侧 altar 变体 |
| Hiisi Base | `5120..6143` | 1024 | 主体约 `X=-2048..1535` |
| Holy Mountain 4 | `6144..6655` | 512 | snowcastle 右侧 altar 变体 |
| Underground Jungle | `6656..8191` | 1536 | `rainforest` + `rainforest_open`，主体约 `X=-2560..2047` |
| Holy Mountain 5 | `8192..8703` | 512 | temple 组合 |
| The Vault | `8704..10239` | 1536 | 主体约 `X=-3072..2559` |
| Holy Mountain 6 | `10240..10751` | 512 | temple 组合 |
| Temple of the Art | `10752..12799` | 2048 | 主体约 `X=-4608..2559` |
| Final Holy Mountain | `12800..13311` | 512 | `temple_wall_ending` 与终局入口，不是普通噪声带 |

The Laboratory 不能建模为第八个全宽随机 biome。主世界通过 `data/biome_impl/spliced/boss_arena.xml` 在 `X=1536..4135`、`Y=12288..13887` 拼接 `5×4` 个 512-cell 场景块，整体尺寸 `2600×1600`；它在空间上与 Temple 底部/Final Holy Mountain 交叠，并通过固定入口连接。

## 4. 当前 Demo 的结构性偏差

截至提交 `fd501c0c`，Demo 有八个名称正确的 biome、七个 Portal/Holy Mountain 锚点和数据化地标，但地图轮廓不具备参考 parity：

- `campaign.json` 把七个普通区域统一设为 512 高、Holy Mountain 统一设为 128 高，完整主路径被压缩到参考纵深的约三分之一。
- `CampaignConfig.ResolveLocation(worldY)` 只按 Y 分类，无法表达同一深度上主区、侧区、实心边界与 Laboratory 固定场景并存。
- `PlayableCavernWorldGenerator` 在每一行先挖 `MainPathHalfWidthCells` 的连续摆动竖井，再以同一个 biome grammar 填满横向世界；参考世界以 70×48 宏观色图先限定区域，再在每个 biome 内生成 Wang/BitmapCaves。
- Holy Mountain authored layout 仅占 128-cell 高带且半宽 176；参考入口由 512×282 的 altar/left/right 像素场景组合并落在完整 512-cell 宏格中。
- 当前 Laboratory 使用随机 grammar + 一个手写 bridge landmark；参考终局是固定 `2600×1600` 拼接场景。
- 当前 11 个 pixel scene 只支持矩形/椭圆操作，不能表达 Wang 边码、颜色 spawn marker、背景层和成组场景权重。

因此此前 parity matrix 中“主路径拓扑已实现”“Holy Mountain 地图已实现”“Laboratory 地图轮廓已实现”的结论全部降级为历史功能基线，不能作为 `DEMO-008` 完成证据。

## 5. 实现约束

- Noita 原始资源只作为本机只读参考；正式构建和测试不得依赖 Noita 安装目录或解包目录。
- Demo 需要自有的语义宏观拓扑、Wang 边码、程序化场景与材料数据，不把参考 PNG/Lua/XML 原样复制进仓库。
- 先实现 512-cell 宏格、可变 region span、X/Y 二维分类和固定 Laboratory，再扩充每个 biome 的 Wang/pixel-scene 目录。
- 所有生成仍只由 `RunSeed + global cell/chunk coordinate` 决定，保持加载顺序无关、修改持久化优先和 64×64 chunk 热路径零稳态分配。

## 6. PixelEngine 实现检查点

2026-07-22 的后续实现按连续提交节点推进：

- `campaign.json` v5 恢复主路径七个程序化区域的 `1024/1024/1536/1024/1536/1536/2048` 高度与七个 512-cell Holy Mountain，旧 v1-v4 存档配置只读迁移到新跨度。
- 早期 `biomes.json` v4 节点只用 98 个运行段覆盖 479 个已支持语义宏格，其余 2881 个格保持 `solid`；该状态是迁移检查点，不再代表当前实现。
- `biomes.json` v6 现记录主图实际使用的全部 129 种颜色/biome、来源 XML 标识与 48 行 row-major 索引。逐格对照隔离解包副本 `biome_map.png` 的 3360 个像素为 `Mismatches=0`，运行时不再构造旧连续摆动竖井。
- 固定地形不把 PNG 原文件装入产品包：left entrance / hall / right / top / floating island 与四个 temple altar 变体按 Demo 的 empty/soft/solid/accent 语义压缩，并同时记录来源 SHA256 与解码后 SHA256；生成器在 `Describe` 阶段一次解码，chunk 热循环只做定长索引。
- Laboratory 保持准确的 `1536,12288,2600,1600` 边界，并把 `boss_arena.png` 的 416 万来源像素映射为 4-bit/8 类 Demo 材质掩码；随机 Laboratory encounter 与原手写矩形替代均被禁用，材质温度服从二维拓扑覆盖。

此检查点闭合完整宏观色图、关键固定山体、Holy Mountain 大尺度组合和 Laboratory 地形轮廓。每个普通 biome 的 Wang/BitmapCaves、背景层、spawn marker、其余固定场景、实体生态和全区域截图 parity 仍是 `DEMO-008` 的未完成项。

## 7. 完整世界内容目录

2026-07-25 新增 `tools/extract-noita-world-content.ps1`，从同一隔离参考树生成
`demo/PixelEngine.Demo/content/noita-world-content.json`。目录只保存结构化属性、资源路径、坐标、
概率入口和 SHA256，不复制 Noita 原始图片、Lua、XML 或音频内容，也不让运行时依赖本机安装。

Build `17130612` 的目录基线为：146 个 biome XML、640 层材料、317 层植被、785 个 Lua
spawn function、12 个全局 spliced pixel scene、91 个 buffered pixel scene、17 张定位背景、
2232 个 `biome_impl` 文件和 229 个 vegetation 文件。该目录是后续地形、背景、花草和交互物
复原的唯一来源清单；现有少量手写 scene/marker 不能再被描述为完整覆盖。

Wang 模板同时恢复为 `1 source pixel = 1 world cell`。512-cell biome macro map 和
BitmapCaves 负责大尺度结构，Wang semantic pixel 不再人为扩成 5 个 world cell，避免把材料边界、
洞穴细节和 spawn marker 粗化为类似略缩图的方块。

`tools/generate-noita-marker-rules.ps1` 将目录中的 785 条 Lua 注册记录、243 个唯一函数名在构建前
转换为 `NoitaMarkerRules.Generated.cs`。运行时只调用 C# `NoitaMarkerRuleCatalog`，分类为 PixelScene、
Vegetation、Loot、Prop、Encounter、Trigger 与 Effect；玩家包不包含 Lua 源码、Lua VM，也没有
运行时字符串 heuristic fallback。Lua 只作为隔离参考树中的只读来源。

`tools/generate-noita-pixel-scenes.ps1` 进一步把 91 个全局 buffered pixel scene 的世界坐标、尺寸、
清理/biome/边缘标志，以及 material/colors/background 三层资产路径与 SHA256 预编译为
`NoitaPixelScenes.Generated.cs`。运行时通过纯 C# `NoitaPixelSceneCatalog` 查询，不读取参考 XML 或 Lua；
其中 30 个 material、22 个 colors、6 个 background 资产描述已锁定。该节点只闭合来源与调度目录，
实际语义像素掩码、背景纹理和实体生成仍必须在后续节点接入后才能形成视觉 parity。

全局 material Pixel Scene 已进一步经 `tools/generate-noita-pixel-scene-masks.ps1` 构建时解析：
30 张 material PNG 的实际材料色通过 Build `17130612` `materials.xml` 的 `wang_color`/graphics color
回溯到稳定材质语义，13 种非材料 marker 色保留为空地并记录像素数量，等待实体生成系统消费。
语义掩码以 Brotli + decoded SHA256 进入 `NoitaPixelSceneMasks.Generated.cs`；世界生成器在
Wang/BitmapCaves 和程序 scene 之前按原始绝对坐标逐像素应用，比例固定为 `1 source pixel = 1 world cell`。
玩家包不携带来源 material PNG，也不读取本机参考树。colors/background 层与 marker 实体仍是后续接线项。

全局视觉层随后由 `tools/extract-noita-pixel-scene-visuals.ps1` 进入 Demo 自有内容包：6 张
background 在权威 cell 世界后方绘制，22 张 colors 在 cell 世界上方、光照合成前绘制；每层沿用
来源 world X/Y 与原始像素尺寸，不随窗口分辨率改变物理比例。`provenance.json` 逐项记录参考路径、
Build、source/content SHA256 和目标矩形，复制后 hash 必须完全一致。运行时现有 4 层出生山体视觉与
28 层全局 scene 视觉合计 32 层，仍低于 Hosting 固定 128 层上限。marker 实体仍待后续接线。
