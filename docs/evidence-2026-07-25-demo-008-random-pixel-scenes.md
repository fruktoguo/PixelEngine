# DEMO-008 全 biome 随机 Pixel Scene 运行时证据

## 范围

- 参考身份：Noita Steam Build `17130612`，version hash `9dbd52ced019a643169a2db02f46c77f8766c6e5`。
- 运行时不读取 Lua、本机 Noita 安装或外部二进制资产。
- 本节点只证明随机 Pixel Scene 的目录、材质、确定性选择、权威 chunk 写入和视口视觉接线；不证明完整地图视觉 parity。

## 产物

- `noita-random-pixel-scenes.json`：19 个来源脚本、87 个表、319 个条目、195/117/66 份 material/visual/background 来源记录。
- `NoitaRandomPixelScenes.Generated.cs`：319 份 Brotli byte mask、73 种材质、2 个 `color_material` 覆写码，最大场景 `520x520`。
- `maps/noita/random-pixel-scenes/`：183 份去重的 Demo 自持视觉资产及 `provenance.json`。
- `materials.json`：150 个运行时材质；Noita fire cell 映射到引擎已有 `Emissive` 渲染风格，材质包可实际加载。

## 运行时语义

- Wang 内建红、黄、青、绿、蓝 marker 分别解析为 `load_pixel_scene` 至 `load_pixel_scene5`。
- Lua 表的 `_` biome id 与 topology 的 `-` id 显式规范化，不依赖模糊子串匹配。
- 同 seed、biome、函数和世界锚点稳定选择同一表与同一场景。
- `is_unique` 场景在完整 topology 候选中按稳定 hash 选唯一胜者，结果不依赖 chunk 加载顺序；没有候选时不强行生成。
- material mask 在固定全局 Pixel Scene 之后覆盖权威 cell，visual/background 分别进入 decoration/background 视口层。

## 验证

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaRandomPixelSceneTests"
```

结果：`5 passed / 0 failed`。覆盖目录与资产 SHA256、掩码和颜色覆写、biome/marker 绑定、唯一场景、权威 chunk 写入与视口 visual/background。

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaRandomPixelSceneTests|FullyQualifiedName~NoitaWorldContentCatalogTests"
```

结果：`14 passed / 0 failed`。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore
```

结果：`0 warning / 0 error`。

## 未完成

- 尚未取得同 seed 全区域真实 Player 截图矩阵，不能宣称视觉 parity。
- 随机场景 marker 中的敌人、道具、机关、商店等仍需逐函数升级为专属 C# 交互实体。
- Noita 原始 RNG 调用序列尚未逐调用对照；当前保证 PixelEngine 内部确定性，不冒充逐位相同。
