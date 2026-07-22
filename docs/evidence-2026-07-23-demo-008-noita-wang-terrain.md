# 2026-07-23 DEMO-008 Noita Wang 地形检查点

## 1. 结论

提交 `d2abcfd61fee35cc731322cbae83d385c8ab94a9` 已把 Build `17130612` 中当前完整宏图实际使用的 15 组 Herringbone Wang 模板接入 Demo：20 个 main/side reference biome 不再使用通用噪声洞穴，而是按各自模板的原始像素轮廓、材质色和 spawn marker 语义生成。固定 pixel scene 仍保持更高优先级，chunk 热路径按全局坐标确定 tile 与 variant，负坐标和加载顺序不改变共享边。

本检查点不关闭 `DEMO-008`。Noita 的 `BitmapCaves`、背景图层、marker 对应实体/结构生成、更多 pixel scene 和完整材料目录仍未接入；当前 variant 选择使用可并行的坐标散列，不冒充复现 Noita 内部未锁定的 RNG 调用序列，因此同一个数值 seed 尚不能逐 cell 对齐 Noita 实机世界。

## 2. 运行身份

| 项目 | 值 |
|---|---|
| task | `DEMO-008`，保持 `[~]` |
| implementation commit | `d2abcfd61fee35cc731322cbae83d385c8ab94a9` |
| run session | `local-20260723-demo008-wang-d2abcfd6` |
| clean worktree | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-d2abcfd6-clean` |
| tracked source status | detached HEAD 指向完整 SHA；构建、测试和 Player capture 前后 `git status --short` 为空 |
| native inputs | Box2D `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`；FreeType `0a0221a1347e2f1e07c395263540026e9a0aa7c7`；dlg `395ccad2c1e0daae535c4d20bb0a3f2424648e17`；RmlUi `22b93ae968dab2713a57780408513d8859bb9503` |
| reference build | Steam Noita Build `17130612`，`_version_hash.txt=9dbd52ced019a643169a2db02f46c77f8766c6e5` |
| catalog SHA256 | `BE423B0049D2695359F1D3C66801208584153A149EB2F9DAA72C28352F56A60A` |

## 3. 可重现派生目录

`tools/extract-noita-wang-terrain.ps1` 只在显式执行时读取隔离的 Noita 参考副本；正式 build 和 Player 只读取仓库内 `content/noita-wang-terrain.json`。脚本结构化解析 biome XML、`materials.xml`、Lua marker 常量和 PNG 像素，按 STB corner-mode 约束表生成 `PWH1` 语义 tile 数据并 Brotli 压缩。

| 来源 | 锁定值 |
|---|---|
| STB 算法许可证 SHA256 | `d371be3ed0cdc728461e9f053867142bf2d406507a2aad844dec107f6e1dffa0` |
| `data/materials.xml` SHA256 | `122df34514edaf312e1a15a619b3d6a44d49ce605c929d5950c9051a57429d04` |
| coalmine PNG SHA256 | `7e45205c7eb1e7a804e73f1ae7d7c3bbb37436d41f530e769c3921544041c8dc` |
| wizardcave PNG SHA256 | `11b43f3a3d5653ce8529166e9b3d50e62e8a70b78bb48890a15e0d4eb632e268` |
| reference biome bindings | 20 个，严格唯一且完整 |
| 连续两次提取 | catalog SHA256 均为 `BE423B...A60A`，字节一致 |

| set | atlas | n | H/V tiles | decoded | Brotli |
|---|---:|---:|---:|---:|---:|
| coalmine | 348x448 | 13 | 72/72 | 49,267 | 8,455 |
| coalmine-alt | 232x300 | 13 | 32/32 | 21,907 | 4,437 |
| excavationsite | 344x440 | 20 | 32/32 | 51,475 | 9,272 |
| fungicave | 144x235 | 13 | 27/27 | 18,487 | 4,602 |
| fungiforest | 144x235 | 13 | 27/27 | 18,487 | 4,068 |
| snowcave | 440x560 | 26 | 32/32 | 86,803 | 12,468 |
| snowcastle | 232x332 | 13 | 48/48 | 32,851 | 6,338 |
| rainforest | 172x360 | 20 | 32/16 | 38,611 | 7,300 |
| rainforest-open | 172x360 | 20 | 32/16 | 38,611 | 6,677 |
| rainforest-dark | 172x360 | 20 | 32/16 | 38,611 | 7,205 |
| vault | 344x440 | 20 | 32/32 | 51,475 | 8,164 |
| vault-frozen | 344x440 | 20 | 32/32 | 51,475 | 8,380 |
| crypt | 282x342 | 22 | 36/24 | 58,339 | 6,408 |
| wandcave | 264x340 | 15 | 32/32 | 29,075 | 5,106 |
| wizardcave | 282x342 | 22 | 36/24 | 58,339 | 5,906 |

目录加载期拒绝未知字段、错误 build/version、来源路径、SHA、tile 头、constraint 组合、variant offset、语义范围和重复绑定。`vault.xml` 的重复 `limit_background_image` 只在提取器的内存副本中按 Noita loader 兼容规则去重；来源文件 SHA 仍锁定原始字节。

## 4. 运行时合同

- 15 组模板在 `Describe` 阶段一次解压并编译；64x64 chunk 热循环不解压、不解析 JSON、不分配。
- Herringbone horizontal/vertical tile 几何与 corner constraint key 来自 STB 模板；全局 corner color 和 variant 由 `world seed + biome salt + tile coordinate` 决定，跨 chunk 访问顺序无关。
- `main-biome` 和 `side-biome` 根据 70x48 宏图中的 reference biome 精确绑定 `coalmine`、`excavationsite`、`fungicave`、`snowcave`、`snowcastle`、`rainforest*`、`vault*`、`crypt`、`wandcave` 或 `wizardcave`。
- authored pixel scene operation 高于 Wang；marker semantic 当前保持空 cell，避免 marker 色误生成为不可交互前景；`RandomColor black/white` 以同 seed 的确定性二值语义处理。
- 材质色映射到 Demo 的 primary/secondary/loose/structure/hazard/pool palette。它保留地形用途和可交互类别，但尚不是 Noita 全材料 ID 的逐项复刻。

## 5. Clean build 与测试

| 命令/范围 | 结果 |
|---|---|
| `git submodule update --init --recursive` | 四个锁定 native 输入 materialize 成功 |
| `pwsh -NoProfile -File tools/build-native.ps1 -Rid win-x64 -Configuration Release` | Box2D shared/static、FreeType、RmlUi/UI native 成功；exit 0 |
| `dotnet build PixelEngine.sln -c Release --disable-build-servers -m:1` | 0 warning / 0 error；39 个项目完成 restore/build |
| `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-build --no-restore --disable-build-servers -m:1` | 197 passed / 1 explicit native-GL skip / 0 failed；198 total |

TRX：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-d2abcfd6-clean\tests\PixelEngine.Demo.Tests\TestResults\demo008-d2abcfd6-clean.trx`，SHA256 `BA75A88DCD6A2CFB0DF1C44A56CFB5293BE4C8F815E8ABF9483149D707DB53E5`。

