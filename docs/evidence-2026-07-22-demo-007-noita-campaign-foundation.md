# DEMO-007 Noita 战役基线与完整换局生命周期完成证据

taskIds: `DEMO-007`
implementationCommit: `e4491e90e77a85d14ad80a7090d391240ca5a7e5`
runSessionId: `local-20260722-demo007-regression-e4491e90`
evidenceState: `complete`

## 结论

Demo 已从单一无限沙盒扩展为默认 Campaign 与可选 InfiniteSandbox 两种真实游戏模式，并建立了 Noita 式纵深主路径、七个 Holy Mountain 锚点、显式 run seed/state、永久死亡结算和新 seed 换局。Campaign 运行态覆盖 `MainMenu -> StartingRun -> Exploring <-> HolyMountain -> Laboratory -> Completed/Dead -> RunSummary`；Campaign 死亡后 `restart_game` 在脚本安全相位重建 world/script/entity/UI/physics/particle/audio/event 状态，InfiniteSandbox 则在同一 world/seed 中安全重生且不进入结算。

用户最初指出的两个直接问题也在本任务内闭合：所有地形破坏入口统一提交 topology dirty region，失去锚点的局部固体只会在明确预算内转为刚体或有限寿命自由粒子，不再留下永久静态悬空颗粒；MaterialBrush、武器、item 与 UI 的数字键、滚轮和鼠标键由单一玩法输入所有者仲裁。HUD 同时显示模式、seed、biome、深度、run state、当前装备、准星材质稳定键与中英文名称。

全部 Demo 行为只使用 PixelEngine 公开 API。为完成换局发现的通用脚本生命周期缺陷在 `PixelEngine.Scripting` / `PixelEngine.Hosting` 公共边界修复，没有把 Noita 玩法写进引擎，也没有由 Demo 访问引擎内部类型。

## 提交节点

| Commit | 节点 |
|---|---|
| `b5feba0e` | Campaign / InfiniteSandbox 双模式、run state、死亡结算与重开合同 |
| `8fb73433` | 八区纵深拓扑、七个 Holy Mountain 与确定性 Campaign chunk 生成 |
| `3535d6c6` | 将帧内退出后的资源释放延迟到 Hosting 安全相位 |
| `f269428c` | 保留暂停态 Play session，避免生命周期误清理 |
| `1698db29` | 同步 Noita 高保真复刻目标、SCOPE-008 与 DEMO-007..013 canonical 口径 |
| `4ccbe577` | 统一地形坍塌通知、玩法输入所有权与材质反馈 |
| `58c46a09` | 修正 Web/ManagedFallback 双后端材质 HUD 布局 |
| `b782307f` | 修复旧 plan checkbox 迁移快照，保持 canonical 单一状态源 |
| `f2a65cfe` | 补齐武器、反应、刚体与外部破坏的 topology dirty 通知 |
| `d22d2092` | 阻断坍塌反馈循环并实现有限寿命悬浮粒子 |
| `42e74fb2` | 将战役数据与界面命名迁移到 Noita 八区 / Holy Mountain 合同 |
| `5a6e78eb` | 收敛 Noita 风格 HUD 为单一无重叠面板 |
| `d5531f00` | 在脚本安全相位原子重建所有 Behaviour，隔离私有运行态 |
| `fa8be8e1` | 修复换局后 HUD 同步与 RunSummary 文本/控件重叠 |
| `614d65d1` | 清理跨 Play session 的 pending restart 与状态泄漏 |
| `7a7b7892` | 按真实游玩反馈重开退化悬空碎条与武器命中可读性验收 |
| `e4491e90` | 将无 2x2 实心核的脱离碎条转为 debris，并数据化射程、强化弹着/爆炸反馈 |

## 战役与世界合同

`content/campaign.json` 经公开 Content/Config API 加载、校验并形成确定性 Campaign 拓扑。主路径顺序为：

1. Mines
2. Coal Pits
3. Snowy Depths
4. Hiisi Base
5. Underground Jungle
6. The Vault
7. Temple of the Art
8. The Laboratory

前七个 biome 出口各连接一个 Holy Mountain；The Laboratory 是最终区域。生成器以全局 seed、chunk 坐标和深度决定区域、材料与连接，不依赖 chunk 加载顺序；修改后的 chunk 仍由 region store 优先恢复，resident chunk 保持预算有界。横向侧区和确定性连接已经进入拓扑合同，更完整的 pixel-scene、遭遇点、秘密连接、捷径与 parity matrix 由下一项 `DEMO-008` 承接。

