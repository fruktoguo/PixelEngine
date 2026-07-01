# Plan 09 — GPU 计算加速

> 本文档定义 PixelEngine 的 **GPU 计算（compute）加速层**，作为 `plan/08-rendering.md` 渲染管线的高性能增强路径，对应架构文档（下称「架构」）§9.3 / §9.4 / §9.5，并受 §4.3 过载降级顺序联动。权威设计依据：`../docs/PixelEngine-架构与需求设计.md`；技术栈定稿：`00-conventions-and-techstack.md`；开发宪法：`../AGENTS.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文档把 GPU 计算用于「渲染侧的合成与光照、以及高密度粒子绘制」，作为 `plan/08` 中 CPU/fragment 路径的**可切换高性能替代**，而不改变模拟的权威归属。它是架构 §4.3 过载降级链中的二级目标（光照质量级降级所操作的对象），并以 capability-gate 保证在 OpenGL 3.3 基线机上完整、优雅地回退到 `plan/08` 的既有路径。

明确的不变式约束（来自 `AGENTS.md §1` 第 9 条与架构 §9.5）：**CPU sim 始终权威**。GPU 在本文档中只承担四类工作——bloom/光照的 compute 合成、Radiance Cascades 高质量 2D GI、高密度自由粒子的 point-sprite 批绘、以及**可选且声明为非权威**的扩散类 sim pass（空气/烟）。本层**绝不**承担权威像素网格的模拟，**绝不**引入 GPU→CPU readback 进入 sim 主循环（像素级碰撞要求网格随时可在 CPU 读，回读会卡流水线，架构 §9.5、§14.1）。任何与该约束冲突的需求一律停止并按 `AGENTS.md §1` 上报，不自行变通。

本文档覆盖（逐项在 §4 / §5 可勾选）：(1) 后端选择与能力门控——`ComputeSharp`（DX12，Windows-only，AOT-safe）对 `Silk.NET` compute（GL 4.3+/Vulkan，跨平台），GL 3.3 基线机回退到 `plan/08` 的 CPU/fragment 路径，**不硬依赖 Vulkan/GL 4.x**；(2) bloom/光照 GPU compute pass（bright-pass → mip 降采样 → dual-Kawase）；(3) Radiance Cascades（Sannikov，PoE2 用）「fancy lighting」可选模式；(4) 高密度自由粒子 GPU point-sprite 批绘（含 emissive、加色混合），读 `plan/05` 粒子缓冲；(5) 可选非权威 sim pass（空气/烟扩散，block/Margolus CA 规避竞争）；(6) 与 `plan/08` 共享 GL 上下文/资源，profiling 与降级联动。

严格不在本范围：窗口/上下文创建、纹理流式（PBO/persistent-mapped）、世界纹理 blit、emissive/occluder buffer 的 CPU 构建、fragment 版光照与后处理、相机——这些是 `plan/08` 的职责，本文档只在其之上接入 compute 替代路径并复用其资源句柄。权威 CA、粒子积分、反应、温度场均属 `plan/03` / `plan/05` / `plan/04`，本文档不触碰其 CPU 权威实现。

---

## 2. 技术栈与依赖

主 compute 后端沿用 `plan/00 §4` 的 **Silk.NET 2.x（`Silk.NET.OpenGL`）**：在 GL ≥ 4.3 时通过 `ARB_compute_shader` + SSBO（`ARB_shader_storage_buffer_object`）+ image load/store（`ARB_shader_image_load_store`）执行 compute shader，跨平台（Win/Linux/Mac，且 Mac 上 GL 4.1 封顶——见下文降级），与 `plan/08` 共用同一 GL 上下文与资源。compute shader 以 GLSL `#version 430` 运行时源码形式提供，无反射、AOT/trim 友好。

可选次级后端 **ComputeSharp（DX12）** 作为 Windows-only、AOT-safe 的高性能路径：它用源生成器把 C# kernel 编译为 HLSL/DXIL，符合 `plan/00 §2` 的 NativeAOT 次发行诉求。**它是 `plan/00 §4` 选型表之外的新增可选依赖**，仅当在 Windows + DX12 capability-gate 命中且被显式启用时加载；若采纳，须在 `Directory.Packages.props`（CPM）登记 `ComputeSharp` 包版本，并在 `plan/15` 的打包中按 `win-x64`/`win-arm64` RID 处理。它**不是默认路径**，不得成为任何平台的硬依赖；非 Windows 与未启用时整路径不引用其类型（以接口隔离，见 §3.1）。这一处置与 `plan/00 §8` 验收第 1 条「不与技术栈表冲突」一致：表内 GL compute 为默认，ComputeSharp 为门控可选增项。