新增测试覆盖 15 套模板来源/尺寸/绑定、损坏目录 fail-closed、正负坐标、反向访问、seed 敏感性、模板签名差异、65,536 次热采样分配，以及实际 coalmine/fungicave chunk 与绑定 Wang 开/实掩码至少 90% 的集成相关性。

## 6. Player 与 Game View 画面

### 6.1 clean commit Player

正式 Demo project 从 detached clean commit 启动，`scenes/infinite-sandbox.scene`、RmlUi active backend、`fallback=False`，进程 exit 0。12 帧脚本只负责从主菜单进入 Campaign 和向右移动；截图来自 GL presentation framebuffer。

| artifact | 尺寸/长度 | SHA256 | 运行态 |
|---|---:|---|---|
| `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-d2abcfd6-clean-evidence\spawn-wang-clean-12.bmp` | 1080x720x32；3,110,454 bytes | `ED6C00900090198EA6718BE93C2360DEDD2BF8239F1C6333EF51BF06D362C8E1` | 玩家中心 `(236.58,142.24)`；出生山体固定轮廓；视口下缘显示 Mines Wang 细节 |

### 6.2 临时 Play 深层视觉补充

下列两张图通过 `pixelengine-editor` 的公开 `runtime.component.field.set`、`game.ui.action.invoke` 和 `game.capture` capability 在 TemporarySnapshot 中定位玩家；artifact 均由 Server 和 Client 双重校验。该次 Editor 使用当前主工作区，Demo Wang 文件与 `d2abcfd6` 相同，但同时存在未提交、与本任务无关的 Editor/Hosting 改动，因此它们只证明深层视觉，不冒充 clean commit 证据。