| 合同 | 结果 |
|---|---|
| 默认模式 | `Campaign`；主菜单可切换 `InfiniteSandbox` |
| 初始 seed | `0x504958454C534248`；新 Campaign run 原子生成不同 seed |
| Campaign 死亡 | 进入 `RunSummary`，保留死亡 run 统计，随后以新 seed 创建干净 run |
| Sandbox 死亡 | seed/world 不变，`RespawnCount` 增加，不显示结果 modal |
| 区域 | 八个主路径 biome、七个 Holy Mountain 锚点、The Laboratory 终区 |
| persistence | 已修改 chunk 优先于程序化再生成；跨加载顺序确定 |
| 生命周期清理 | world/script/entity/UI/physics/particle/audio/event 与 Behaviour 私有字段重新初始化 |
| 输入所有权 | Combat/Brush 模式和 UI modal 由一个权威 owner 仲裁，未挂载占位玩法对象 |

## 地形悬空残留修复

地形变化不再由单一武器局部处理。MaterialBrush、投射物/爆炸、材料反应、刚体 erase/re-stamp、外部 damage sink 与脚本化破坏统一提交 dirty rectangle；topology processor 只扫描 dirty region 与规定 halo，并在预算内做锚点连通性判断。断开的 solid component 按尺寸、像素数和形状合同转为刚体；不适合刚体的少量残片进入带 age、gravity、rest/ground termination 的有限自由粒子路径。

坍塌自身产生的 erase/re-stamp 通知带来源和抑制语义，不会再次递归触发同一区域坍塌；扫描、连通分量、fallback 与 impact 共用实例工作缓冲，稳态不为每个 region 分配临时集合。定向测试覆盖所有破坏入口、跨 chunk dirty halo、锚定结构、悬挑 fallback、刚体转换、有限粒子终止和反馈循环上限。

## 构建与测试

原始完成证据的 clean worktree HEAD 为 `614d65d1dc5692df2ad7822239a3618dd8d7ff31`，tracked status 为空；submodule 固定为 Box2D `8c661469`、FreeType `0a0221a1`、RmlUi `22b93ae9`。`dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` 返回 0 warning / 0 error。真实游玩回归修复后的新 clean HEAD 与复验结果见下节。

| 测试项目 | 结果 |
|---|---|
| `PixelEngine.Scripting.Tests` | 99 passed / 0 skipped / 0 failed；15 s |
| `PixelEngine.Demo.Tests` | 175 passed / 1 显式 native GL 条件 skipped / 0 failed；28 s |
| `PixelEngine.Hosting.Tests` | 980 passed / 7 显式 native GL、外部拖放或物理环境条件 skipped / 0 failed；单次全套 5 m 6 s |

首次把 detached worktree 放在主仓库 `artifacts/` 下时，`SolutionTracksEveryRepositoryProject` 因测试夹具会过滤绝对路径中的任意 `artifacts` segment 而把全部 `.csproj` 排除，得到 979 passed / 7 skipped / 1 failed；将同一测试程序集（SHA256 `92496569fdd958471d87c7911e14a0239b83f3e17561d359d532a8b974aa1b98`）放到仓库同级 worktree 后该项通过。第二次外部 worktree 全套因尚未 materialize submodule/native 输入而有 3 项环境失败；初始化三个固定 submodule 并复制同一 clean native build 后，这 3 项先以 3/3 通过，随后 Hosting 全套取得上表单次全绿结果。证据没有把这些环境准备失败隐藏成源码成功。

关键回归覆盖 Campaign 换局重建全部 Behaviour、private field 复位、HUD 新 run 同步、RunSummary 分块布局、跨 Play pending restart 清理、Sandbox 同 world respawn、terrain topology、输入仲裁和双 UI backend。Demo 的 1 条 skip 与 Hosting 的 7 条 skip 均是已有显式环境条件；DEMO-007 的生命周期、地图、输入、悬空地形与 UI 用例没有被 skip。

## PixelEngine Editor 真实 Campaign 生命周期

全部操作经已认证 `pixelengine-editor` 公共 CLI 完成：discover、capability/matrix、Play、runtime component field、game UI semantic action、artifact capture、Stop 与 workspace exit。没有读取凭据、调用内部 pipe、使用 MCP/Computer Use/OCR/坐标点击或 `--scripted-*`。

| 字段 | 值 |
|---|---|
| Editor process / instance | PID `104744` / `85ae13035a03411088e0bf088429f122` |
| Play session | `4aa74f9b9b104cc0a33e2f2a2b09fca3` |
| capability / UI / matrix | 173 capabilities / 329 UI commands；matrix digest `6b96db32c79064972876067afba2b718a2434034a97474b093708d8b835affff` |
| 初始状态 | Campaign / Exploring / Mines；seed `504958454C534248`；health 100；HUD 2；Combat；weapon input active |
| 死亡触发 | 仅通过公共 runtime Inspector 设置 `PlayerHealth.ForceHazardForProbe=true`，未直接写 Health |
| RunSummary | seed 保持死亡 run；health 0；damage events 294；respawns 0；reason `永久死亡 / Run ended`；modal 3 |
| 新 run | `restart_game` 后 seed `BB23EDFF3CDE65D8`；Exploring / Mines；health 100；damage/respawn 0；force hazard false |
| 清理 | Main 0 / HUD 5 / Modal 0；相关 Behaviour 均 `faulted=false`；Console 0 warning / 0 error |
| 性能快照 | 118.38 FPS；p99 12.7655 ms；sim 60 Hz；particles/bodies/pending physics damage 均 0 |

