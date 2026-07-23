# 2026-07-23 DEMO-008 Noita BitmapCaves 与地图标记检查点

## 1. 结论

提交 `9fa14e871dbc951dcc3d672ba3fec67f0fc86489` 已把 Steam Noita Build `17130612` 的 biome XML `BitmapCaves` 配置接入 Demo，并保持既有固定 pixel scene 高于 BitmapCaves、BitmapCaves 高于 Wang template 的地形优先级。15 个 Wang set 中，4 个源 biome 没有 `BitmapCaves` 节点、3 个显式全零禁用、8 个启用；Coal Mine 的 `dangerroom.png` 也按来源 SHA 和 8x8 semantic mask 锁定。

运行时不再逐 cell 重建洞穴几何。每个线程使用固定 16-slot block cache，cache miss 时一次编译几何、64x64 spatial bin、8x8 boundary scale 与完整 bounded semantic mask；last-block 命中直接由世界坐标恢复 local cell。最终 clean benchmark 为冷 block `1.702 ms`、热采样 `5.612 ns`、实际 marker 窗口 `120.424 us`，稳态托管分配为 `0/0/1 B` 的 MemoryDiagnoser 舍入值。

本检查点不关闭 `DEMO-008`。背景 pixel scene、marker 对应的真实敌人/可拾取道具/物理陷阱、剩余 fixed/random pixel scene、完整材料与生态、Noita 原始 RNG 调用序列、同 seed 全区域截图矩阵和组合后无软锁长路线仍未完成。

## 2. 运行身份

| 项目 | 值 |
|---|---|
| task | `DEMO-008`，保持 `[~]` |
| implementation commit | `9fa14e871dbc951dcc3d672ba3fec67f0fc86489` |
| commits in checkpoint | `c9ebe0d843923ef196b9808a682cdf1c602e4d44` 功能；`4990983d01036b184d53ed20385fdf391250080c` 提取换行；`9fa14e871dbc951dcc3d672ba3fec67f0fc86489` 热路径 |
| run session | `local-20260723-demo008-bitmapcaves-9fa14e87` |
| clean worktree | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-bitmapcaves-c9ebe0d8` |
| tracked source status | detached HEAD 指向完整 SHA；最终 build、test、benchmark 前后 `git status --short` 为空 |
| native inputs | Box2D `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`；FreeType `0a0221a1347e2f1e07c395263540026e9a0aa7c7`；dlg `395ccad2c1e0daae535c4d20bb0a3f2424648e17`；RmlUi `22b93ae968dab2713a57780408513d8859bb9503` |
| reference install | `D:\SteamLibrary\steamapps\common\Noita`，只读读取 |
| isolated data root | `D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data` |
| reference identity | Build `17130612`；`_version_hash.txt=9dbd52ced019a643169a2db02f46c77f8766c6e5` |
| catalog SHA256 | `EDB357A41AB51B0C3F1C684AD62EA82BAEA2694FC576A26B0891AE101E816B78` |

## 3. 可重现来源目录

`tools/extract-noita-wang-terrain.ps1` 结构化读取 15 个 biome XML 的 `BitmapCaves`、Wang PNG/XML/Lua marker、`materials.xml` 与 structure PNG。最终输出显式规范为 UTF-8 no-BOM + LF，消除了 Windows CRLF 工作树与 clean checkout 的字节哈希差异。

最终 clean worktree 执行：

```pwsh
pwsh -NoProfile -File tools/extract-noita-wang-terrain.ps1 `
  -DataRoot 'D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data' `
  -NoitaInstallRoot 'D:\SteamLibrary\steamapps\common\Noita' `
  -OutputPath <temp-json>
```

仓库目录与重提取产物 SHA256 均为 `EDB357A41AB51B0C3F1C684AD62EA82BAEA2694FC576A26B0891AE101E816B78`，`Match=True`。

| 分类 | set |
|---|---|
| source XML 无 `BitmapCaves` | `fungicave`、`fungiforest`、`snowcave`、`snowcastle` |
| 节点存在但 count 全零 | `rainforest`、`rainforest-open`、`rainforest-dark` |
| 启用 | `coalmine`、`coalmine-alt`、`excavationsite`、`vault`、`vault-frozen`、`crypt`、`wandcave`、`wizardcave` |

Coal Mine structure 来源为 `data/biome_impl/coalmine/dangerroom.png`，SHA256 `dd94d797e8cb3d7c2a121a0f53681a8a3ef8c0d222bdf01d53f5da5487a9401e`，尺寸 8x8；解码 semantic SHA256 为 `391a558df56f00b6b8792b4abb1e90244914c49ba2c3a82abc699bc323c7fb04`。目录加载会 fail-closed 拒绝错误范围、来源路径、图像 SHA、decoded SHA、semantic、marker index、feature 上界与 block 尺寸。

## 4. 运行时合同

- `PlayableCavernWorldGenerator.PersistenceKey` 升级为 `showcase-campaign-v11`，避免旧 region store 把新地形语法覆盖成历史结果。
- authored pixel scene 继续最高优先；其余 main/side reference biome 先采样 BitmapCaves override，未命中才采样 Wang。
- block 几何只由 `world seed + biome salt + block coordinate` 决定；正负坐标、加载顺序和重复访问不改变结果。
- 每线程 16 个固定 cache entry，带 last-hit fast path；每 entry 的 feature、spatial reference、boundary scale、semantic 与 override bitset 都是加载后固定容量数组，稳态不租还数组、不加 cell lock。
- 最大来源 block 为 516x256，feature 上界 256；超限配置在目录加载期拒绝，而不是运行时扩容。
- 当前坐标散列是适合并行 chunk 生成的确定性序列，不冒充 Noita 内部 RNG 调用序列；因此相同数值 seed 尚不能宣称逐 cell 与 Noita 实机一致。