| reference 区域 | 玩家中心 | artifact | SHA256 |
|---|---:|---|---|
| coalmine / Mines | `(3,706)` | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-wang-evidence\mines-wang.bmp`；1280x720x32；3,686,454 bytes | `0DEB9FA60799B4AAEE4563C349C039A616ABE97034C61D346F81CF447B812E56` |
| fungicave / Coal Pits 西侧 Fungal Caverns 宏格 | `(-3325,2022)` | `C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-wang-evidence\fungal-caverns-wang.bmp`；1280x720x32；3,686,454 bytes | `6448F5C750AAB0A46FCEC119D705584EB591B01DEAC3B7824FAAD180616AC90C` |

第二张 HUD 按纵深显示 `Coal Pits`，而宏图 reference biome 是左侧 `fungicave`；这正是二维宏图侧区覆盖一维纵深名称的预期结果。

## 7. Clean benchmark

命令：

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release --no-build --no-restore -- --filter "*InfiniteTerrainChunkGenerationBenchmarks*" --job short --inProcess --artifacts <temp-output>
```

BenchmarkDotNet `0.15.8`，`.NET 10.0.8`，InProcess ShortRun，3 warmup + 3 measured iteration。报告：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-demo008-d2abcfd6-benchmark-clean\results\PixelEngine.Benchmarks.InfiniteTerrainChunkGenerationBenchmarks-report-github.md`，SHA256 `1B71BEE003776D7A59F1D18DDD16F38DC681912BDA2A134A3DF5C2B49091CC97`。

| 位置 | Mean | Allocated |
|---|---:|---:|
| SurfaceWest | `73.42 us` | `0 B` |
| SurfaceOrigin | `224.31 us` | `1 B` |
| SurfaceEast | `657.33 us` | `7 B` |
| MinesDeep | `308.21 us` | `3 B` |
| FungalCaverns | `369.04 us` | `3 B` |
| PortalHolyMountain | `198.18 us` | `0 B` |
| LaboratoryDeep | `103.06 us` | `0 B` |

`0-7 B` 是 MemoryDiagnoser 的单次诊断舍入噪声；独立 allocation gate 对实际热循环仍要求 `<=1024 B`/多次批量调用。与前一检查点相比，Mines 从 `491.44 us` 降至 `308.21 us`，没有因 Wang 采样产生性能回退。

## 8. 未完成边界

`DEMO-008` 继续 active，下一节点从 reference biome XML 的 `BitmapCaves` 开始：解析 cave/blob/mountain/structure 参数并与 Wang 结果组合；随后接背景图层、marker 对应实体/结构、剩余 fixed/random pixel scene、地图生态与同 seed 全区域截图矩阵。要宣称数值 seed 与 Noita 同图，还必须锁定 Noita world seed 到内部 RNG 调用序列，而不是只证明模板库、约束连接和宏图身份相同。
