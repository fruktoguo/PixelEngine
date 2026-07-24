# DEMO-008 Noita Biome Background 检查点

日期：2026-07-25
任务：`DEMO-008`（保持 `[~]`）
参考：Noita Build `17130612`

## 实现范围

- `tools/generate-noita-biome-backgrounds.ps1` 从 129 个 reference biome 的 `Topology` 首标签提取 background、四向 edge、neighbor 继承和 edge priority；来源 XML 含非法注释与重复属性，因此生成器只解析背景所需的受限属性，不在玩家运行时解释 XML/Lua。
- 111 张去重 weather/background/edge PNG 进入 Demo 自有内容包；`noita-biome-backgrounds.json` 逐文件记录 source/content SHA256、尺寸、Build 与 topology version hash，正式 Player 不读取本机 Noita。
- `NoitaBiomeBackgroundCatalog` 与生成的纯 C# 定义按 reference biome id 查询；视口按 512-cell topology macro cell 平铺原尺寸背景，并依据相邻 biome 与 edge priority 合成四向边缘。
- `PlayableWorldDirector` 正式实现 `IViewportWorldVisualLayerProvider` 并转发给世界生成器，修复此前 Snowcastle、植被和 biome background 只在直接生成器测试中可见、真实 Player 未经过动态视觉层的接线缺口。
- 来源 Vegetation marker 混合 Verlet chain、刚体、敌人、产怪建筑和静态植物；在逐类 C# 实现前抑制旧通用绿色占位，避免以错误图标冒充交互生态。

## 定向验证

```text
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore \
  --filter "FullyQualifiedName~NoitaBiomeBackgroundTests|...DynamicVegetationMarkers...|...WangMarkerVisualProfile...|...WangMarkerAnchorsAreDeterministic" \
  -p:TreatWarningsAsErrors=true

Passed: 4, Failed: 0, Skipped: 0
```

覆盖 129/111 目录与逐文件 hash、煤矿 `background_coalmine.png` 视口组合、同查询逐描述符确定性、动态 provider 正式转发、256 次 warmup 后视口收集零托管分配，以及错误 vegetation marker 占位抑制。

```text
dotnet build PixelEngine.sln -c Release --no-restore -p:TreatWarningsAsErrors=true

Warnings: 0, Errors: 0
```

真实 Player 以煤矿出生点自动进入 Campaign，运行 120 帧并写出 `artifacts/noita-coalmine-background.bmp`；RmlUi 原生后端有效、无 fallback，截图确认 weather background 经过正式 Player 动态视口链路合成。

## 未完成边界

真实帧同时证明当前地形仍呈现整张缩略图式的过密碎片：相机内出现的 Wang/pixel-scene 结构数量和尺度明显不符合 Noita 的局部视口。该缺陷不是 background 资产尺寸造成，下一节点必须校准 topology/Wang 世界坐标、相机 pixels-per-cell 与 scene 激活范围，取得同 seed 局部区域对照截图后才能宣称地图尺度正确。

此外，background parallax/雾化、完整 weather 特效、动态 Vegetation marker 的 Verlet/刚体/敌人/建筑行为和全部 biome 截图矩阵仍未闭合，`DEMO-008` 保持 `[~]`。
