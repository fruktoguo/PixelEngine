# 2026-07-19 EDITOR-003 用户反馈修复：Game 材质色、斜坡伤害与受击反馈

taskIds: `EDITOR-003`
implementationCommit: `d22aeab85f7e7004818763ac96edb99092a1e88e`
implementationPatchId: `0f518a1b2c7687bddcfad48ec817b4c70979c8b0`
runInstanceId: `7af9bbea48ed4fb29f47b2a985f20ff8`
runClientInstanceId: `codex-d22aeab8-evidence`
slopePlaySessionId: `3e4a16e336c84a31997ffb00a0fbd624`
colorPlaySessionId: `2ba1b772df8f4fd7a8f25d376c4914d8`
evidenceState: `local_game_color_damage_feedback_complete_external_dpi_drag_review_blocked`

## 结论

本轮闭合三个用户可复现缺陷：Game View 不再把相位 9 已经是 display-referred sRGB 的材质色当作 linear color 再做一次 gamma；emissive 材质不再把同一份 albedo 重复相加；玩家站在普通斜坡或被静止刚体支撑时不再持续触发伤害，受击也不再向权威 cell world 喷出 ash 粒子。

提交同源 Editor 的代表性熔岩内部像素在 Scene 为 `#D35D15`，Game 为 `#D45D15`，仅红通道相差 1/255，均保持红橙色而不是黄色。默认斜坡长跑 296 个 simulation tick 后仍为 `Health=100`、`DamageEventCount=0`、`RespawnCount=0`、`HazardContactCellCount=0`、`activeParticleCount=0`。一次真实刚体接触伤害则得到 `Health=65`、`DamageEventCount=1`、`DamageFlashRemainingSeconds=0.12`、`activeParticleCount=0`，证明反馈已改为清晰 overlay 闪烁且不污染世界。

`EDITOR-003` 继续保持 `[!]`。不同物理 DPI/200% 显示器跨屏、Explorer→Editor 人工 pointer drag、runtime 数值物理拖拽与独立 reviewer 仍是既有外部解除条件；本地用户反馈修复不能替代这些证据。

## 根因与实现契约

1. `RenderBufferBuilder` 输出的是可直接显示的 authored sRGB BGRA8。Scene View 原样采样，所以颜色正确；Game 管线此前把该纹理当 linear，再执行 `GammaPass(2.2)`，导致灰岩提亮、红橙熔岩偏黄。
2. emissive 副输出保存了与 world 相同的材质色，旧 composite 使用 `world * visibility + emissive`，完全可见的熔岩仍被加亮一次。fragment 与 compute 路径都存在同一语义。
3. `PlayerHealth` 旧逻辑对整个 AABB 取最大危险材质伤害，任意一个 stray hazard cell 都等同整身浸入；`PlayerController` 还用头顶 rigid-owned cell 探针补判，`PhysicsSystem` 则把持续支撑/重叠清理重复计为新接触。
4. 每次伤害会向权威 world 以 `ash` 材质喷出 10 个自由粒子，沉积后形成用户看到的杂乱物体；Controller 与 Health 还会重复播放受击音效。

实现提交 `d22aeab8` 建立以下边界：

- `WorldBlitPass` 在光照前将 authored sRGB 以 gamma 2.2 解码为 linear；`CompositePass` 通过 `uDecodeWorldSrgb` 区分直接 `WorldTexture` 重载与已经 linear 的 `ColorRenderTarget` 重载。
- CPU emissive、fragment composite、GL compute composite、Radiance Cascades 与 GPU particle MRT 统一颜色空间。最终 core 使用 `max(litWorld, emissive)`，emissive 只补足 visibility 压暗量，不重复叠加同一 albedo；末端 gamma 与输入解码对称。
- 环境伤害按危险 cell 数量相对玩家横向宽度归一，并以最高材质 DPS 封顶；单个危险 cell 只产生 `1 / playerWidth` 的覆盖伤害，一整行接触才达到完整 DPS。
- Physics 只在刚体首次进入角色 proxy 时报告接触，以 1px support hysteresis 吸收重力微分离；持续支撑和已跟踪 overlap cleanup 不再重复计数。
- 受击反馈由 `PlayerVisual.ShowDamageFeedback` 绘制红/白闪烁，`PlayerHealth` 独占音效与 cooldown；删除 ash burst，不再写入权威 cell world。

## 基线复现

修复前实例 `50d638c327c340e485393df2fe0ccd27`、Play session `60bc5575d9894dbe8bcd9a45a7651c75` 中：

