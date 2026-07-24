# DEMO-008 全局 Pixel Scene 接线证据

## 结果

Build `17130612` 的全局 buffered Pixel Scene 已分为三条独立链：30 张 material PNG 转为权威
cell 掩码，6 张 background 与 22 张 colors 作为非权威世界视觉层，43 个非材质 marker 像素转为
纯 C# 世界锚点。正式 Player 不读取 Noita 安装、XML 或 Lua，也不包含 Lua VM。

## 参考身份

- build id：`17130612`
- version hash：`9dbd52ced019a643169a2db02f46c77f8766c6e5`
- 隔离数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`
- 运行时地形比例：`1 source pixel = 1 world cell`

## 快速验证

- `GlobalPixelSceneMarkersPreserveEveryUnresolvedMaterialPixel`：43/43 marker 与掩码计数一致。
- `ForgePixelSceneMarkerResolvesToPureCSharpSpawnAnchor`：forge marker 为世界坐标 `(1537,6042)`、
  ARGB `ff4cacab`、函数 `spawn_forge_check`、来源 `data/scripts/biomes/snowcastle.lua`。
- Demo targeted test：2 passed，0 failed。

## 真实 Player

PixelEngine Editor 实例 `02cd560ff13c48c7b415d607050554ec` 在真实 Play 会话中将玩家移动至
forge 区域，捕获并完成服务端/本地 SHA256 校验：

- framebuffer：`D:\Temp\PixelEngineGoalNoita\artifacts\02cd560ff13c48c7b415d607050554ec\ed43ab8567604665b52917c890acfae1\56823248991b45f1811c9aded1516585.bmp`
- size：`1280x720`
- SHA256：`f96e9f6f5d625ef95e002b7749cb3e333300dfb3d3e4c30a406a2daaa2b417de`
- Console：0 log / 0 warning / 0 error
- runtime `WorldVisualLayerCount`：32

画面证明 forge background/colors 与 material scene 使用同一世界坐标并进入真实 Player。外围地形的
材质观感、洞穴密度以及 marker 对应的真实交互仍与参考目标有明显差距，因此本证据不宣称地图视觉
parity 完成，`DEMO-008` 继续保持进行中。
