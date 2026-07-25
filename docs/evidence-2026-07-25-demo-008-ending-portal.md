# DEMO-008 ending Portal 液体门控证据

## 来源

- `scripts/biomes/temple_wall_ending.lua`：SHA256 `40ddf06749bdad3cecc9beb3a57cc25ccc204b01bfaf18d4083a14f838fc4a83`。
- `entities/buildings/teleport_ending.xml`：SHA256 `8d12b8e6b0a1471b6f192b0216a55ab4f32cea5854667b0e7fd384cfc3c787fb`。
- `scripts/buildings/teleport_liquid_check.lua`：SHA256 `b50bcee8db2fda6b569db97fae5dd12b98d6419d179a5599f1e8686a598bb9ee`。
- 参考身份：Noita Build `17130612`，version hash `9dbd52ced019a643169a2db02f46c77f8766c6e5`。

## 已实现

- ending Portal 保留来源实体 `y-4` 偏移、目标 `(1891,280)` 与 30x30 hitbox。
- Portal 下方相对 `x=-2..2 / y=136..140` 的 5x5 区域按权威 cell 读取；stable 或 unstable teleportatium 任一存在时才启用 Portal。
- 检查周期为 1 秒，对应来源 `update_every_x_frame=60` 的 60 Hz 语义。
- Noita 材料生成器新增交互系统必需材料输入，unstable teleportatium 从同一材料目录继承链生成真实运行时属性和纹理 provenance。
- 玩家包材料目录由 150 增至 151，来源纹理由 77 增至 78；后续材料扩充由生成元数据验证，不再硬编码历史总数。

## 验证

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaMarkerPortalTests|FullyQualifiedName~NoitaMaterialCatalogTests|FullyQualifiedName~NoitaWangTerrainCatalogTests.ExactWangMaterialsArePresentInRuntimeMaterialCatalog|FullyQualifiedName~NoitaWangTerrainCatalogTests.ExactWangMaterialTexturesArePackagedWithVerifiedProvenance"
```

结果：`13 passed / 0 failed`。覆盖六组静态 Portal、ending entity 偏移、动态目标 fail-closed、30x30 hitbox、5x5 teleportatium 门控、材料存在性及纹理 provenance。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore
```

结果：`0 warning / 0 error`。

## 未完成

- 本节点尚未取得真实 Player 注液、Portal 启用和跨区传送画面。
- Portal 旋涡 sprite 与来源音频 bank 尚未逐资产复刻。
