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

提交 `db9063b6` 构建后的 PixelEngine Editor 实例 `caaa633a3a204a7d8c73536559164dd8`
在真实 Play 会话中将玩家移动至 forge 区域。运行态实体列表确认：

- 玩家中心：`(1503,6006)`
- marker entity：`NoitaWangMarkerProp`
- function：`spawn_forge_check`
- marker world position：`(1537,6042)`

随后捕获并完成服务端/本地 SHA256 校验：

- framebuffer：`D:\Temp\PixelEngineGoalNoita\artifacts\caaa633a3a204a7d8c73536559164dd8\0d5e5f761493459f9c8c1d9d1162ec51\97a5da0e94684991b50302fc0b6047e1.bmp`
- size：`1280x720`
- SHA256：`b741964aed05f61af6f87f27e324d2272477ea69ef8b79a878c9cf44e9585ec4`
- Console：0 log / 0 warning / 0 error
- runtime `WorldVisualLayerCount`：32

画面证明 forge background/colors、material scene 与 `spawn_forge_check` marker 使用同一世界坐标并进入
真实 Player。marker 当前呈现为通用青色 Machine/VFX，外围地形的材质观感、洞穴密度也仍与参考目标有
明显差距，因此本证据只证明完整接线，不宣称地图视觉或交互 parity 完成，`DEMO-008` 继续保持进行中。