| 项目 | 结果 |
|---|---|
| Scene artifact `793af06a118847c4b71070beb18d0a12` | 1970×1794；SHA256 `8674a10cf84d909ea8a63faedeb1e8ec266f199409912974d2c7c1d9cea72e33`；熔岩为红橙色 |
| Game artifact `d51681e93ddd4747b6d37543f9d2c7a6` | 2560×1440；SHA256 `88eb7aa1ff8168523bad1f9d3bd9e78f33f283e63d3b125c0c3d75fa00d547c7`；熔岩被推向黄橙，灰岩接近白色 |
| 默认斜坡玩家 | `X=48`、`Y=284.0379`、`OnGround=true`、`GroundSlopeRadians≈0.7854`；`Health=100` 但已累计 `DamageEventCount=274`、`RespawnCount=4` |
| 旧反馈 | 代码路径每次伤害喷 10 个 `ash` 粒子，速度 70；快照时 active 为 0，但此前粒子已可沉积回 cell world |

## 提交同源 Editor 验收

Release Editor 从 clean `d22aeab8` 工作树二进制启动，使用隔离 discovery、artifact、import、user-data 与 log 目录。CLI 经 `discover`、`ping`、`capabilities --matrix` 连接 PID `116416` / instance `7af9bbea48ed4fb29f47b2a985f20ff8`，验证 172 个 capabilities 与 329 个 UI commands：

| 身份 | SHA256 |
|---|---|
| capability digest | `71646e3c403d0856441e59135b2219581162f88d395e9a6b5977bfcac7c0b321` |
| UI command digest | `100edff1c3002fb7c797f7284365377c8fc29626409d49ced1bdf1b4b10d0f95` |
| matrix digest | `fe1c43b84f518b6fd2380acee0c6d8af9baa773fc3df22dfa6a971ba7eebab54` |

所有截图均由 `scene.capture` / `game.capture --verify-artifact` 生成，server/local byte length 与 SHA256 双重验证通过，并复制到 `artifacts/editor003-game-color-damage-d22aeab8/`：

| 阶段 | artifact | 尺寸 | SHA256 |
|---|---|---:|---|
| 斜坡 296 tick Scene | `e640a48ff2544d068bfdfbc01dc07065` / `slope-scene.bmp` | 521×590 | `f59d78c8cc7131eb9da150ea859ec3cc250166a37bd9073cecd79179d8251d4d` |
| 斜坡 296 tick Game | `46961e0bc854478f81a6c744bc44f137` / `slope-game.bmp` | 1280×720 | `ce3f447ddf8e976ce9242144042398f1f429ccdf4e0e48997cd486c2ffbc9c52` |
| tick 6 熔岩 Scene | `cbcdcf549c3f4d93ae874e53684a120d` / `lava-scene.bmp` | 521×590 | `c69ffb9272d8cbf20b8658d3ebbcd194a522d5b0714a3b1e1aa7776363e60730` |
| tick 6 熔岩 Game | `5e7fc411a2c240a8b7eb1378a6b002e1` / `lava-game.bmp` | 1280×720 | `a62b57e840acb6c769f88b6a5b903c3a81b858c7709b189fc38db301b3410d6c` |

同一熔岩区域的内部采样为 Scene `(514,360) = RGB(211,93,21) / #D35D15`，Game `(1260,500) = RGB(212,93,21) / #D45D15`。另有真实 GL 回归 `RenderPipelinePreservesAuthoredMaterialColorsThroughDefaultGammaWhenExplicitlyEnabled`，直接以 stone `#5D6169` 和 lava `#CF5913` 输入完整 RenderPipeline，默认 gamma 下逐通道误差均不超过 4/255。

第二个 Paused 临时快照只为镜头/反馈验收把玩家重生点改到 `X=244` 并单步一帧；该位置发生一次真实 rigid impact，`HazardContactCellCount=0`，因此 `Health=65` 来自刚体而非熔岩。Flash 为 0.12 秒、overlay command 为 6、粒子 active 为 0。`play.stop` 恢复 tick 0 / 192 chunks，最后通过公共 `workspace.exit` 关闭；PID 已退出且 discovery record 已移除，没有直接终止进程或读取 credential 内容。

## 构建、测试与门禁

| 验证 | 最终结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore` | 0 warnings / 0 errors |
| `PixelEngine.Physics.Tests` | 82 passed / 0 skipped / 0 failed |
| `PixelEngine.Rendering.Tests` | 197 passed / 28 个显式环境 smoke skipped / 0 failed |
| `PixelEngine.Demo.Tests` | 154 passed / 1 个显式环境 smoke skipped / 0 failed |
| `PIXELENGINE_RENDERING_GL_SMOKE=1` 定向真实 GL | 5 passed：direct composite、authored color、GPU particles、compute bloom/light composite、Radiance Cascades |
| shader / composite 合同定向 | 22 passed / 0 failed |
| `git diff --check` | 通过，无 whitespace error |

新增回归覆盖：默认 gamma authored 色还原、emissive 不重复相加、fragment/compute/RC/GPU particle 颜色空间合同、单危险 cell 覆盖伤害、斜坡移动零伤害、刚体持续支撑只报告一次接触，以及受击闪烁且粒子系统保持 0 active。