Campaign 结算截图为 1280x720、3,686,454 bytes、SHA256 `4b1cd1120f950438f5287eb4971888d2a432540d4a24e46263791cd9e6e08c7f`；标题、死亡原因、seed、统计与按钮没有重叠。新 run HUD 截图同为 1280x720、3,686,454 bytes、SHA256 `2738111f45202fa43213565ce627dee8e30476b357d02f16e92304328947a9ac`；没有错误回到主菜单，seed、Mines、health 与装备面板同步到新 run。

## PixelEngine Editor 真实 Sandbox 重生

最终 Sandbox 验证使用 Editor PID `112676`、instance `96aa0033e4b04bbdba554eebfcc2f119`、Play session `3849838987454da4bcbb5d937ea9a15c`。新 Play session 首先正确停在 MainMenu，证明上一轮 restart 状态没有泄漏；随后经 `game.ui.action.invoke` 选择 InfiniteSandbox 并开始。

初始为 InfiniteSandbox / Exploring、seed `504958454C534248`、health 100、damage 0、respawn 0、Combat、brush available。经公共 runtime Inspector 启用 hazard 后，bounded poll 观察到 `RespawnCount=1`，立即以当前 revision 关闭 hazard。最终仍为同一 Play session、同一 seed、Exploring，`ResultReason` 为空、`IsRestartRequested=false`、health `46.231865`、damage events 451、respawns 1、Main 0 / HUD 2 / Modal 0，相关 Behaviour 无 fault；Console 0 warning / 0 error。性能快照为 119.75 FPS、p99 12.3749 ms、1% low 80.81 FPS、sim 60 Hz，particles/bodies/pending damage/task bridge faults 均 0。

Sandbox 截图为 1280x720、3,686,454 bytes、SHA256 `d6bca37909fafa45520652959b497dbb84d38d65737418026cb3643392bf1cbf`；画面显示 InfiniteSandbox HUD、仍为正值的受损 health，且没有 RunSummary modal。Stop 与 workspace exit 均通过，Editor 进程退出、discovery 为空。

## 2026-07-22 退化悬空碎条与命中反馈回归复验

真实游玩复现了此前测试未覆盖的边界：一个脱离地形的连通块可能达到 8-pixel 刚体下限，却只有一像素厚、没有任何 2x2 实心核。旧扫描会尝试 `CreateFromRegion`，随后以 `degenerate` 拒绝并把原 cell 永久留在静态网格。`e4491e90e77a85d14ad80a7090d391240ca5a7e5` 改为在每次扫描的 256-pixel 硬预算内把这类无合法刚体面积的碎条转成带重力和有限寿命的 debris；预算耗尽会显式报告，不再把退化失败当作已处理。新增测试同时覆盖直接扫描和真实 `Input -> Damage -> WorldMutationEvent -> topology scan` 路径，12x1 stone strip 均完整离开权威静态网格。

同一提交移除了武器控制器的 180/220-cell 隐式固定射程：`weapons.json` 现在为六种武器声明 280–520 cell 射程，一号枪为 420 cell；弹道只在真实 solid 命中时显示亮色命中点/描边，射程截止显示暗色叉号，并记录命中材质稳定键。爆炸 flash 增加八向射线、双层轮廓和核心，弹着/手雷/爆炸补充有界彩色 streak、粒子核心和点光；HUD 原本被 CSS 隐藏的材质用途说明恢复显示。

复验使用外部 detached clean worktree，HEAD/tracked status 为 `e4491e90`/空。Editor Shell 与 Demo Release build 均为 0 warning / 0 error。`PixelEngine.Demo.Tests` 的最终 clean 全套为 190 passed / 1 显式 native GL 条件 skipped / 0 failed；其中退化碎条真实事件测试与 180-cell 之外一号枪命中测试均执行。第一次 clean 全套曾出现一次既有长路线用例在 2400 帧卡于起点右侧通道；该用例随后独立连续 6/6 通过，完整 clean 全套重跑也通过，本报告保留这次瞬时失败而不把它改写成首次即绿。

