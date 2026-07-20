# DEMO-006 确定性流式无限沙盒完成证据

taskIds: `DEMO-006`
implementationCommit: `34973b16a7384846766c166297d3b84e9d596ab1`
runSessionId: `local-20260720-demo006-final-34973b16`
evidenceState: `complete`

## 结论

默认 Showcase Demo 已从固定横向闯关改为确定性、按 chunk 动态生成的无限沙盒。默认入口是 `scenes/infinite-sandbox.scene`，场景声明 `PixelEngine.Demo.PlayableWorldDirector` 作为流式程序化世界生成器；玩家和相机可越过正负坐标，世界只维持 active、border 与缓存预算内 chunk，缺失 chunk 才生成，已修改 chunk 卸载后由 region store 优先恢复。

地形生成使用全局坐标 domain warp、multi-octave value noise、ridged mountain、continental hill 与 basin field，生成山脉、丘陵、盆地、湖泊、分层土壤、洞穴、矿点和深层熔岩。原点附近保留确定性安全出生区，但不把整张世界压平成固定地图。默认场景不再装配 `GoalTrigger`、`MissionDirector`、`LevelDirector` 或胜利结算流；旧 `lava-mine.scene` 与旧任务组件只作为显式兼容机制测试保留。

## 提交节点

| Commit | 节点 |
|---|---|
| `657bfb23` | canonical task、产品目标、架构与程序化无限世界合同 |
| `6ec3ac69` | 缺失 chunk 初始化公开 API、流式 procedural Hosting 装配、负坐标与存档优先测试 |
| `f502a34c` | Demo 自然地形、无限相机、沙盒 UI、默认场景和 benchmark |
| `700ec2b8` | Editor 动态脚本装配顺序、authoring residency、active/border 模拟边界、粒子与温度长跑修复 |
| `34973b16` | 玩家构建、制品审计、快速/正式输出统一使用无限沙盒默认入口 |

## 世界合同

| 合同 | 结果 |
|---|---|
| extent / seed | `ProceduralWorldExtent.Infinite`；seed `0x504958454C534248` |
| persistence key | `showcase-infinite-sandbox-v1` |
| 初始焦点 / 出生区 | focus `(0,208)`；安全地表 `Y=224`；玩家出生 `(0,192)` |
| 坐标 | chunk 生成只依赖 seed、全局坐标和材质查询；负坐标使用 floor chunk addressing |
| 接缝 | `SurfaceYAt(worldX)` 与洞穴采样使用全局坐标，64x64 chunk 边界不重置 noise |
| 流送 | `WorldManager` 同时提供 resident source 与 active-state gate；border/cached 默认不调度 CA |
| KeepAlive | active 边缘写入留下 current/working/incoming dirty；下一帧相位 2 在清理 border 前提升目标并扩出新 border |
| 持久化 | region store 命中优先于生成器；正负方向平移、卸载、重入后玩家修改保留 |
| 常驻预算 | 双向长距离流送测试持续断言 resident bytes 和 chunk count 不随距离无界增长 |

对 `X=-32768..32768`、步长 64 的 1025 个地表列做确定性扫描：最高山脊为 `Y=81 @ X=-14528`，最深盆地为 `Y=281 @ X=25536`，地貌高差 200 cells；每列从地表下 24 到 280 cells 按 16 cells 采样，共命中 91 个洞穴样本。重复调用返回完全相同结果。

关键自动化用例包括：

- `DefaultPlayableWorldBuildsDeterministicInfiniteNaturalTerrain`
- `InfiniteSandboxStreamsBothDirectionsWithinBudgetAndPersistsEdits`
- `AttachCurrentSceneWorldBuildsStreamingProceduralWorldAcrossNegativeCoordinates`
- `SceneFileCanAttachDeclaredStreamingProceduralWorld`
- `ActiveEdgeKeepAlivePromotesBorderAndLoadsOuterRing`
- `ResolveDepositsDefersWhenDirtyHaloIsNotResident`
- `TemperaturePassesKeepUnrelatedInactiveResidentChunksFrozen`
- `DemoDefaultHudAndResultTextDescribesInfiniteSandboxWithoutVictoryGoal`

## 构建与测试

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore` | 当前实现提交 `34973b16`：0 warning / 0 error |
| Simulation 全量 | 207 passed / 0 skipped / 0 failed |
| World 全量 | 46 passed / 0 skipped / 0 failed |
| Demo 全量 | 156 passed / 1 显式 native GL 条件 skipped / 0 failed |
| Hosting 全量 | 976 passed / 7 显式环境条件 skipped / 0 failed |
| 发布入口锁定测试 | 1 passed / 0 skipped / 0 failed；PowerShell 4 个脚本与 Bash 2 个脚本语法通过 |
| task catalog | valid；完成前为 84 total / 52 done / 4 open / 1 active / 27 blocked |

Hosting 的 7 条 skip 是已有的 native GL、外部拖放或缺失物理环境条件；DEMO-006 的默认场景、流送、负坐标、自然地形、无胜利流和 Editor 动态脚本装配用例均实际执行，没有用 skip 关闭任务。

## 地形生成性能

命令：

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj `
  -c Release --no-build -- `
  --filter "*InfiniteTerrainChunkGenerationBenchmarks*" --job short --inProcess
```

