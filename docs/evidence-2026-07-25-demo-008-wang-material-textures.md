# DEMO-008 Noita Wang 材质纹理与环境可见度证据

## 结果

提交 `feb957aa` 为通用 Rendering/Hosting 路径接入 `DirectoryMaterialTextureProvider`：引擎在内容装载期一次性解码 `content/textures/<id>_*.png`，运行时按世界坐标平铺采样，不把颜色写入 cell，也不在稳态帧产生托管分配。Demo 生成器从 Build `17130612` 的规范化参考目录复制 28 张 Wang 实际引用材质纹理，为 41 个精确材质绑定稳定 `TextureId=19..46`，并在 `noita-material-textures.json` 记录来源路径、来源 SHA256、包内 SHA256 与材质绑定。

全视口 fog reveal 从完全可见 `255` 收敛到环境可见度 `112`，玩家附近仍由点光源继续提亮。该修改恢复深层 biome 的暗部层次，不改变实际物理像素、相机倍率或 Canvas 设计分辨率。

## 可重现内容

- 参考数据根：`D:\Temp\PixelEngineReference\Noita-17130612-9dbd52ce\unpacked\data`。
- 参考身份：Build `17130612`，version hash `9dbd52ced019a643169a2db02f46c77f8766c6e5`。
- 生成纹理：28 张，合计 245986 bytes。
- `materials.json` 重跑前后 SHA256：`4905274859707B06314905D56A1D03C863BAB06FD85CC09698A24271B811D543`。
- `noita-material-textures.json` 重跑前后 SHA256：`F9985507894210E5D8543168F24E0BBE5555021E121E6A6FA73A53921A9929A0`。
- 28 张生成纹理聚合 SHA256：`50c72f2e9ebc72052d8f5c7a67fe4dea8ce660ab7b3d24621bda06d6c0819f7c`。
- 重跑结果：41 个 Wang 必需材质，4 个复用，37 个生成，28 张纹理，62 个运行时材质；输出幂等。

## 快速验证

- `DirectoryMaterialTextureProviderTests`：2 passed，0 failed。
- `NoitaWangTerrainCatalogTests`：5 passed，0 failed。
- `PlayableWorldDirectorRevealsFullViewport` 与 `PlayerVisualSubmitsOverlayAndLocalLight`：2 passed，0 failed。
- Editor Shell Release build：0 warning，0 error。
- 实机 Editor Console：0 log / 0 warning / 0 error。
- 未运行无关的完整 UI、物理或性能测试套件。

## 真实 Editor

两次捕获均使用配置出生点 `(1503,6006)` 进入深层 Snowcastle/forge 区域，并在结束后停止 Play、Undo 临时事务、退出 Editor；Scene 未保存探针字段。

纹理接入、环境亮度调整前的捕获：

- artifact：`D:\Temp\PixelEngineGoalNoitaTextures1\artifacts\ec235eb116d54f73805fd62745c9ace5\d46f95ad09ff40d78c4e5cfd014842da\8b96a2e7b2324119bca2f55eaf6564dd.bmp`
- size：`1280x720`
- SHA256：`680e82720a95bd50100c1a21123780561c855290330b58e693bd8564664d9887`

纹理接入并使用环境可见度 `112` 的最终捕获：

- Editor instance：`457922d72f4e41d1a752643b73ec43df`
- Play session：`624b47dfbf984ba5bcba0f88042a8c27`
- artifact：`D:\Temp\PixelEngineGoalNoitaTextures2\artifacts\457922d72f4e41d1a752643b73ec43df\d6cad19ef1714f0bb25539e4ad7b5367\b6e77567ba4f4b6c81e0c324e7b8b21e.bmp`
- size：`1280x720`，byte length `3686454`
- SHA256：`f8c7ea73b2e2abe7845c765b54ed01380671716e7fa6a4ea931e725f665d8451`

最终画面中钢板、混凝土、冰等区域已从纯灰/纯白色块恢复为来源纹理，出生房内部暗部与玩家点光源可区分。

## 未完成边界

左侧地形仍呈现密集、碎片化的 atlas 观感。复核来源确认 `snowcastle.png` 是供 Wang 约束选片的 tile atlas，不是可直接按固定倍率展示的世界缩略图。当前管线已经能正确采样 tile 像素和材质纹理，但还没有完整复现 Noita 在 biome 内对 pixel scene、background/visual layer、scene marker、vegetation、实体与装饰的组合规则，因此本证据不宣称地图或视觉 parity 完成。

`DEMO-008` 继续保持 `[~]`。下一节点应恢复 Noita 的 biome scene 拼装次序与背景/植被生成，将 Wang 输出限制在其真实地形职责内，再补交互 marker 和同 seed 全区域截图矩阵。