公共 `pixelengine-editor` skill/CLI 连接 PID `131432`、instance `36721f3feb5a41cc965b3fdc364c3f87`；ping、capability/UI matrix digest `e70c5275bd56501b0934049e6d59478adf81c159e0bf82f8de54af5bb3011f10`、revision-safe Play 与 workspace exit 均通过，Console 为 0 warning / 0 error。Scene artifact 为 652x590、SHA256 `c0eb91feda0cd7ff9b2fc9e329a296505d8eff906579d304d52a73058f04c330`；Play 主菜单 artifact 为 1280x720、SHA256 `ca79629d540355af8f78a23bc9b5e14d1a3476e41551488b2b23759a6fef12cc`，两者均由 server/local 重算长度与 SHA256。由于该 clean commit 不含另一个工作树尚未提交的 UI semantic-click 扩展，战斗态没有借用脏代码点击按钮；改由同一 clean commit 的正式 Demo 真窗口 `--scripted-window-demo` 自动走公开玩法输入并捕获 1080x720 framebuffer，SHA256 `28463d7a7f60a800640610d8f226ac564d3c1427404093d416cd9230f0e47f65`。画面实际显示 `石 / Stone` 与“可被冲击、破碎后成为砂砾”的用途说明，以及手雷爆炸核心、射线和粒子层。

## 最终输出与 packaged Player

`tools/update-final-output-fast.ps1 -Rid win-x64 -Configuration Release -DemoRuntimeUiBackend RmlUi` 从 tracked-clean `614d65d1` worktree 生成 Editor、R2R Demo 与 Windows MSI，再经同卷临时目录和入口哈希校验原子提升到仓库根 `最终输出/`。`_快速构建/manifest.json` 记录 `gitCommit=614d65d1...`、`sourceTrackedWorktreeClean=true`、`win-x64 / Release / r2r / RmlUi`。`content/startup.json` 为 `scenes/infinite-sandbox.scene`、1080x720 Windowed、RmlUi、Production；打包 scenes 只包含该入口场景。

| 文件 | Bytes | SHA256 |
|---|---:|---|
| `最终输出/编辑器/PixelEngine.exe` | 177,152 | `7f4c561efb619389c89ea532f1f4f991381709d3c1e332825c46c97a1a1d5591` |
| `最终输出/游戏Demo/PixelEngine Demo.exe` | 162,304 | `ed7333cf09b08bf028a847fe1d1ff4143e4ba34fc2b18cc54f575ac44721ce5c` |
| `最终输出/游戏Demo/content/startup.json` | 255 | `5e6ec639f8c0cc74da147afe32ace0d6fee6ec9ac9a23f4e3002eea6f7299307` |
| `最终输出/安装器/PixelEngine-Setup-0.1.0-win-x64.msi` | 73,800,063 | `cf0cd6e9bc89f511877b7a97acf59b946acd965ce2393635c16edcb5ec55f3d3` |

游戏包 `SHA256SUMS` 独立重算 186/186 匹配、0 mismatch；索引自身 SHA256 为 `3afb9f48a8d4d3e884ccc12540794d6ff016df6c727043c3299c7678017a6dcd`。MSI 静态 verifier 为 `ok=True`、277 files、2 shortcuts、2 embedded cabinets。

clean 包与提升后的根正式包都真实运行 80 tick 并返回 exit 0。正式根 Player probe 为 Windowed applied true、client/framebuffer 1080x720、3 Canvas、requested/active `RmlUi`、fallback false、non-ASCII content path true、stderr 0；sim 60 Hz，wall p99 14.639 ms。framebuffer 为 3,110,454 bytes、SHA256 `8778b88d550688564516dd0ebe22bbdf8ae49e8a777c83356901b01e2aefefb1`，主菜单显示 Campaign / InfiniteSandbox、Noita 主路径说明和实际地形背景，文字与控件无重叠。正式 stdout SHA256 为 `40bda335a9d76b4371c08284e5ada025eea73a1f09601647edb418fa5f5d1c01`。

快速输出 manifest 按合同保持 `verified=false`、`testsRun=false`、`probesRun=false`，因为该脚本自身不运行完整 release verifier。本报告不把这些字段冒充正式发行门禁；DEMO-007 的完成由独立 clean build/TRX、真实 Editor 双模式生命周期、包内 checksum、安装器 verifier、clean packaged Player 与提升后根 Player 共同闭合。签名、确定性复构、GitHub Release 与外部 reviewer 仍由各自 release/UI canonical task 跟踪。

## 证据边界

原始 Editor artifacts、TRX、Player framebuffer/stdout 位于本机 `artifacts/` 或同级 detached worktree，属于可再生产物而不是唯一证据。本报告由 Evidence Index 记录 SHA256；`implementationCommit` 是经过构建、测试、Editor 与 Player 运行的源码身份，报告与 canonical 完成状态在随后独立 docs 提交登记，不倒填成运行时 commit。
