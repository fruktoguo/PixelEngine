# DEMO-008 Snowcastle 随机 Pixel Scene 检查点

## 结果

提交 `129ce294` 将 Noita Build `17130612` 的 Snowcastle Wang 内建红/黄 marker 转为纯 C# 随机场景组合，不在玩家包中执行 Lua。`load_pixel_scene` 的 4 个 130x260 场景与 `load_pixel_scene2` 的 8 个 260x130 场景均保留来源权重；material 掩码写入 CPU 权威网格，background/visual 通过视口动态视觉层合成，旧紫色 SceneLoad 占位被抑制。

生成目录锁定 `snowcastle.lua` 与 `director_helpers.lua` SHA256，包含 12 个场景、25 种稳定材质、10 张 background/visual 资产。运行时材质目录由 62 增至 77 项，纹理目录由 28 增至 36 项；地形生成语义变化后 persistence key 升级到 `showcase-campaign-v14`。

## 验证

- Demo 定向测试：10 passed，0 failed。覆盖来源目录、权重分组、视口图层、占位抑制、25 种材质编译、权威 chunk 落料与重复生成逐 cell 一致。
- Hosting 定向测试：1 passed，0 failed。静态与视口动态 world visual layer 均按 runtime camera 映射。
- Editor Shell Release build：0 warning，0 error。
- `git diff --check`：通过。
- BenchmarkDotNet：`SnowcastlePixelScene` 为 `2.603 ms/chunk`，StdDev `0.0319 ms`，MemoryDiagnoser `26 B`；完整八场景报告 SHA256 为 `775D0E3D6AFD4B74278D4FA11E0FDE4F6C67D3336E771B71A1EB59757364926D`。

## 实机边界

PixelEngine Editor 实例 `73787dea563240b7934a06c5b198114b` 成功进入 TemporarySnapshot、启动 Campaign 并生成经服务端与客户端双重校验的 1280x720 framebuffer，Editor/Play 生命周期正常。该轮运行时字段修改发生在角色初始化之后，玩家仍位于出生山体，截图没有展示 Snowcastle 随机场景，因此不作为本节点视觉 parity 证据。停止 Play 后临时快照已恢复，Editor 已通过 `workspace.exit` 关闭，工程文件未保存探针字段。

## 未完成边界

`DEMO-008` 保持进行中。当前只闭合 Snowcastle 两张来源随机表；其他 biome 的 random/spliced Pixel Scene、vegetation、真实敌人/道具/陷阱 marker、Noita RNG 调用序列、深层真实 framebuffer 与全区域长路线仍未完成。当前 benchmark 的 26 B 也需要后续定位并恢复稳态零分配目标。