环境为 AMD Ryzen 7 5800X、.NET 10.0.8、X64 RyuJIT x86-64-v3、AVX2、BenchmarkDotNet 0.15.8。每次操作生成一个真实 64x64 chunk 与 16x16 temperature block，材质目录在 setup 阶段加载，不计入样本。

| 代表区域 | ChunkX | Mean | StdDev | Allocated |
|---|---:|---:|---:|---:|
| 西侧盆地 | -214 | 562.29 us | 19.891 us | 0 B |
| 原点安全区 | 0 | 90.14 us | 5.606 us | 1 B |
| 东侧山脉 | 123 | 286.68 us | 8.818 us | 3 B |

这是 `ShortRun` 回归样本，不冒充目标硬件长稳态性能认证；它证明 chunk 生成没有随调用产生有意义的托管堆分配，并保留可复跑入口。

## Editor CLI 真实运行

来源实现提交为 `700ec2b8e965606c44641ba45e7fc394a9932b14`；其后 `34973b16` 只修改发布脚本、审计默认值、文档和锁定测试，不改变 Editor/Demo runtime 二进制逻辑。

| 字段 | 值 |
|---|---|
| Editor process / instance | PID `52548` / `c43961ac08d144ddaa175aaf51e7763e` |
| Play session | `7e3af883476443b7986e895032648c68` |
| project / scene | `PixelEngine Demo` / `scenes/infinite-sandbox.scene`；scene clean |
| capability matrix | 172 capabilities / 329 UI commands；matrix digest `e70c5275bd56501b0934049e6d59478adf81c159e0bf82f8de54af5bb3011f10` |
| runtime | 5 entities；`CameraFollow.ClampToBounds=False`；legacy goal/mission/level component count `0` |
| negative-coordinate inspection | world cell `(-64,224)` resolved to chunk `(-1,3)` and material `dirt` |
| Play stability | frame advanced past 51,000 editor frames；runtime snapshot about 150.55 FPS、p99 11.44 ms；Console 0 warnings / 0 errors |
| lifecycle | Play, Pause, Step, Stop all succeeded；Stop restored temporary snapshot `tick=0, chunks=504` |
| exit | `workspace.exit` succeeded；process exited；stderr 0 bytes |

全部操作通过 `pixelengine-editor` 公共 CLI 完成，没有使用 MCP、Computer Use、OCR、屏幕坐标或 `--scripted-*`。真实制品由 Server 与本地重新计算 SHA256 双重校验：

| Artifact | 尺寸 / bytes | SHA256 |
|---|---:|---|
| Scene View authoring preview | 652x590 / 1,538,774 | `a45b6dc3346a7ff1178c314ef7b262adcb9e01c723e7d57d25de2b113f60bd89` |
| Game View Play capture | 1280x720 / 3,686,454 | `40ce6d052567eaab1fc3ff952f9171d0ddcd6eaf7980f098130b3ddae83232ac` |

Scene View 显示 720x480 authoritative cell-world preview、出生点、起伏地表、浅水、土层、洞穴和矿点；Game View 显示 `PixelEngine 无限沙盒` 主菜单以及实时生成的地形背景。

## 最终输出

`tools/update-final-output-fast.ps1 -Rid win-x64 -Configuration Release -DemoRuntimeUiBackend RmlUi` 从干净 tracked worktree `34973b16` 原子更新 `最终输出/`。玩家包 `content/startup.json` 为 `scenes/infinite-sandbox.scene`、`RmlUi`、`Production`，并且打包的 `content/scenes/` 只包含 `infinite-sandbox.scene`。

| 文件 | Bytes | SHA256 |
|---|---:|---|
| `最终输出/编辑器/PixelEngine.exe` | 177,152 | `7520d8b888ea29aaaadeaaddfc7b042385818dbfa01961eafdd15c2ea96aa30b` |
| `最终输出/游戏Demo/PixelEngine Demo.exe` | 162,304 | `057223850dd9d7273b38aee33013c89059a7274a9d947ee2f1d63e592afb9023` |
| `最终输出/游戏Demo/content/startup.json` | 255 | `5e6ec639f8c0cc74da147afe32ace0d6fee6ec9ac9a23f4e3002eea6f7299307` |
| `最终输出/安装器/PixelEngine-Setup-0.1.0-win-x64.msi` | 73,795,967 | `41016527d8024ab95bd30dc6f194557f624cea7cf8a9b65f27a93e174ec97e2c` |

MSI 静态 verifier 返回 `ok=True`、277 files、2 shortcuts。最终玩家包本身又独立运行两次：120-frame 运行写出 1080x720、32-bit、3,110,454-byte BMP，SHA256 `5b32f6bb36e6fc7119bc4725271a3992895cf795fa46b1fd5622a493edfc826e`；第二次 30-frame `Start-Process -Wait` 返回 exit code 0。

快速输出 manifest 按合同保持 `verified=false`，因为它不运行整套 release verifier；本报告不把它冒充 `REL-002/REL-003` 正式发行证据。DEMO-006 的验收由当前提交 Release build、四个相关测试项目、真实 Editor CLI Play、双重校验截图和最终玩家包 exit 0 共同闭合。正式签名、确定性复构、GitHub Release、外部硬件和物理点击 reviewer 仍由各自 release/UI canonical task 跟踪。
