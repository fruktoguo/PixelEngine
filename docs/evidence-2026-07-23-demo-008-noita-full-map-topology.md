# 2026-07-23 DEMO-008 Noita 完整宏图与固定地形检查点

## 1. 结论

提交 `ff9898bffbad47ae464461630976285fc5162e2a` 已把 Demo 从“少量支持区域 + 默认实心格 + 连续直井”的近似地图，纠正为 Noita Build `17130612` 的完整 `70x48` 二维宏观色图：129 种实际 biome 全部进入运行时，3360 个宏格逐格匹配参考 PNG，旧直井被删除。出生山体、Holy Mountain 大尺度组合和 The Laboratory 固定地形也改为来源 hash 锁定的语义掩码。

此报告只登记这个可复核检查点，不关闭 `DEMO-008`。普通 biome 的 Wang/BitmapCaves、背景、spawn marker、其余 pixel scene 和实体生态尚未完整接入，因此当前结果不能描述为 Noita 全地图视觉或玩法完全复刻。

## 2. 运行身份

| 项目 | 值 |
|---|---|
| task | `DEMO-008`，保持 `[~]` |
| implementation commit | `ff9898bffbad47ae464461630976285fc5162e2a` |
| run session | `local-20260723-demo008-map-topology-ff9898bf` |
| clean worktree | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-ff9898bf-clean` |
| tracked source status | detached HEAD 指向上述完整 SHA；验证前后无 tracked diff |
| native inputs | Box2D `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`；FreeType `0a0221a1347e2f1e07c395263540026e9a0aa7c7`；dlg `395ccad2c1e0daae535c4d20bb0a3f2424648e17`；RmlUi `22b93ae968dab2713a57780408513d8859bb9503` |
| reference build | Steam Noita Build `17130612`，`_version_hash.txt=9dbd52ced019a643169a2db02f46c77f8766c6e5` |
| machine | Windows 11 `10.0.26100.8457`；AMD Ryzen 7 5800X 8C/16T；AMD Radeon RX 7900 XT；.NET SDK `10.0.108`；win-x64 |

Noita 参考副本的来源、`data.wak` hash、解包树 hash、坐标换算和加载链见 [`reference-noita-build-17130612-map-topology.md`](reference-noita-build-17130612-map-topology.md)。正式仓库和 Player 不读取 Noita 安装目录或解包目录。

## 3. 来源逐项审计

在 detached clean commit 中读取产品 `biomes.json`，同时只读打开隔离参考副本的 `biome_map.png` 和九个固定 512 掩码来源；Brotli 产品掩码按运行时相同方式解码后重算 SHA256。

| 检查项 | 结果 |
|---|---:|
| 宏图尺寸 | `70x48` |
| 宏格总数 | `3360` |
| 产品记录的 reference biome | `129` |
| 色图实际使用的索引 | `129` |
| 逐格颜色/索引不匹配 | `0` |
| 512x512 固定掩码 | `9` |
| 固定掩码来源 SHA256 不匹配 | `0` |
| 固定掩码解码长度不匹配 | `0` |
| 固定掩码解码 SHA256 不匹配 | `0` |
| Laboratory 边界 | `2600x1600`，`X=1536..4135`，参考深度 `12288..13887` |
| Laboratory 解码字节数 | `2,080,000` |
| Laboratory 来源 SHA256 | match |
| Laboratory 解码 SHA256 | match |

实现使用 `512` cell 宏格和 `(35,14)` 色图原点。Noita 出生坐标 `(227,-85)` 在 Demo 的安全地表偏移后为 `(227,139)`。色图外仍保留 `legacy` 无限世界回退，但色图内部不再用默认 solid 填补未知格。

## 4. Clean build 与测试

| 命令/范围 | 结果 |
|---|---|
| `git submodule update --init --recursive` | 四个锁定 native 输入 materialize 成功 |
| `pwsh -NoProfile -File tools/build-native.ps1 -Rid win-x64 -Configuration Release` | Box2D shared/static、FreeType、RmlUi/UI native 成功；exit 0 |
| `dotnet build PixelEngine.sln -c Release --disable-build-servers -m:1` | 0 warning / 0 error；39 个项目完成 restore/build |
| `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --disable-build-servers -m:1` | 193 passed / 1 explicit native-GL skip / 0 failed；194 total |

TRX：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-ff9898bf-clean\tests\PixelEngine.Demo.Tests\TestResults\demo008-ff9898bf-clean.trx`，SHA256 `4EFFA9C3720737CFA25E2288E4F329E6D0AD71733711D9D119DF864498D1DDF9`。