## 5. Wang marker materialization

`CollectWangMarkerAnchors` 通过调用方提供的固定容量 `Span<NoitaWangMarkerAnchor>` 暴露 reference biome、Wang set、marker color/function/origin/semantic 和世界坐标。`NoitaWangMarkerContentSystem` 每 0.35 秒扫描玩家附近 1025x769 cell 窗口，并将已知 marker 物化为 Demo 自有 overlay prop、VFX burst 与点光源；它不写 cell 网格，因此不会制造不可命中的背景颗粒或悬空地形。

当前真实 gameplay 映射包括 oil/acid/fire `MaterialEmitter` 与 smoke/fire/crystal `SparkEmitter`。系统只在 `Exploring`、`HolyMountain`、`Laboratory` 激活，0.5 秒 warmup 按每帧 `dt` 累积；死亡、完成和结算阶段停用 emitter。marker 去重与 emitter 引用均使用固定容量数组。

这些映射已经是实际脚本实体，但仍不是最终敌人、箱子拾取、物理机关或背景 scene loader；后续 checkpoint 必须替换对应类别，不能把当前 overlay prop 当作完整复刻。

## 6. Clean build 与测试

| 命令/范围 | 结果 |
|---|---|
| `git submodule update --init --recursive` | 四个锁定 native 输入 materialize 成功 |
| `pwsh -NoProfile -File tools/build-native.ps1 -Rid win-x64 -Configuration Release` | Box2D shared/static、FreeType、RmlUi/UI native 成功；exit 0 |
| `dotnet build PixelEngine.sln -c Release --no-restore` | 0 warning / 0 error |
| `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --no-build` | 218 passed / 1 explicit native-GL skip / 0 failed；219 total |

新增和扩展测试覆盖 XML 分组与范围、danger room 来源/语义、损坏目录 fail-closed、8 个启用配置的正负 block/遍历顺序/seed、禁用配置、65,536 次热采样分配、生成器实际覆盖、Fungal Caverns 无配置不受影响、marker anchor 确定性与空地合同、profile gameplay 映射、状态门控和逐帧 warmup。

## 7. Clean benchmark

两组都使用 BenchmarkDotNet `0.15.8`、`.NET 10.0.8`、InProcess ShortRun、1 launch、3 warmup、3 measured iteration；主机为 AMD Ryzen 7 5800X 8c/16t，Windows 11 build `26100.8457`。

### 7.1 BitmapCaves 分项

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release --no-build --no-restore -- `
  --filter "*NoitaBitmapCavesBenchmarks*" --job short --inProcess --artifacts <temp-output>
```

报告：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-bitmapcaves-9fa14e87-clean\results\PixelEngine.Benchmarks.NoitaBitmapCavesBenchmarks-report-github.md`，SHA256 `5B1F8D539C427150FFD463177FEBAB5F5607C2BC46EDA9008D0F8C7DCF2A55B0`。

| 方法 | 基准含义 | Mean | Allocated |
|---|---|---:|---:|
| `ColdBitmapBlock` | 新 512x256 block 的几何编译、索引和完整栅格化 | `1.702 ms` | `0 B` |
| `HotBitmapSample` | 已驻留 block 的单 cell 查询 | `5.612 ns` | `0 B` |
| `MarkerWindow` | gameplay 每 0.35 秒使用的 1025x769 窗口 | `120.424 us` | `1 B` |

在最后的 last-block fast path 前，同一 clean checkpoint 的热采样为 `8.794 ns`；最终值下降约 36%。`1 B` 是 MemoryDiagnoser 按 operation 摊分的舍入值，测试中的批量 allocation gate 仍单独约束实际线程分配。

### 7.2 七场景完整 chunk

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release --no-build --no-restore -- `
  --filter "*InfiniteTerrainChunkGenerationBenchmarks*" --job short --inProcess --artifacts <temp-output>
```

报告：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-terrain-9fa14e87-clean\results\PixelEngine.Benchmarks.InfiniteTerrainChunkGenerationBenchmarks-report-github.md`，SHA256 `936557B87EECFAA9777B254FE493545C8D051B2C12B3B19B92378497CC5E7F5B`。

| 位置 | Mean | Allocated |
|---|---:|---:|
| SurfaceWest | `90.88 us` | `0 B` |
| SurfaceOrigin | `264.45 us` | `3 B` |
| SurfaceEast | `814.66 us` | `7 B` |
| MinesDeep | `366.31 us` | `3 B` |
| FungalCaverns | `420.43 us` | `3 B` |
| Portal/HolyMountain | `220.40 us` | `2 B` |
| LaboratoryDeep | `119.69 us` | `1 B` |

Wang-only 历史 clean ShortRun 为 `73.42–657.33 us/chunk`，本次为 `90.88–814.66 us/chunk`；跨 session 的七项平均值约增加 16%–24%，不能宣称“无回归”。另一方面，开发中的逐 cell 几何版本曾达到 SurfaceOrigin `527.51 us`、Mines `876.39 us`；固定 block cache 已将对应最终值压到 `264.45 us` 和 `366.31 us`。所有场景仍低于 `0.82 ms/chunk` 且无实际逐次托管分配。后续新增背景与生态时必须继续以这一报告为回归基线。

## 8. 未完成边界

下一地图节点应先接 biome background scene/pixel scene loader，并将 marker 的 scene-load、enemy、item、trap 类别替换为真实玩法实体；随后补剩余 fixed/random pixel scene、材料/生态、Noita RNG 序列与全区域截图/长路线矩阵。这个检查点没有新增 Player framebuffer，因此只证明来源、生成语义、脚本实体、测试和性能，不把视觉 parity 伪装成已完成。
