# DEMO-008 Noita VegetationComponent 检查点

日期：2026-07-25  
任务：`DEMO-008`（保持 `[~]`）  
参考：Noita Build `17130612`

## 实现范围

- `tools/extract-noita-wak.ps1` 以边界检查和路径穿越防护读取 `data.wak`，本机参考树共解出 14,745 项；正式仓库和玩家运行时不读取本机 Noita。
- `tools/generate-noita-vegetation.ps1` 从世界内容目录和隔离参考树生成 317 层、105 张成熟 sprite、12 种材质的 `noita-vegetation.json`、Demo 自有 PNG 和 `NoitaVegetation.Generated.cs`。XML 动画只在离线生成阶段解析，玩家运行时不执行 Lua/XML。
- 运行时解码 Brotli alpha mask 并校验 SHA256；植被只按稳定字符串材质名编译为运行时 id，不把数值 id 入盘。
- 全局 seed/grid、layer random seed、reference biome id 和真实 Wang/BitmapCaves/Pixel Scene 空实边界共同决定落点。跨 chunk 重复计算得到相同 placement，加载顺序不影响结果。
- `is_visual=false` 层仅向空 cell 写入权威材质；`is_visual=true` 层按原 sprite pivot、visual offset、extra Y 与 tint 进入固定容量视口视觉层。Snowcastle authored Pixel Scene 仍最后覆盖。
- 地形算法改变后存档身份升级为 `showcase-campaign-v15`。Demo 增加直接 `StbImageSharp` 运行时引用，修复真实 Player 初始化 PNG provider 时缺失程序集的问题。

## 定向验证

```text
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore \
  --filter "FullyQualifiedName~NoitaVegetationTests|...ExactWangMaterials...|...ExactWangMaterialTextures...|...GeneratorAppliesBitmapCaves..."

Passed: 7, Failed: 0, Skipped: 0
```

覆盖目录/资产 provenance、105 个 mask 解码、12 种材质解析、煤矿 `fungus_loose` 权威 cell、同 seed 逐 cell 确定性、真实 vegetation PNG 视口层，以及 Wang/BitmapCaves 与材质纹理数量回归。

隔离 BenchmarkDotNet 短跑（1 warmup / 3 iterations）完成 8 个场景，MemoryDiagnoser 未报告托管分配：

| 场景 | Mean |
| --- | ---: |
| SurfaceWest | 1.307 ms/chunk |
| SurfaceOrigin | 1.865 ms/chunk |
| SurfaceEast | 1.987 ms/chunk |
| MinesDeep | 1.643 ms/chunk |
| FungalCaverns | 2.644 ms/chunk |
| SnowcastlePixelScene | 2.350 ms/chunk |
| PortalAndHolyMountain | 1.486 ms/chunk |
| LaboratoryDeep | 1.284 ms/chunk |

报告：`artifacts/vegetation-benchmark/results/PixelEngine.Benchmarks.InfiniteTerrainChunkGenerationBenchmarks-report-github.md`（本地 ignored artifact，不作为仓库静态伪证据）。

真实 Player 使用 RmlUi、86 材质、128 反应运行 180 帧并写出 `artifacts/noita-vegetation-gameplay.bmp`；窗口后端无 fallback、GC collection 为 0。截图可见默认 Campaign 地表的植被视觉与权威地形共同组合。产品“新游戏”流程会重建默认 Campaign，因此本帧不宣称已定位煤矿；煤矿权威植被由上述 chunk 测试覆盖。

## 未完成边界

本节点不把 317 层数据接线冒充完整生态 parity。marker vegetation 实体行为、生长/燃烧/掉落等逐实体交互、全部深层 biome 的同 seed 截图矩阵、原版 RNG 序列和完整长路线仍未闭合，`DEMO-008` 保持 `[~]`。