首次在新 worktree 跑 Demo 全套时，三个 native submodule 尚未 materialize，得到 100 passed、1 skip、93 个 `DllNotFoundException: box2d` 失败。按仓库标准流程初始化锁定 submodule 并构建 native 后，同一源码和同一测试命令取得上表全绿结果；前一轮只作为环境准备诊断，不冒充源码回归。

## 5. 真实 Player framebuffer

两个窗口 probe 均从 clean commit 的正式 Demo project 启动，使用 `scenes/infinite-sandbox.scene`、Campaign 默认模式、RmlUi active backend、`fallback=False`，进程 exit 0。截图由 GL presentation framebuffer 直接读取，不是离线示意图。

| 场景 | 运行态 | artifact | SHA256 |
|---|---|---|---|
| 出生点 | 7 frames；玩家 `(227,139)`；出生山体左入口坡面和下方洞室进入画面 | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-ff9898bf-evidence\spawn-7.bmp`；1080x720x32；3,110,454 bytes | `C8E7565D51A539AD08C8D23A200D0D86EA8AC547291AC11D18155231D3EDF43C` |
| 山体内部 | 180 scripted frames；玩家中心 `(413.69,117.00)`；镜头进入 hall/right 组合；65.6 effective FPS 的末帧探针 | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-ff9898bf-evidence\mountain-180.bmp`；1080x720x32；3,110,454 bytes | `9E09162DF6281D776BAD221162997F7A9ECC281AD85DAC0A3B74C242DFEB91E9` |

这两张图证明出生坐标、固定山体轮廓、真实 Player 渲染链和山体内移动已接通；它们不替代后续全部 70x48 区域的同 seed 截图矩阵。

## 6. Clean benchmark

命令：

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release -- --filter "*InfiniteTerrainChunkGenerationBenchmarks*" --job short --inProcess --artifacts <temp-output>
```

BenchmarkDotNet `0.15.8`，`.NET 10.0.8`，InProcess ShortRun，3 warmup + 3 measured iteration。最终报告：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-ff9898bf-benchmark-clean\results\PixelEngine.Benchmarks.InfiniteTerrainChunkGenerationBenchmarks-report-github.md`。

| 位置 | Mean | Allocated |
|---|---:|---:|
| SurfaceWest / lake | `81.60 us` | `0 B` |
| SurfaceOrigin / spawn mountain | `330.70 us` | `3 B` |
| SurfaceEast / desert | `744.55 us` | `7 B` |
| MinesDeep | `491.44 us` | `7 B` |
| FungalCaverns | `383.30 us` | `3 B` |
| PortalHolyMountain | `266.90 us` | `1 B` |
| LaboratoryDeep / fixed arena | `102.48 us` | `1 B` |

`0-7 B` 是当前 MemoryDiagnoser 的单次诊断舍入噪声范围；生成器已有独立稳态 allocation gate。首次 clean benchmark 启动缺 benchmark project 的 `project.assets.json`，没有产生样本；允许该 worktree 正常 restore 后上表运行完整结束。更早的非 InProcess 尝试因仓库历史 artifact 中的同名 benchmark project 造成 BenchmarkDotNet 临时工程歧义而被判 invalid，其输出未计入结果。

## 7. 本检查点实际修正

- `biomes.json` v6 记录 129 种来源颜色/biome 和 48 行完整 row-major 索引，运行时拒绝版本/hash/尺寸/未使用索引/错误 row。
- 生成器先做完整 X/Y 宏观分类，再进入 biome 语法；旧的全局连续摆动直井已删除。
- left entrance、hall、right、top、floating island 和四个 altar 变体使用 512x512 四分类掩码。
- Holy Mountain 保留 Portal/供能池/设施玩法覆盖，但外部大轮廓由参考 temple 组合决定。
- The Laboratory 使用固定 2600x1600、4-bit/8 材质类别掩码；旧随机 encounter、手写桥/vat/lava sea 替代已删除。
- 固定掩码只在配置加载阶段解码；64x64 chunk 生成热循环直接索引预解码定长数组。

## 8. 未完成边界

`DEMO-008` 继续为 active，下一阶段必须依次补齐：普通 biome Wang template 与 BitmapCaves；背景层和颜色 spawn marker；剩余固定/random pixel scene；地图玩法实体和生态；全区域同 seed reference/Player 截图矩阵；完整主路径无软锁长路线。Wand/Spell、敌人生态、Perk、Boss 与终局交互分别由后续 `DEMO-009` 至 `DEMO-012` 承接。
