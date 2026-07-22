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