其余依赖：Core 的诊断/计时器与持久线程池（`plan/02`，GPU timer query 结果注册到诊断、CPU 回退路径复用线程池）、`EngineConstants`（`plan/00 §7`，新增本层编译期常量如 cascade 层数、工作组尺寸）、`plan/08` 的 render graph 与资源句柄（世界纹理、emissive buffer、occluder/solidity map、PBO）、`plan/05` 的粒子 SoA 缓冲。互操作纪律遵循 `AGENTS.md §3`：GL 调用经 Silk.NET 托管绑定，无新 `DllImport`；GPU↔CPU 共享缓冲若需 CPU 侧暂存走 POH/`NativeMemory`，但本层默认零回读。

不引入：任何要求 Vulkan/GL 4.x 才能启动的代码路径；任何把 GPU 结果同步回读进 sim tick 的逻辑；compute 替代不得改变 `plan/08` 既有 fragment 路径的对外行为（二者输出在同一 buffer 上等价可切换）。

---

## 3. 详细设计

### 3.1 后端抽象与能力门控（架构 §9.5）

定义 `PixelEngine.Rendering.Compute` 命名空间，核心抽象 `IComputeBackend`：声明 compute kernel 的加载、SSBO/image 绑定、`Dispatch(groupsX,groupsY,groupsZ)`、内存屏障（`MemoryBarrier`）与 GPU 计时查询。三个实现：`GLComputeBackend`（默认，Silk.NET GL 4.3 compute）、`ComputeSharpBackend`（可选，Windows/DX12，经接口隔离，未启用时不被 JIT/AOT 触及）、`NullComputeBackend`（不可用时的空实现，所有 compute 入口转交 `plan/08` 的 fragment/CPU 路径）。

`GpuCapabilities`（只读 struct）在引擎启动时由 `plan/08` 的上下文创建后探测一次并缓存：GL major/minor 版本、`GL_ARB_compute_shader` / `GL_ARB_shader_storage_buffer_object` / `GL_ARB_shader_image_load_store` 扩展存在性、`GL_MAX_COMPUTE_WORK_GROUP_*`、是否 ANGLE/ES3 上下文、是否 DX12 可用（仅 Windows 查询）。`ComputeCapabilityGate` 据此选择后端并产出每能力的布尔门控位，决策点（统一编号，供清单与测试引用）：

- **G1 GL-compute 门控**：GL ≥ 4.3 且三扩展齐备 → `GLComputeBackend` 可用。
- **G2 DX12/ComputeSharp 门控**：Windows + DX12 feature level 达标 + `ComputeSharp` 已编译进当前发行 + 用户/配置显式启用 → `ComputeSharpBackend` 可用（否则即便在 Windows 也不启用）。
- **G3 基线回退门控**：G1/G2 均不命中（含 GL 3.3、Mac GL 4.1、ANGLE/ES3、问题驱动）→ 整层降为 `NullComputeBackend`，bloom/光照走 `plan/08` fragment 路径、粒子走 `plan/08` CPU stamp 路径。**这是「不硬依赖 Vulkan/GL 4.x」的落点。**
- **G4 逐特性门控**：在后端可用前提下，bloom-compute、Radiance Cascades、GPU 粒子、非权威 air pass 各自有独立开关（受配置、`plan/12` 编辑器、与 §4.3 自适应降级控制），任一关闭即回退其对应 `plan/08` 路径，互不牵连。

后端选择优先级：默认 `GLComputeBackend`（跨平台一致）；ComputeSharp 仅在 G2 且被显式选择时覆盖光照/bloom/非权威 pass 的执行（粒子绘制始终走 GL point-sprite，因其与 `plan/08` 的 GL render graph 绑定）。所有门控结果与所选后端注册到 Core 诊断，供 HUD 显示。

