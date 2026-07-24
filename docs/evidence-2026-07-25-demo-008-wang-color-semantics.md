# DEMO-008 Noita Wang 颜色语义纠偏检查点

日期：2026-07-25
任务：`DEMO-008`（保持 `[~]`）
参考：Noita Build `17130612`

## 根因与修复

- 实机煤矿帧中大量高亮白色碎条不是相机放大倍率导致。对照 `data/wang_tiles/coalmine.png` 与生成目录后确认，离线生成器先匹配普通 `Graphics color`，把 Wang 基础实心色 `ffffffff` 错误解析成 `trailer_text` 材料。
- `tools/extract-noita-wang-terrain.ps1` 现在分离显式 `wang_color` 与普通 Graphics alias。Wang atlas 的解析优先级为精确 `wang_color`、灰度基础拓扑、非灰度 Graphics fallback；BitmapCaves 单独允许灰度 Graphics fallback，因此 dangerroom 的 `505050 -> coal` 语义保持不变。
- 15 组 Wang/BitmapCaves 目录重新生成。精确运行时 Wang 材料由错误的 41 种收敛为 36 种；总运行时材质仍为 86，统一生成器重排并验证 37 张实际需要的材质纹理。
- 新增回归断言，禁止 `trailer_text` 或 `ffffffff` 再进入 Wang material mapping。

## 定向验证

```text
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore \
  --filter "FullyQualifiedName~NoitaWangTerrainCatalogTests|FullyQualifiedName~NoitaBitmapCavesTests|...ExactWang...|...GeneratorAppliesBitmapCaves...|...WangMarkerAnchorsAreDeterministic" \
  -p:TreatWarningsAsErrors=true

Passed: 10, Failed: 0, Skipped: 0
```

```text
dotnet build PixelEngine.sln -c Release --no-restore -p:TreatWarningsAsErrors=true

Warnings: 0, Errors: 0
```

真实 Player 在同一煤矿出生点运行 120 帧并写出 `artifacts/noita-wang-material-fixed.bmp`。修复后原高亮白色碎条恢复为灰岩、煤与木结构，RmlUi 原生后端有效，无 fallback。

## 未完成边界

Noita 来源同时声明单个 biome 宏格为 `512x512` world cells，而煤矿 `wang_map_width/height` 为 `256x256`，并在同一 biome XML 中提供多层 `<Materials>` 噪声与范围参数。当前实现仍把 Wang 语义直接当最终逐 cell 材料，没有复原“低频拓扑生成后由材料层填充 512-cell 世界细节”的完整链路；因此洞穴仍偏碎、偏密。

下一节点必须提取全部 biome 的 wang map 尺寸与 MaterialComponent 参数，在 C# 中实现确定性、零分配的拓扑到最终材料填充，并用同 seed 局部煤矿截图验证。不得用简单 20 倍最近邻放大或相机缩放冒充地形 parity。
