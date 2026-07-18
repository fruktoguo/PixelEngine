# 2026-07-18 EDITOR-003 用户反馈修复：材质配色、非等比画笔与当前 Tab 清晰度

taskIds: `EDITOR-003`
implementationCommit: `a1b4ca92e3e8c3683beb92a8e62bfd8eaef77fe1`
runInstanceId: `d678cb34f4ee4a6faa2260e372ec80da`
runClientInstanceId: `codex-editor003-cycle14-live`
evidenceState: `local_material_brush_tab_feedback_fixed_external_dpi_drag_review_pending`

## 结论

用户反馈的三个本地可复现问题已修复：Demo 材质 fallback 色不再与纹理/物理语义串色，`water` 从黄绿色 `#A9E700` 改为与水纹理一致的蓝色 `#3170BE`；Scene Brush 现在可独立调整横向、纵向半径并锁定/解锁比例，鼠标 footprint 随 Scene 相机缩放且与实际椭圆/矩形落笔范围一致；Unity6Dark 不再用高亮焦点窗口标题竞争视觉注意力，当前 Tab 改由稳定的中性明度差异识别。

本轮只关闭这些本地缺陷，不解除 `EDITOR-003` 的不同物理 DPI/200% 显示器、Explorer 人工 pointer drag、runtime 数值物理拖拽和独立 reviewer 外部阻塞。

## 材质配色

`demo/PixelEngine.Demo/content/materials/textures/*.png` 使用 StbImageSharp 解码后，对非透明像素求 RGB 平均值；据此校正 19 个可见材质的 `baseColor`，并同步收敛 `edgeColor` / `highlightColor`。这些颜色是纹理缺失、Editor 调色板和当前无 texture provider 渲染路径的语义 fallback，不把 RGBA 写回 sim cell。

| 材质 | 旧 fallback | 新 fallback / 纹理均色 |
|---|---:|---:|
| water | `#A9E700` | `#3170BE` |
| oil | `#17154B` | `#282032` |
| lava | `#E2A2C9` | `#CF5913` |
| steam | `#D2B0DA` | `#D9E0E5` |
| wood | `#9E5620` | `#72441F` |
| ice | `#ADD8FF` | `#AFE0F5` |
| metal | `#A8A8B0` | `#969CA0` |
| glass | `#D0E8F0` | `#A8D6E4` |

`DemoContentMaterialFallbackPaletteMatchesMaterialSemantics` 固定校验 sand、dirt、ash、water、oil、acid、lava、molten metal、steam、smoke、acid gas、fire、stone、wood、ice、metal、glass、gravel、crystal 共 19 项，并额外断言 water 蓝通道占优、acid 绿通道占优。

## 画笔契约

- `MaterialBrushSettings.RadiusX` / `RadiusY` 分别控制横纵半径，范围 `0..128`；`LockAspectRatio` 提供等比锁。兼容属性 `Radius` 继续同时设置两轴，旧 automation payload 缺少新字段时仍恢复为等比圆/方形。
- Circle/Square 的 UI 语义明确为椭圆/矩形；Point 固定为单 cell。椭圆采用整数判定，矩形走 bulk span，实际写入、越界预算与 automation stroke budget 都使用独立宽高。
- Scene footprint 以 `(radius + 0.5) / cameraCellsPerPixel` 计算屏幕半轴，随相机 zoom 改变而保持世界 cell 尺寸不变；只在 Edit 模式、Scene 内容区域和 Brush 激活时显示，不覆盖 toolbar、参数 overlay 或 Web canvas。
- 面板提供“锁定横纵比例”、横向半径、纵向半径和 `width x height cells` 即时读数。automation 写入材质后，Combo 会从权威 `MaterialId` 同步索引，不再在下一帧回滚选择。

## 真实运行验证

Release Editor 使用隔离 discovery、artifact 和 user-data 目录启动。capability matrix digest 为 `29dd65b20cfb9c50cde41a999568d73391a21655501f908872d4e48a39b67c6a`，capability digest 为 `742fec8855b3ed32a9d79e41be1061d64c56cc59bbea91850612e8661043c7b3`，UI command digest 为 `f29c3e16a9c1625e7339c5e0689707fa7002f700f418fc54f26be211415d51fa`。

automation 将工具设为 Brush、材质设为 water、`RadiusX=8`、`RadiusY=2`、`LockAspectRatio=false`、`cameraCellsPerPixel=0.5`。多帧后的 `tool.scene.get` 保持同一状态，面板显示 `17 x 5 cells`。在 `(320,100)` 执行 `tool.brush.apply`，返回 `writtenCells=45`、未驻留/越界均为 `0`，global revision 从 `12` 增至 `13`，与半径 `8 x 2` 的整数椭圆覆盖一致。

| 捕获 | 结果 | SHA256 |
|---|---|---|
| 落笔前 Scene artifact | 蓝色 water 色块、X/Y 控件与 `17 x 5 cells` 可见 | `4fa65f14c7c02aa607424f91305ce010a6efee6d7699b129e0be5e6e9c27924b` |
| 落笔后 Scene artifact | 世界中出现横向蓝色椭圆，状态显示应用 45 cells | `13e3fe832558f6f1439004288b32d0ad2148eabc9e58be0e85589c32110a9fe6` |
| Windows 外层窗口截图 | 焦点窗口标题保持中性；Scene 当前 Tab 明亮中性，Game View 非当前 Tab 更暗 | `4dc0fbb2a7da7786bbcc7fc4fcf2c492e761f1f79e352854d9af7488aaf3e53f` |

Scene artifact 位于可再生的 `%TEMP%/pixelengine-editor003-cycle14-*/artifacts/`，Windows 截图位于 `%TEMP%/codex-shot-2026-07-18_22-14-03.png`；它们是本机短期证据，报告只固化语义、身份和 hash，不冒充 commit 内持久制品。验证结束后通过 `workspace.exit` 关闭隔离 Editor，未关闭用户原有实例。

## 独立构建与测试

实现暂存补丁导入 detached worktree 后，patch-id 与主工作树暂存区一致，均为 `c6c92947ddb847fea52a66d027dea68077cd1b85`。随后在隔离目录完成验证：

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore -m:1` | 0 warnings / 0 errors |
| `PixelEngine.Editor.Tests` | 135 passed / 0 failed |
| `PixelEngine.UI.Tests` | 160 passed / 13 environment smoke skipped / 0 failed |
| `PixelEngine.Demo.Tests` | 151 passed / 1 environment smoke skipped / 0 failed |
| `PixelEngine.Editor.Automation.Tests` | 126 passed / 0 failed |
| `PixelEngine.Hosting.Tests` 首轮全量 | 960 passed / 7 environment skipped；唯一失败准确发现新增尺寸控件未标记为 command 可达 primitive |
| `EditorAutomationUiClosureTests` 修复后重跑 | 3 passed / 0 failed |
| automation matrix `--check` | snapshot 一致 |
| `tools/validate-task-catalog.ps1` | 通过；82 canonical tasks，51 done / 4 open / 27 blocked |

Hosting 首轮失败后只新增 `[EditorUiControlPrimitive]` 元数据，把尺寸 helper 纳入既有 `panel.brush.radius` production command 的可达闭包；solution 重新编译和三条 closure 门禁均在最终补丁上通过。没有用跳过 hook 或排除失败测试来制造全绿结果。
