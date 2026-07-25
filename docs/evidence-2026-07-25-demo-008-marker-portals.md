# DEMO-008 静态目标 Portal marker 证据

## 来源与实现

- 参考数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`，Build `17130612`。
- `spawn_teleport_back`：`lake.lua` -> `teleport_bunker_back.xml`，目标 `(-12557, 190)`。
- `spawn_buried_eye_teleporter`：`snowcave.lua` -> `teleport_snowcave_buried_eye.xml`，目标 `(3895, 4510)`。
- `spawn_teleporter` 按 marker origin 区分 meditation cube、hourglass 和 snowcave secret return，目标分别为 `(190, 1525)`、`(190, 5231)`、`(190, 3080)`。
- Demo 侧纯 C# `NoitaMarkerPortal` 使用来源 `TeleportComponent` 的 30x30 hitbox，首次进入时调用公开玩家传送 API，并绘制 overlay、点光、粒子和音频反馈。
- `spawn_teleport` 的机器人蛋 return 由 `teleport_robot_egg_return.lua` 动态改写目标；在 Portal 网络状态接入前显式 fail-closed。

## 来源 SHA256

| 文件 | SHA256 |
|---|---|
| `scripts/biomes/lake.lua` | `744c1fd12dd66810ef19b2b4b91259c83bda1c8f07fc6c5bf32256b70ff64e31` |
| `scripts/biomes/snowcave.lua` | `efd7b821af7c9784430ad2e7dd011e4a52578246c7750f8251a1c8310b801b66` |
| `scripts/biomes/excavationsite_cube_chamber.lua` | `805fef671c1a338a2c376f20c9bb536e5727887262d54fba04abab5c8d5e227c` |
| `scripts/biomes/snowcastle_hourglass_chamber.lua` | `d1d455f4b33689610f2326acc9adc541b2a367af3af5860d38f5d5080cfd5f01` |
| `scripts/biomes/snowcave_secret_chamber.lua` | `9033d8e02959c2198b2b1e8cb49fa9ac918c9f2cd4013c3843983691ad0b71bd` |
| `entities/buildings/teleport_bunker_back.xml` | `96dd4b9d0f8fc0afb8dc3e1f71208a056cfee5a64122284cbaf246394a361085` |
| `entities/buildings/teleport_snowcave_buried_eye.xml` | `8631f79608c81168fbe7417d3796dcb8d4a22b96da985eb4418e101985c5c862` |
| `entities/buildings/teleport_meditation_cube_return.xml` | `c60fc867f1efe345005d31c4b95698bef18d0458aec0bf94027969e684e01870` |
| `entities/buildings/teleport_hourglass_return.xml` | `d27a70399d6f9202f09ccb834b2137bb360af30e1ccb2069aa45acd20e45cb8f` |
| `entities/buildings/teleport_snowcave_buried_eye_return.xml` | `ebdbb913c305c70077c0ef468be476d4cf975e862b613b028bd4c17e0b17a3a3` |

## 验证

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NoitaMarkerPortalTests|FullyQualifiedName~NoitaMarkerHazardTests|FullyQualifiedName~NoitaMarkerLootTests|FullyQualifiedName~NoitaMarkerEnemyTests"
```

结果：`30 passed / 0 failed`。覆盖五组静态目的地、30x30 触发范围、动态目标 fail-closed 及相邻 marker 分支。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore
```

结果：`0 warning / 0 error`。

## 未完成

- 尚未建立动态入口位置状态，因此 robot egg return 不启用。
- meditation cube 来源包含液体启用条件，当前尚未读取对应局部材料状态。
- 本节点没有真实 Player 传送画面，不能作为最终 Portal parity 证据。
