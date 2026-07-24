# DEMO-008 Noita Wang 精确材质与深层驻留证据

## 结果

提交 `e48a8114` 将 Build `17130612` 的 15 组 Wang/BitmapCaves 从六类代表材质升级为逐组精确材质槽。当前模板实际使用的 41 个稳定材质名全部存在于 Demo 自有 `materials.json`，Player 不读取本机 Noita 安装，也不执行 Lua。

本检查点同时修复深层参考捕获暴露的两个流式边界问题：显式玩家出生点现在同步程序化世界 `InitialFocus`；温度相变与脚本 cell 写入在 3x3 驻留邻域未补齐时延后，补环后继续执行，不再越过 border ring 传播 dirty。

## 快速验证

- Demo 地图/出生点定向测试：28 passed，0 failed。
- Simulation 温度定向测试：17 passed，0 failed。
- Scripting 命令提交定向测试：25 passed，0 failed。
- Editor Shell Release build：0 warning，0 error。
- 未运行无关的完整 UI、物理和性能测试套件。

## 真实 Editor

Editor 实例 `a8099fceed0b414cb6f728b22d114ac8` 通过 TemporarySnapshot 将玩家出生点设为 `(1503,6006)`，程序化世界初始焦点同步为 `(1506,6012)`。Play 会话 `38c89b171a7f49caa7a67d469cb37825` 稳定跨过此前 6 秒内必现的崩溃窗口，Console 为 0 log / 0 warning / 0 error。

已完成服务端与本地双重 artifact 校验：

- framebuffer：`D:\Temp\PixelEngineGoalNoitaExact7\artifacts\a8099fceed0b414cb6f728b22d114ac8\2e87b6deb5df4bdf868e6ab445ca2d73\80ea76f120224443abe8e62179d99cfc.bmp`
- size：`1280x720`
- SHA256：`64e2908b06bcae00d1376858159fc83d9b8528f2a0c4943896cb62cb6b519fc5`
- byte length：`3686454`

捕获结束后已停止 Play、Undo 整个临时事务并通过 `workspace.exit` 关闭 Editor，Scene 未保存探针字段。

## 未完成边界

该 framebuffer 证明 Snowcastle Wang 原始像素拓扑、精确材质名、forge Pixel Scene 与深层流式运行已进入真实 Player，但画面仍存在大面积纯白/纯黑。参考 `snowcastle.png` 本身是 Wang tile atlas，而非世界缩略图；Noita 最终观感还依赖材质纹理、颜色随机化、背景、雾化、光照和后处理。当前 Demo 主要使用单色 `baseColor`，因此本证据不宣称视觉 parity 完成。

`DEMO-008` 继续保持 `[~]`。下一节点应优先复现 41 种 Wang 材质的 Graphics 纹理/颜色噪声与 biome 雾化合成，再扩展剩余材料目录、vegetation、真实交互 marker、fixed/random pixel scene 和同 seed 全区域截图矩阵。