### 3.2 与 plan/08 的资源/上下文共享（架构 §9 引言、§9.2）

本层不创建 GL 上下文，复用 `plan/08` 的 `IRenderContext` 与其 render graph 暴露的资源句柄：世界纹理（BGRA8）、emissive additive buffer、occluder/solidity map、bloom mip 链纹理、最终合成 target。`GpuComputeResources` 持有这些句柄的引用并管理 compute 专属的中间资源（SSBO、cascade 纹理、SDF 纹理），生命周期随 `plan/08` 的 swapchain/resize 事件重建。compute 与 graphics 之间的可见性用 `glMemoryBarrier`（`SHADER_IMAGE_ACCESS_BARRIER_BIT` / `SHADER_STORAGE_BARRIER_BIT` / `TEXTURE_FETCH_BARRIER_BIT`）显式插入，遵守相位顺序（架构 §3.3 相位 10 渲染线程内）。所有 compute pass 在相位 10 内执行，**不跨相位**，不与 sim 相位（3–8）争用 CPU 权威数据。

### 3.3 bloom/光照 GPU compute pass（架构 §9.4）

把 `plan/08` 的 fragment bloom（bright-pass → mip 降采样 → dual-Kawase/separable Gaussian → additive upsample）整体改写为 compute 等价管线，输入为 `plan/08` 构建的 emissive buffer，输出回 `plan/08` 的合成 target，二者**像素等价、可切换**。compute pass（GLSL `#version 430`，命名与编号供清单引用）：

- `bloom_brightpass.comp`（CP-B1）：从 emissive buffer 提取超阈值亮部到 mip0，threshold/soft-knee 作 uniform。
- `bloom_downsample.comp`（CP-B2）：13-tap / Kawase 降采样逐级生成 mip 链（image load/store，逐 mip dispatch）。
- `bloom_dualkawase_down.comp`（CP-B3）与 `bloom_dualkawase_up.comp`（CP-B4）：dual-Kawase 模糊的下/上行 pass（架构 §9.4 指定 dual-Kawase）。
- `bloom_upsample_composite.comp`（CP-B5）：逐级 additive 上采样并合回 target。

光照基础合成（emissive additive composite、fog-of-war reveal、dither、gamma）在 compute 可用时可一并下放为 `light_composite.comp`（CP-L0），否则保留在 `plan/08` fragment。dithering/gamma/可选 CRT 仍可留在 `plan/08` 的最终 fragment pass（避免重复实现），本层只替换计算密集的 bloom 与 GI。工作组尺寸（如 8×8）作 `EngineConstants` 常量，按 `GL_MAX_COMPUTE_WORK_GROUP_*` 校验。

### 3.4 Radiance Cascades 高质量 2D GI（架构 §9.4 可选高质量模式）

实现 Sannikov 的 Radiance Cascades 作为「fancy lighting」**可选模式**（默认关，G4 控制），产出无噪、软、带 bounce 的 2D GI，与 bloom 同为架构 §4.3 第二级降级目标。一串 compute（GL 4.3+）/fragment（ComputeSharp 或回退时）pass：

- `rc_sdf_jfa.comp`（CP-R0）：由 occluder/solidity map 经 Jump-Flood 生成距离场（SDF），供 cascade 射线步进。
- `rc_cascade_build.comp`（CP-R1）：逐 cascade 沿其角度/空间分辨率配置采样射线区间（近场高空间分辨率/低角分辨率，远场反之），写入 cascade 纹理数组。
- `rc_merge.comp`（CP-R2）：自高 cascade 向低 cascade 双线性合并射线（cascade N+1 → N），形成连续辐照。
- `rc_apply.comp`（CP-R3）：把 cascade 0 的辐照应用到场景光照 buffer，叠加 emissive。

cascade 层数 `RadianceCascadeCount`、每层角度/空间分辨率、射线步数封为 `EngineConstants` 常量与运行时质量档。该模式的输出接到与 fragment 光照相同的合成点，启用/禁用对 `plan/08` 透明。它与 bloom 共享 §4.3 降级语义：降级时先关 Radiance Cascades 回退到 fog-of-war + emissive（架构 §4.3 第二级、§9.4）。

### 3.5 高密度自由粒子 GPU point-sprite 批绘（架构 §9.3）

为大量火花/血雾/熔渣/爆炸碎屑提供 GPU point-sprite 批绘，作为 `plan/08` CPU stamp 路径的**可切换替代**（`ParticleRenderMode { CpuStamp, GpuPointSprite }`，G4 控制）。读 `plan/05` 的粒子 SoA 缓冲（`float x,y,vx,vy` + `ushort material` + `byte colorVariant` + `byte life`，架构 §7.6），经 persistent-mapped/orphan VBO 或 SSBO 上传当前活跃粒子（仅 `activeCount` 个），用 `GL_POINTS` 一次 draw call 绘制：

- `particle_pointsprite.vert`（SP-P1）：从粒子缓冲取位置（cell→屏幕坐标变换由 uniform 提供，与 `plan/08` 相机一致），设 `gl_PointSize`，按 `material`/`colorVariant` 采样材质基色。
- `particle_pointsprite.frag`（SP-P2）：输出粒子颜色；**发光粒子（火花/熔渣）经 MRT 同时写入 emissive buffer**（或第二 additive pass），使其参与 bloom 辉光（架构 §9.3 明确「这批 sprite 也要参与 emissive pass」）。emissive 加色混合用 `glBlendFunc(GL_ONE, GL_ONE)`（加色），非发光粒子用常规 alpha/直写以与世界纹理视觉一致。

`GpuParticleRenderer` 类管理 VBO/SSBO（POH 暂存→上传，零 per-frame 托管分配，遵 `AGENTS.md §3`）、绘制顺序（世界纹理 → 粒子 → 角色/刚体高亮 → 光照 → bloom，架构 §9.3 合成顺序）。CPU stamp 路径与 GPU 路径产出在视觉上一致、可热切换；高密度（5 万–20 万+，含加色火花）场景默认偏好 GPU 路径，G3 回退时自动用 CPU stamp。本层**只读** `plan/05` 缓冲，不修改粒子状态（积分仍在 CPU 权威，架构 §7.6）。

### 3.6 可选非权威 sim pass：空气/烟扩散（架构 §9.5、不变式 #9）

提供**可选、默认关、显式声明为非权威**的扩散类 compute pass（G4 + 配置控制），用于空气/烟的视觉扩散增强。关键设计与不变式对齐：

权威像素网格**始终在 CPU**（`plan/03` CellGrid SoA）。本 pass **不读写**权威网格，而是操作一个**GPU 常驻的独立标量场**（air/smoke density texture），由 emissive/材质信息单向播种（CPU→GPU 上传，可与世界纹理同传），其输出**仅用于渲染合成**（烟雾 shimmer/遮蔽视觉），**不回读进 sim tick**——严格遵守「不接受 GPU→CPU readback 卡流水线」（架构 §9.5、§14.1）。

并发正确性用 **block/Margolus 邻域 CA**：把场按 2×2 block 划分，每步在 block 内做守恒置换，逐步交替 block 原点 parity（Margolus neighborhood），从而**规避「两个 cell 同时入一格」的竞争**（架构 §9.5 明示）。pass：

- `air_diffuse_margolus.comp`（CP-A1）：Margolus 2×2 block 扩散步，block 原点 parity 作 uniform 每步翻转；image load/store 原地更新密度场（单缓冲或 ping-pong 视实现，但不破坏守恒）。

该 pass 在所有 capability 不足或被关闭时直接不执行，无 fragment 回退义务（它是纯增强，不是 `plan/08` 既有功能的替代）。文档与代码注释必须显式标注「非权威、仅渲染、零 sim 回读」，防止后续误用为模拟数据源。

### 3.7 Profiling 与降级联动（架构 §4.3、§12）

每个 compute/绘制 pass 用 GL timer query（`GL_TIME_ELAPSED` / `glQueryCounter`，双帧缓冲避免 stall）测耗时，注册到 Core 诊断分项（`plan/02`），供 `plan/12` 性能 HUD 显示与自适应降级判定。降级联动按架构 §4.3：本层是**第二级降级目标**——连续超预算时先关 Radiance Cascades，再关 bloom-compute，回退到 fog-of-war + emissive（fragment）；再不足则与 §4.3 其余级别（热场、远 chunk 降频、sim 30Hz）协同，由 `plan/08`/帧计时器统一调度。降级开关即 §3.1 的 G4 位，切换无缝、不重建上下文。GPU 计时**不**回读进 sim（仅诊断用途，异步取上一帧结果）。

---

## 4. 实现清单

### 4.1 后端抽象与能力门控（§3.1，架构 §9.5）
- [x] 定义 `IComputeBackend` 接口（kernel 加载、SSBO/image 绑定、`Dispatch`、`MemoryBarrier`、GPU 计时），带完整中文 XML 注释。
- [x] 实现 `GLComputeBackend`（Silk.NET GL 4.3 compute：`ARB_compute_shader`/SSBO/image load-store），GLSL `#version 430`，运行时编译、无反射。
- [!] 阻塞：`ComputeSharpBackend` 已经接口隔离且 `Directory.Packages.props` 登记 `ComputeSharp` 3.2.0；真实 Windows/DX12 执行后端需 `plan/15` 明确 AOT/打包与 PackageReference 策略后实现，当前不可假装可执行。
- [x] 实现 `NullComputeBackend`（空实现，所有入口委派 `plan/08` fragment/CPU 路径）。
- [x] 实现 `GpuCapabilities` 探测（GL 版本、compute/SSBO/image 扩展、`GL_MAX_COMPUTE_WORK_GROUP_*`、ANGLE/ES3、DX12），启动期探测一次并缓存。
- [x] 实现 `ComputeCapabilityGate`：产出门控位 **G1**（GL≥4.3+扩展）、**G2**（Win+DX12+ComputeSharp 显式启用）、**G3**（基线回退到 plan/08）、**G4**（逐特性独立开关）。
- [~] 后端选择优先级已通过 `ComputeBackendFactory` 落地（默认 GLComputeBackend；ComputeSharp 仅 G2 且显式选择时覆盖光照/bloom/air）；门控结果与所选后端注册到 Core 诊断待诊断字段扩展。

### 4.2 与 plan/08 资源/上下文共享（§3.2）
- [x] 复用 `plan/08` 的 `IRenderContext`/render graph，不新建 GL 上下文。
- [~] 实现 `GpuComputeResources`：已持有世界纹理/emissive/occluder/bloom mip/合成 target 句柄引用；compute 专属中间资源（SSBO/cascade/SDF）随后续 pass 落地。
- [ ] 资源随 `plan/08` swapchain/resize 事件重建；compute↔graphics 间插入正确 `glMemoryBarrier`。
- [ ] 所有 compute pass 限定在架构 §3.3 相位 10 内执行，不跨相位、不碰 sim 权威数据。

### 4.3 bloom/光照 compute（§3.3，架构 §9.4）
- [ ] `bloom_brightpass.comp`（CP-B1）：emissive→mip0 阈值提取（threshold/soft-knee uniform）。
- [ ] `bloom_downsample.comp`（CP-B2）：逐级 mip 降采样（13-tap/Kawase，逐 mip dispatch）。
- [ ] `bloom_dualkawase_down.comp`（CP-B3）/`bloom_dualkawase_up.comp`（CP-B4）：dual-Kawase 下/上行模糊。
- [ ] `bloom_upsample_composite.comp`（CP-B5）：逐级 additive 上采样合回 target。
- [ ] 可选 `light_composite.comp`（CP-L0）：emissive additive + fog-of-war reveal 合成下放 compute（dither/gamma 可留 plan/08 fragment）。
- [~] 工作组尺寸（16×16×1）已作 `EngineConstants` 常量并按 `GL_MAX_COMPUTE_WORK_GROUP_*` 校验；compute bloom 与 plan/08 fragment bloom 像素等价、可切换待后续 pass 接线。

### 4.4 Radiance Cascades 可选 GI（§3.4，架构 §9.4）
- [ ] `rc_sdf_jfa.comp`（CP-R0）：occluder/solidity → Jump-Flood SDF。
- [ ] `rc_cascade_build.comp`（CP-R1）：逐 cascade 角度/空间分辨率射线区间采样，写 cascade 纹理数组。
- [ ] `rc_merge.comp`（CP-R2）：高→低 cascade 双线性射线合并。
- [ ] `rc_apply.comp`（CP-R3）：cascade 0 辐照应用到光照 buffer + 叠 emissive。
- [ ] `RadianceCascadeCount`/角度/空间分辨率/射线步数作 `EngineConstants` 常量与质量档；模式默认关、G4 控制、对 plan/08 透明。

### 4.5 GPU 粒子 point-sprite（§3.5，架构 §9.3）
- [ ] `GpuParticleRenderer`：读 `plan/05` 粒子 SoA 缓冲，POH 暂存→persistent-mapped/orphan VBO/SSBO 上传仅 `activeCount` 个，零 per-frame 托管分配。
- [ ] `particle_pointsprite.vert`（SP-P1）：cell→屏幕变换（与 plan/08 相机一致 uniform）、`gl_PointSize`、按 material/colorVariant 取色。
- [ ] `particle_pointsprite.frag`（SP-P2）：输出粒子色；发光粒子经 MRT/第二 pass 写 emissive buffer 参与 bloom；加色混合 `glBlendFunc(GL_ONE,GL_ONE)`。
- [ ] `ParticleRenderMode { CpuStamp, GpuPointSprite }`（G4 控制），与 plan/08 CPU stamp 路径视觉一致、可热切换；G3 回退自动用 CPU stamp。
- [ ] 遵架构 §9.3 合成顺序（世界纹理→粒子→角色/刚体高亮→光照→bloom）；本层只读 plan/05 缓冲、不改粒子状态。

### 4.6 可选非权威 air/smoke 扩散 pass（§3.6，架构 §9.5、不变式 #9）
- [ ] GPU 常驻 air/smoke density 场（独立于权威网格），CPU→GPU 单向播种、可随世界纹理同传。
- [ ] `air_diffuse_margolus.comp`（CP-A1）：Margolus 2×2 block 守恒扩散，block 原点 parity 每步翻转，规避两 cell 入一格竞争。
- [ ] 输出仅用于渲染合成，**实现零 GPU→CPU readback 进 sim tick**；默认关、G4+配置控制、无 fragment 回退义务。
- [ ] 代码与文档显式标注「非权威/仅渲染/零 sim 回读」，权威像素网格始终在 CPU。

### 4.7 Profiling 与降级联动（§3.7，架构 §4.3、§12）
- [ ] 每 pass GL timer query（双帧缓冲，异步取上一帧结果，不 stall），注册 Core 诊断分项。
- [ ] 接入架构 §4.3 第二级降级：超预算先关 Radiance Cascades→再关 bloom-compute→回退 fog-of-war+emissive；与其余级别协同，切换无缝不重建上下文。
- [ ] GPU 计时仅诊断用途，绝不回读进 sim；HUD 经 `plan/12` 展示门控位与各 pass 耗时。

---

## 5. 验收标准

- [ ] **基线机回退**：在仅 GL 3.3（或 Mac GL 4.1 / ANGLE-ES3 / 关闭 compute）环境，G3 命中、整层降为 `NullComputeBackend`，引擎正常出帧，bloom/光照走 plan/08 fragment、粒子走 plan/08 CPU stamp，无报错、无功能缺失。
- [ ] **不硬依赖 Vulkan/GL 4.x**：代码与启动路径不存在「无 GL4.3/Vulkan 即无法启动」的依赖；门控探测覆盖并验证 G1/G2/G3/G4 全分支（含 ComputeSharp 未编译进发行时 G2 恒假）。
- [ ] **compute 与 fragment 等价**：GL-compute bloom（CP-B1..B5）输出与 plan/08 fragment bloom 在同一 emissive 输入下像素级一致（容差内），可运行时热切换无可见跳变。
- [ ] **Radiance Cascades 模式**：启用后产出无噪/软/带 bounce 的 2D GI，禁用后无缝回退 fog-of-war+emissive；默认关、对 plan/08 透明、归入 §4.3 第二级降级。
- [ ] **GPU 粒子**：从 plan/05 缓冲一次 draw call 批绘活跃粒子，发光粒子正确进 emissive 并产生 bloom 辉光；与 CPU stamp 路径视觉一致、可热切换；高密度（≥10 万）加色火花场景帧时间优于 CPU stamp（BenchmarkDotNet/帧计时实测）。
- [ ] **零 per-frame 托管分配**：GPU 粒子上传与所有 compute dispatch 在稳态帧循环内零托管堆分配（`AGENTS.md §3`，分析器/基准验证）。
- [ ] **非权威约束（硬）**：air/smoke pass 不读写权威网格、零 GPU→CPU readback 进 sim tick；权威像素网格始终在 CPU；Margolus block 扩散守恒、无两 cell 入一格（性质测试）。default-off 且显式标注非权威。
- [ ] **资源/上下文共享**：本层不创建 GL 上下文，复用 plan/08 资源；compute↔graphics 屏障正确（无读写竞争/可见性 bug）；resize 后资源正确重建。
- [ ] **profiling/降级**：各 pass GPU 计时注册 Core 诊断并在 HUD 可见；§4.3 第二级自动降级按序触发（RC→bloom→fog-of-war），切换无缝不重建上下文，GPU 计时不回读 sim。
- [ ] **不变式与技术栈无冲突**：全文档不违背 `AGENTS.md §1`（尤其 #9 CPU sim 权威）与 `plan/00 §4` 技术栈表（ComputeSharp 作门控可选增项、已在 CPM 登记，默认 GL compute）。

---

## 6. 依赖关系

前置（必须先完成或同步提供接口）：`plan/08-rendering.md`（**强依赖**——GL 上下文、render graph、世界纹理/emissive/occluder/bloom mip/合成 target、fragment 光照与 bloom 回退路径、CPU 粒子 stamp 路径、相机 uniform；本层是其增强，无 08 无法落地）；`plan/05-particles-lifecycle.md`（GPU 粒子绘制读其粒子 SoA 缓冲与 `activeCount`）；`plan/02-core-infrastructure.md`（诊断/计时器、`EngineConstants`、CPU 回退所用线程池）；`plan/04-materials-reactions-temperature.md`（emissive/发光材质语义、§4.3 降级链上下文）。

后置（消费本层能力）：`plan/12-editor-tooling-ui.md`（HUD 显示门控位/各 pass 耗时、fancy-lighting 与粒子模式开关、降级档调试）；`plan/15-build-packaging-distribution.md`（ComputeSharp 若采纳的 per-RID 打包与 AOT 处理）；`plan/16-performance-hardening.md`（GPU pass 的跨切面性能加固与 profiling 门禁）。

约束：依赖方向遵 `plan/00 §5`（Rendering 不被反向依赖）；本层只读 `plan/05` 缓冲、复用 `plan/08` 资源，不向 Simulation/Physics 引入新依赖；与架构 §3.3 相位顺序一致（全部在相位 10 渲染线程内）。

---

## 7. 提交节点

每完成一个节点立即用中文 git 提交（`AGENTS.md §6`，`scope=render`）：

1. `feat(render): GPU compute 后端抽象与能力门控(G1-G4)` —— §4.1 + §4.2 完成（`IComputeBackend`/三后端/`GpuCapabilities`/`ComputeCapabilityGate`/资源共享，含 G3 基线回退验证）。对应：本文档 §4.1、§4.2。
2. `feat(render): bloom 光照 GPU compute pass(bright-pass→mip→dual-Kawase)` —— §4.3 完成（CP-B1..B5、可选 CP-L0，与 plan/08 fragment 等价可切换）。对应：§4.3。
3. `feat(render): Radiance Cascades 高质量 2D GI 可选模式` —— §4.4 完成（CP-R0..R3、SDF/JFA、质量档、对 plan/08 透明）。对应：§4.4。
4. `feat(render): 高密度自由粒子 GPU point-sprite 批绘(含 emissive)` —— §4.5 完成（`GpuParticleRenderer`、SP-P1/P2、模式热切换、读 plan/05 缓冲）。对应：§4.5。
5. `feat(render): 可选非权威 air/smoke 扩散 compute pass(Margolus, 零回读)` —— §4.6 完成（CP-A1、default-off、非权威标注、不变式 #9 守恒）。对应：§4.6。
6. `feat(render): GPU compute profiling 与 §4.3 降级联动` —— §4.7 完成（timer query、自动降级序、HUD 接入）。对应：§4.7。

> 每个提交对应清单条目勾选；全部「验收标准」勾选后本文档方算完成（`AGENTS.md §7`）。任何与不变式/技术栈冲突项，标 `- [!] 阻塞：原因` 并上报，不写假实现绕过。
