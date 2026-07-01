# Plan 08 — CPU 侧渲染管线与 OpenGL 后端（PixelEngine.Rendering）

> 权威设计依据：`../docs/PixelEngine-架构与需求设计.md`（下称架构文档）§3.1、§3.3、§7.1、§9.1–§9.5、§4.2–§4.3、§12.1。技术栈与约定以 `00-conventions-and-techstack.md` 为准，开发宪法见 `../AGENTS.md`。
> 状态标记：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。
> 本文档只覆盖 **CPU 侧渲染编排 + OpenGL 后端封装**。纯 GPU 计算专项（Radiance Cascades、compute shader、GPU 粒子批绘）见 `09-gpu-compute.md`；ImGui 编辑器 UI 见 `12-editor-tooling-ui.md`（本文档负责提供其可共用的 GL 上下文与帧 present 时机）。

---

## 1. 目标与范围

本子系统是引擎的渲染后端 `PixelEngine.Rendering`，对应架构文档 §3.1 中「渲染后端」职责与 §9 全章。其核心事实是：**渲染器职责很轻**——每帧把一张视口大小的 BGRA8 世界纹理 blit 到屏幕，叠加自由粒子合成，再跑一遍光照/后处理 pass（架构 §9 开篇 [高]）。Sim 权威留在 CPU（不变式 #9、架构 §9.5），GPU 层只做「上传 + 合成 + shader」。

**范围（本文档逐项落地）**：

- **Silk.NET 后端封装**：窗口、输入上下文、GL 上下文的创建与生命周期（`Silk.NET.Windowing` / `Silk.NET.Input` / `Silk.NET.OpenGL`），目标 **OpenGL 3.3 Core 基线**（架构 §9.1、§9.5），运行时 **capability-gate** GL 4.3/4.4 特性（compute、persistent-mapped buffer），对问题驱动提供 **OpenGL ES 3.0 via ANGLE** 回退路径。
- **世界纹理流式**（性能关键路径，架构 §9.2）：单张视口大小纹理、**BGRA8 + `GL_UNSIGNED_INT_8_8_8_8_REV`**（internal `GL_RGBA8`）、**2-PBO ping-pong** 全帧上传（map 前 orphan、异步 DMA）+ dirty-rect `glTexSubImage2D` 子上传；明确反对 per-chunk 纹理。
- **render buffer 构建**（帧相位 9，并行，复用 Core 持久线程池/JobSystem）：从 `Material` id 采样材质纹理 + 温度 glow 生成 `uint` BGRA8；**颜色绝不入 cell**（不变式 #7、架构 §7.1）。
- **自由粒子 CPU stamp 合成**（帧相位 9，架构 §9.3）：按 `round(x),round(y)` 写颜色进 render buffer；发光粒子同时写 emissive buffer（供 bloom）。粒子缓冲读取契约见 `05-particles-lifecycle.md`。
- **光照管线**（帧相位 10，架构 §9.4）：emissive additive buffer、occluder/solidity map、可见性（每光源 1D shadow map 硬阴影 或 fog-of-war reveal 粗字节数组）、additive composite + bloom、dithering、gamma、可选 CRT/scanline。高质量 **Radiance Cascades** 模式委托 `09-gpu-compute.md`；光照/bloom 作为架构 §4.3 **第二级降级目标**。
- **相机/视口协作**（相机权威在 `PixelEngine.World`，见 `07-world-streaming-serialization.md`）、世界纹理 → 屏幕绘制、固定**合成顺序**（世界 → 粒子 → 角色/刚体高亮 → 光照 → bloom → dither → gamma → UI，架构 §9.3）。
- **带宽核算**与诊断/降级钩子接入。

**显式不在本文档范围内**（避免与他文档冲突）：

- GPU compute（含 Radiance Cascades GI、compute-based bloom 变体、可选非权威 sim pass、GPU 粒子 point-sprite 批绘）——全部归 `09-gpu-compute.md`，本文档只提供 capability-gate 与 hook 点。
- ImGui 面板/控件/编辑器逻辑——归 `12-editor-tooling-ui.md`；本文档仅暴露共用 GL 上下文、默认 framebuffer 与 present 时机。
- CA 内核、材质/反应表、温度场的产出语义——分别归 `03`/`04`，本文档只**只读消费**其 SoA 数组与材质纹理索引。
- 粒子积分/生命周期——归 `05`，本文档只在相位 9 只读消费活跃粒子数组。
- 相机平移/缩放/世界坐标逻辑——归 `07`，本文档只读消费 `CameraState`。

**不变式守护（本文档必须满足，违反即停并上报）**：颜色不入 cell（#7）；CPU sim 权威、不接受 GPU→CPU readback 卡流水线（#9，本文档不引入任何回读到 sim 的路径）；帧节奏不追帧（#6，渲染始终每帧出帧，sim 降频时复用上一世界纹理，架构 §4.2）。

---

## 2. 技术栈与依赖

与 `00-conventions-and-techstack.md` §4 选型表一致，不另立选型：

- **窗口/输入/GL**：`Silk.NET.Windowing`、`Silk.NET.Input`、`Silk.NET.OpenGL`（Silk.NET 2.x，MIT、.NET Foundation）。窗口后端用 Silk.NET 的 GLFW（默认）/SDL。目标 **OpenGL 3.3 Core**，capability-gate 4.3/4.4。
- **兼容回退**：**OpenGL ES 3.0 via ANGLE**（系统/动态分发，遵循不变式 #10「native 面收敛到 Box2D 一个依赖」，ANGLE 不作 vendored native）。
- **着色器**：手写 GLSL，`#version 330 core` 为基线，提供 `#version 300 es`（ANGLE/ES3）变体；用 `#define` 宏统一管理两套 profile 差异。
- **内存**：staging/render buffer 走 **POH（`GC.AllocateArray<uint>(n, pinned:true)`）或 `NativeMemory`**（`00` §4、架构 §14.3），保证到 PBO 的 `memcpy`/map 拷贝零 pin 开销、零托管堆分配（性能纪律 §3）。
- **并行**：render buffer 构建与粒子 stamp 复用 **Core 持久线程池 + barrier**（不用 per-frame `Parallel.For`，AGENTS §3、架构 §12.7），因相位顺序与 CA/physics 错开不冲突。
- **数学**：`System.Numerics`；与 GL 交互便利处可选 `Silk.NET.Maths`（`00` §4）。
- **诊断**：向 `PixelEngine.Core` 诊断/计时器注册分项耗时（`00` §7、架构 §4.3 降级依赖）。

**项目依赖方向**（`00` §5，绝不反向）：`Rendering → {World, Simulation, Content, Core}`。Rendering 只读消费 Simulation 的 SoA 网格、Content 的材质纹理/`MaterialDef[]`、World 的 `CameraState` 与 chunk 驻留视图、Particles 的活跃粒子数组（粒子池物理归 Simulation 程序集，见 `05`）。`.csproj` 开 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`（`00` §1/§6，PBO map 指针与 render buffer 漫游需要）。

**上游依赖文档**：`02-core-infrastructure.md`（线程池/barrier/POH/诊断/`EngineConstants`）、`03-simulation-kernel.md`（`CellGrid` SoA、chunk dirty rect 只读视图、`Material`/`Flags`/`Temperature` 数组）、`05-particles-lifecycle.md`（活跃粒子 `Span<Particle>`）、`04-materials-reactions-temperature.md`（材质纹理与温度 glow 参数）、`07-world-streaming-serialization.md`（`CameraState`/视口）。

---

## 3. 详细设计

### 3.1 后端封装与能力门控（架构 §9.1、§9.5）

`RenderWindow` 封装 `Silk.NET.Windowing.IWindow` + `Silk.NET.Input.IInputContext` + `Silk.NET.OpenGL.GL`。创建时请求 `ContextAPI.OpenGL`、`ContextProfile.Core`、version 3.3、`ContextFlags`（debug 构建加 `Debug`）。窗口的 `Render`/`Update`/`Resize`/`Closing` 事件不直接驱动引擎主循环——主循环由 `PixelEngine.Hosting` 拥有（架构 §3.3 十二相位），本子系统在相位 10 主动调用 GL 绘制并在帧末 `IWindow.SwapBuffers`/present。窗口运行用手动 pump（`window.DoEvents()` + 自管循环），以与固定逻辑步长帧节奏对齐（架构 §4，不让 Silk.NET 内部 `Run()` 抢节奏）。

`GlCapabilities` 在上下文创建后探测：`glGetString(GL_VERSION/GL_RENDERER/GL_VENDOR)`、`glGetIntegerv(GL_MAJOR_VERSION/GL_MINOR_VERSION)`、扩展枚举（`glGetIntegerv(GL_NUM_EXTENSIONS)` + `glGetStringi(GL_EXTENSIONS, i)`）。据此置位：`HasComputeShader`（GL≥4.3 或 `GL_ARB_compute_shader`）、`HasBufferStorage`（GL≥4.4 或 `GL_ARB_buffer_storage`，persistent-mapped 快车道）、`IsGles`（ANGLE/ES3 路径）。这些标志是 `09-gpu-compute.md` 启用 compute 路径的唯一开关；本文档的整条管线只用 3.3 Core 必备能力（`glTexSubImage2D` + PBO + FBO，架构 §9.5）。

`RenderBackend` 选择：默认 `DesktopGl33`；当桌面 GL 创建失败、或外部配置/命令行/检测到问题 Intel 老驱动时，回退 `GlEs30Angle`（ES3 仍支持 PBO，架构 §9.5）。回退是创建期一次性决策，运行期不切换。

GL 调试：debug 构建启用 `glDebugMessageCallback`（GL≥4.3）/`GL_KHR_debug`，把 GL 错误/性能警告转入 Core 诊断；release 不启用回调，改为关键调用后 `glGetError` 抽样（非热路径）。

### 3.2 GL 资源封装基础

薄封装、`IDisposable`、句柄即 `uint`，零每帧分配：

- `GlTexture`：2D 纹理，封装 `glGenTextures`/`glTexImage2D`/`glTexParameteri`（`GL_NEAREST` 过滤——像素游戏不插值，架构 §9 像素贴合）。世界纹理与各 FBO 附件用之。
- `GlBuffer`：VBO/PBO/UBO 统一封装；PBO 走 `GL_PIXEL_UNPACK_BUFFER` target。
- `Framebuffer`：FBO + 颜色附件管理，供光照/bloom 离屏 pass。
- `ShaderProgram`：编译/链接 GLSL，缓存 uniform location，提供强类型 setter；编译失败抛明确异常（库代码不吞异常，AGENTS §4）。
- `FullscreenQuad`：一个 VAO + 全屏三角形（避免对角线撕裂），所有 post pass 复用。
- 所有 GL 调用集中走 `GL` 实例；不在热路径 new 委托/闭包（性能纪律 §3）。

### 3.3 世界纹理流式（架构 §9.2，性能关键）

单张 `GlTexture`，尺寸 = 内部渲染分辨率（视口大小，非 per-chunk）。格式 `internalFormat=GL_RGBA8`、`format=GL_BGRA`、`type=GL_UNSIGNED_INT_8_8_8_8_REV`——与 CPU 端 BGRA8 内存布局逐字节匹配，上传是直 `memcpy`，避免逐像素 swizzle（实测 >25x，架构 §9.2 [高]）。**因此 render buffer 在 CPU 内存就按 BGRA8 存**（与架构 §7.1 `Render` 数组 `uint BGRA8` 一致）。

上传走 **2-PBO ping-pong**（`PboUploader`）：两个 `GL_PIXEL_UNPACK_BUFFER`，本帧写 buffer A、上帧的 buffer B 正被 GPU 异步 DMA。写前 **orphan**（`glBufferData(target, size, NULL, GL_STREAM_DRAW)` 丢弃旧存储，驱动给新后备内存避免同步停顿），再 `glMapBufferRange(GL_MAP_WRITE_BIT | GL_MAP_INVALIDATE_BUFFER_BIT | GL_MAP_UNSYNCHRONIZED_BIT)` 拿指针、`memcpy` render buffer 进去、`glUnmapBuffer`，最后 `glBindBuffer(GL_PIXEL_UNPACK_BUFFER, pbo)` + `glTexSubImage2D(..., offset=0)`（PBO 已绑定时 data 指针即 PBO 偏移）触发异步上传。

两种上传模式，按诊断/配置切换：

- **全帧上传**（默认，先实现）：整张 render buffer 进 PBO + 一次 `glTexSubImage2D` 覆盖全纹理。带宽充裕（见 §3.8），实现简单且正确。
- **dirty-rect 子上传**（CPU 侧优化，架构 §9.2 [高]：是省 memcpy/cache 压力而非 GPU 带宽必需）：从 Simulation 的 per-chunk dirty rect 只读视图聚合出本帧脏区，用 `glPixelStorei(GL_UNPACK_ROW_LENGTH, texWidth)` + `glPixelStorei(GL_UNPACK_SKIP_ROWS/SKIP_PIXELS)` 描述子区，逐脏矩形 `glTexSubImage2D`。dirty-rect 聚合复用 `03` 的 chunk dirty rect，不在渲染侧重算物理脏区。

**persistent-mapped 快车道**（可选，capability-gate `HasBufferStorage`）：`glBufferStorage(... GL_MAP_PERSISTENT_BIT | GL_MAP_COHERENT_BIT)` + 一次性 `glMapBufferRange`，配 `glFenceSync`/`glClientWaitSync` 做帧间同步。仅作 A/B 测试开关，**不作默认**（现场报告其在 HD 流式未必更快，PBO 保持可移植默认，架构 §9.2 [中]）。

明确反对：per-chunk 纹理（倍增 bind/draw call 并产生缝，架构 §9.2 [高]）。chunking 只用于 CPU sim 网格。

### 3.4 render buffer 构建（帧相位 9，并行）

`RenderBufferBuilder` 在架构 §3.3 **相位 9 [Build Render Buffer]** 运行，**并行**派发到 Core 持久线程池，按屏幕扫描行/行带（tile）划分任务，输出到 POH 的 `uint[]` render buffer（BGRA8）。对每个屏幕像素，由 `CameraState` 映射到世界 cell 坐标，读 `Material` id：

1. `id==Empty` → 背景色（或透明，由 composite 决定）。
2. 否则按 `MaterialDef.TextureId` **采样材质纹理**（`MaterialTextureProvider`，纹理像素按**世界坐标**取模采样，使同材质大片区有连续纹理而非屏幕锁定）；无纹理材质用 `BaseColorBGRA`。可选 `colorVariant`/`ColorNoise`（架构 §7.1/§7.3）：以世界坐标哈希出的廉价噪声扰动亮度，替代纹理。
3. 叠加**温度 glow**：从 1/4 分辨率温度场（架构 §7.5）双线性/最近邻采样该 cell 温度，超过材质发光阈值则按温度加色（加热金属/熔岩自发光）。
4. 写 BGRA8 进 render buffer。

**关键纪律**：本相位是 `Material` id + 温度的**只读**消费，绝不向 sim cell 写颜色（不变式 #7）。材质纹理采样、glow 计算全在 CPU（架构相位 9 文本图）。该 pass 是规则、分支统一的，可受益 SIMD（palette→BGRA 转色，架构 §12.5）——起步标量正确实现，热点处用 `Vector<T>`/Intrinsics + scalar fallback（性能纪律 §3，校验后启用）。

sim 降频时（架构 §4.2）：若本渲染帧未执行 sim step，相位 9 可跳过重建、直接复用上一帧 render buffer/世界纹理；相机平移用整图偏移采样而非像素插值（架构 §4.2）。该决策由 Hosting 通过 `RenderFrameContext.SimStepped` 标志传入。

### 3.5 自由粒子 CPU stamp 合成（帧相位 9，架构 §9.3）

`ParticleCompositor` 在相位 9（render buffer 之后、上传之前）把活跃自由粒子 stamp 进 render buffer。自由粒子不在 material 网格里，若不显式合成则飞行中不可见（架构 §9.3 修正缺口）。读 `05` 暴露的活跃粒子只读数组（`ReadOnlySpan<Particle>` + active-count，连续数组无虚调用迭代，架构 §7.6）：

- 对每粒子，由 `CameraState` 把浮点世界位置映射到屏幕，按 `round(x),round(y)` 写其颜色（材质色 + `colorVariant`）进 render buffer，使其与像素世界视觉一致并统一受后续光照/bloom 影响。
- **发光粒子（火花、熔渣等）同时写入 emissive buffer**（§3.6），从而正确产生 bloom 辉光。
- 视口裁剪：屏外粒子跳过。5万–20万粒子的顺序写很便宜（架构 §9.3 [中]）。

这是 **CPU stamp 默认路径**。高密度火花需亚像素/加色混合时的 GPU point-sprite 批绘是**备选**，归 `09-gpu-compute.md`（届时那批 sprite 也参与 emissive pass）；本文档只提供 emissive buffer 契约与 hook 点。

### 3.6 光照管线（帧相位 10，架构 §9.4）

`LightingPipeline` 在 **相位 10 [GPU Upload & Render]** 运行，先发 **Noita 式管线**（架构 §9.4 [高]），全部基于 3.3 Core fragment pass：

1. **emissive additive buffer**：来自发光材质（相位 9 由 `RenderBufferBuilder` 同时写出，按材质发光属性）+ 发光粒子（§3.5）。是一张离屏 `Framebuffer` 颜色附件（`GL_RGBA8`/可选 `GL_R11F_G11F_B10F` HDR，gate by capability）。
2. **occluder/solidity map**：从 cell 网格的固体性派生（相位 9 由 builder 顺带写出 1-bit/1-byte solidity，或独立轻 pass）。光照可见性的遮挡输入。
3. **可见性**，两条可选路径：
   - **每光源 1D shadow map 硬阴影**（mattdesl 式）：对每个动态光源，把周遭 occluder 投影到一维距离图（极角 → 最近遮挡距离），fragment 阶段比较得硬阴影。光源数有限（gate 上限）。
   - **fog-of-war reveal**（Noita 观感）：光源在 fog 上 punch 洞；fog 是**粗字节数组**（约 1 字节 / 32×32 区域，架构 §9.4），廉价、覆盖大世界。两路径可配置/并存。
4. **additive composite**：世界纹理 + emissive + 可见性结果加色合成（`glBlendFunc(GL_ONE, GL_ONE)` 或在 composite fragment 内做）。

**bloom**（`BloomPass`）：bright-pass（阈值提取高亮）→ mip 降采样 → **dual-Kawase**（默认，少 tap 高质量）或 separable Gaussian（水平/垂直两 pass，回退）→ additive upsample 回合成结果（架构 §9.4）。bloom 用 FBO + mip 链。

**降级**：`LightingPipeline.QualityLevel` 是架构 §4.3 **第二级降级目标**——降级顺序：关高质量 Radiance Cascades（该模式本就在 `09`）→ 关 bloom → 回退到 fog-of-war + emissive only。由 Core 诊断的过载信号驱动（§3.9），本子系统只暴露质量档位与开关。

### 3.7 后处理与合成编排（架构 §9.3 合成顺序）

`PostProcessStack` + `RenderPipeline` 编排固定**合成顺序**（架构 §9.3）：

```
世界纹理 → 粒子 stamp(已并入 render buffer) → 角色/刚体高亮 → 光照合成 → bloom → dither → gamma → UI
```

- **世界纹理 blit**：`world_blit` shader 把世界纹理按 `CameraState`（视口变换/缩放）绘到内部 framebuffer。相机权威在 `07`，本子系统只读 `CameraState`（位置、缩放、视口矩形）构造投影/采样 UV。
- **角色/刚体高亮**：Demo 角色（kinematic AABB，架构 §8.5）与刚体（架构 §8）的轮廓/高亮叠加层——本子系统提供绘制原语（`OverlayRenderer`：实色矩形/精灵/轮廓），具体内容由 Demo/Editor 通过公开 API 提交；不在渲染侧硬编码玩法。
- **dither**：抗 banding（有序 Bayer 矩阵或蓝噪声），在 gamma 前。
- **gamma**：线性 → sRGB 输出校正。
- **可选 CRT/scanline**：最终 pass 可选效果（开关）。
- **UI**：ImGui 在最后绘制——本子系统在 present 前暴露「UI 绘制时机」回调 + 共用 GL 上下文/默认 framebuffer，`12` 接入；本文档不实现 ImGui 内容。
- present：`SwapBuffers`。

### 3.8 带宽核算（架构 §9.2 [高]）

设计依据，写入诊断断言：1080p RGBA8 世界纹理 ~8MB，全帧 60fps 重传 ~480MB/s；720p ~3.7MB（~220MB/s）。相对 PCIe（数 GB/s）微不足道。**结论：全帧上传足够；dirty-rect 上传是 CPU 侧优化（省 memcpy/cache 压力），不是 GPU 带宽必需。** 因此实现先发全帧，profiling 显示 CPU 拷贝成本高时再启用 dirty-rect（§3.3）。瓶颈分析按延迟+分支而非带宽（AGENTS §3、架构 §12.7）。

### 3.9 诊断、降级与零分配

- 向 Core 诊断注册分项计时：相位 9 render buffer 构建、粒子 stamp、相位 10 PBO 上传、光照、bloom、post、present（架构 §4.3 降级与编辑器 HUD 依赖，`00` §7）。
- 暴露质量档位/开关给过载降级（§3.6，架构 §4.3 第二级）。降级**决策**逻辑在 Hosting/Core，本子系统只响应档位。
- **稳态帧循环零托管堆分配**（AGENTS §3、架构 §12.4）：render buffer/staging 用 POH/`NativeMemory`；无 LINQ/闭包/装箱/`params`/字符串拼接于相位 9/10 热路径；GL 句柄与 shader uniform location 启动期一次性缓存。

---

## 4. 实现清单

### 4.1 项目骨架与后端封装（架构 §9.1/§9.5）

- [x] 建立 `src/PixelEngine.Rendering/PixelEngine.Rendering.csproj`，`TargetFramework=net10.0`、`AllowUnsafeBlocks=true`、ProjectReference 至 `Core`/`Simulation`/`Content`/`World`（依赖方向遵 `00` §5，无反向）。
- [x] `RenderWindow`：封装 `IWindow`+`IInputContext`+`GL`；请求 OpenGL 3.3 Core profile，debug 构建加 `ContextFlags.Debug`；暴露手动 pump（`DoEvents`/`SwapBuffers`），不调用 Silk.NET 内部 `Run()`（与架构 §4 帧节奏对齐）。
- [x] `GlCapabilities`：`glGetString(GL_VERSION/RENDERER/VENDOR)`、`glGetIntegerv(GL_MAJOR_VERSION/GL_MINOR_VERSION/GL_NUM_EXTENSIONS)` + `glGetStringi(GL_EXTENSIONS,i)`；置位 `HasComputeShader`(≥4.3)、`HasBufferStorage`(≥4.4)、`IsGles`。
- [x] `RenderBackend` 选择：默认 `DesktopGl33`；桌面 GL 创建失败或配置指定时回退 `GlEs30Angle`（ES 3.0 via ANGLE，架构 §9.5），创建期一次性决策。
- [x] GL debug 回调：`glDebugMessageCallback`/`GL_KHR_debug`（capability-gate，debug 构建）转入 Core 诊断；release 关键调用后 `glGetError` 抽样。

### 4.2 GL 资源封装基础

- [x] `GlTexture`：`glGenTextures`/`glBindTexture`/`glTexImage2D`/`glTexParameteri`（`GL_NEAREST` min/mag、`GL_CLAMP_TO_EDGE`），`IDisposable`。
- [x] `GlBuffer`：VBO/PBO/UBO 统一封装；PBO 走 `GL_PIXEL_UNPACK_BUFFER`。
- [x] `Framebuffer`：FBO + 颜色附件 + 完整性检查（`glCheckFramebufferStatus`），离屏 pass 用。
- [x] `ShaderProgram`：`glCreateShader`/`glShaderSource`/`glCompileShader`/`glLinkProgram`，编译/链接失败抛明确异常并附 info log；缓存 uniform location。
- [x] `FullscreenQuad`：全屏三角形 VAO/VBO，所有 post pass 复用。
- [x] GLSL profile 管理：`#version 330 core` 基线 + `#version 300 es` 变体，宏统一差异（精度限定符、`texture()` 等）。

### 4.3 世界纹理流式（架构 §9.2，性能关键）

- [x] `WorldTexture`：单张视口大小 `GlTexture`，`internalFormat=GL_RGBA8`、`format=GL_BGRA`、`type=GL_UNSIGNED_INT_8_8_8_8_REV`（直 memcpy，>25x，架构 §9.2）；resize 随视口重建。
- [x] render buffer 后备存储：POH `GC.AllocateArray<uint>(w*h, pinned:true)` 或 `NativeMemory`（BGRA8，零 pin 开销、零托管分配，架构 §14.3）。
- [x] `PboUploader`（2-PBO ping-pong）：两个 `GL_PIXEL_UNPACK_BUFFER`；每帧 orphan（`glBufferData(...,NULL,GL_STREAM_DRAW)`）→ `glMapBufferRange(GL_MAP_WRITE_BIT|GL_MAP_INVALIDATE_BUFFER_BIT|GL_MAP_UNSYNCHRONIZED_BIT)` → `memcpy` → `glUnmapBuffer` → 绑定 PBO + `glTexSubImage2D`（异步 DMA）。
- [x] 全帧上传模式（默认）：整 render buffer → PBO → 一次 `glTexSubImage2D` 覆盖全纹理。
- [x] dirty-rect 子上传模式：聚合 `03` 的 per-chunk dirty rect 只读视图，`glPixelStorei(GL_UNPACK_ROW_LENGTH/SKIP_ROWS/SKIP_PIXELS)` 描述子区，逐脏矩形 `glTexSubImage2D`；运行期可切换（默认关，profiling 触发）。
- [x] persistent-mapped 快车道（可选，gate `HasBufferStorage`）：`glBufferStorage(...GL_MAP_PERSISTENT_BIT|GL_MAP_COHERENT_BIT)` + `glFenceSync`/`glClientWaitSync`，仅作 A/B 开关、非默认。
- [x] 明确反对 per-chunk 纹理：仅单张视口纹理（代码注释引用架构 §9.2，禁止 per-chunk 上传纹理）。

### 4.4 render buffer 构建（帧相位 9，并行）

- [x] `RenderBufferBuilder.Build(...)`：相位 9 并行（Core 持久线程池，按屏幕行带 tile 划分），输出 BGRA8 render buffer。
- [x] 世界→屏幕映射：由 `CameraState`（来自 `07`）把屏幕像素映射到世界 cell 坐标，只读 `Material` id。
- [x] `MaterialTextureProvider`：按 `MaterialDef.TextureId` 采样材质纹理，UV 用**世界坐标**取模（同材质大片连续）；无纹理用 `BaseColorBGRA`；可选 `colorVariant`/`ColorNoise` 世界坐标哈希噪声扰动亮度（架构 §7.1/§7.3）。
- [x] 温度 glow：从 1/4 分辨率温度场（架构 §7.5）采样，超发光阈值按温度加色，写入 BGRA8。
- [x] emissive 与 solidity 副输出：builder 同时写出 emissive buffer（发光材质）与 occluder/solidity map（供 §4.6）。
- [x] 守护不变式 #7：本 pass 只读 `Material`/`Temperature`，绝不向 sim cell 写颜色（代码注释引用架构 §7.1，CI 评审项）。
- [x] sim 降频复用：`RenderFrameContext.SimStepped==false` 时跳过重建、复用上帧 render buffer，相机平移用整图偏移采样（架构 §4.2）。
- [x] SIMD 转色（palette→BGRA）：标量正确实现优先；热点处 `Vector<T>`/Intrinsics + scalar fallback，反汇编/基准校验后启用（架构 §12.5）。已接入零分配 palette fast path；AVX2 gather 经 BDN/disasm 验证但在 Ryzen 5800X/.NET 10 上慢于标量，因此保留为显式实验路径，不作为默认热循环。

### 4.5 自由粒子 CPU stamp 合成（帧相位 9，架构 §9.3）

- [x] `ParticleCompositor.Stamp(...)`：相位 9（render buffer 之后、上传之前）消费 `05` 暴露的活跃粒子 `ReadOnlySpan<Particle>` + active-count（连续数组无虚调用，架构 §7.6）。
- [x] 屏幕映射 + `round(x),round(y)` 写材质色（+`colorVariant`）进 render buffer；视口裁剪屏外粒子。
- [x] 发光粒子（火花/熔渣）同时写 emissive buffer（供 bloom，架构 §9.3）。
- [x] 提供 emissive buffer 契约与 GPU point-sprite hook 点（实际 GPU 批绘归 `09`，本文档不实现）。

### 4.6 光照管线（帧相位 10，架构 §9.4）

- [x] `EmissiveBuffer`：离屏 `Framebuffer` 颜色附件，承接发光材质 + 发光粒子（HDR 格式 `GL_R11F_G11F_B10F` 可选，gate capability）。
- [x] `OccluderMap`/solidity map：从 cell 固体性派生（builder 副输出或独立轻 pass），作可见性遮挡输入。
- [x] `ShadowMap1DPass`：每光源 1D shadow map 硬阴影（极角→最近遮挡距离，mattdesl 式），光源数 gate 上限；`shadow_1d.frag`。
- [x] `FogOfWarBuffer`：粗字节数组（~1 字节/32×32 区，架构 §9.4），光源 punch 洞 reveal；与 1D shadow 可配置/并存。
- [x] `CompositePass`：世界纹理 + emissive + 可见性 additive 合成（`glBlendFunc(GL_ONE,GL_ONE)` 或 `composite.frag` 内加色）。
- [x] `BloomPass`：bright-pass(`bright_pass.frag`) → mip 降采样 → **dual-Kawase**(`kawase_down.frag`/`kawase_up.frag`，默认) 或 separable Gaussian(`gaussian_blur.frag` 水平/垂直，回退) → additive upsample。
- [x] `LightingPipeline.QualityLevel` 降级档位（架构 §4.3 第二级）：关 Radiance Cascades(归 `09`)→关 bloom→fog-of-war+emissive only；响应 Core 诊断过载信号（决策不在本子系统）。
- [x] capability hook：`HasComputeShader` 为真时把高质量光照/RC 委托 `09`，本管线只保留 fragment 路径与 hook 点。

### 4.7 后处理与合成编排（架构 §9.3）

- [x] `RenderPipeline.RenderFrame(...)`：相位 10 主入口，固定合成顺序：世界 blit → (粒子已并入) → 角色/刚体高亮 → 光照合成 → bloom → dither → gamma → UI → present。
- [x] `world_blit.vert/frag`：世界纹理按 `CameraState`（视口/缩放）绘到内部 framebuffer，`GL_NEAREST` 采样。
- [x] `OverlayRenderer`：实色矩形/精灵/轮廓绘制原语，供 Demo/Editor 通过公开 API 提交角色/刚体高亮（不硬编码玩法内容）。
- [x] `DitherPass`：有序 Bayer/蓝噪声抗 banding（gamma 前）；`post_final.frag` 内或独立 pass。
- [x] `GammaPass`：线性→sRGB 输出校正。
- [x] 可选 `CrtPass`/scanline：最终 pass 开关。
- [x] UI present hook：present 前暴露「UI 绘制时机」回调 + 共用 GL 上下文/默认 framebuffer 给 `12`（ImGui 内容不在本文档）；最后 `SwapBuffers`。

### 4.8 相机/视口协作（与 `07`）

- [x] 只读消费 `CameraState`（位置/缩放/视口矩形，权威在 `07`/`PixelEngine.World`），构造世界 blit 投影与 render buffer 世界坐标映射；本子系统不持有相机权威。
- [x] 视口 resize：`RenderWindow.Resize` → 重建 `WorldTexture`/render buffer/FBO 链，更新内部渲染分辨率。

### 4.9 诊断、降级与零分配

- [x] 向 Core 诊断注册分项计时器：相位 9（render buffer 构建、粒子 stamp）、相位 10（PBO 上传、光照、bloom、post、present）。
- [x] 暴露光照/bloom 质量档位与开关给过载降级（架构 §4.3 第二级）。
- [x] 公开 API 全带中文 XML 文档注释（脚本/编辑器 IntelliSense 依赖，`00` §7、AGENTS §4）。
- [x] 稳态帧循环零托管堆分配：POH/`NativeMemory` 后备、无 LINQ/闭包/装箱于相位 9/10 热路径、句柄/uniform location 启动期缓存（基准校验，AGENTS §3）。

### 4.10 带宽与上传基准

- [x] BenchmarkDotNet 基准：全帧上传 vs dirty-rect 上传的 CPU memcpy/上传耗时（1080p/720p），校验 §3.8 带宽核算结论，据此定档默认上传模式。

---

## 5. 验收标准

- [x] OpenGL 3.3 Core 上下文在 win-x64 成功创建并出帧；`GlCapabilities` 正确报告 version/extensions 与 `HasComputeShader`/`HasBufferStorage`/`IsGles`。
- [x] 桌面 GL 创建失败路径能回退到 OpenGL ES 3.0 via ANGLE 并正常出帧（架构 §9.5），整条管线仅用 3.3 Core/ES3 必备能力（`glTexSubImage2D`+PBO+FBO）。已增加实际 GLES 上下文校验与 `PIXELENGINE_RENDERING_ANGLE_SMOKE=1` 显式 smoke；Auto fallback 由后端尝试顺序与失败续尝试覆盖。
- [x] 世界纹理为单张视口大小纹理、BGRA8 + `GL_UNSIGNED_INT_8_8_8_8_REV`；CPU render buffer 到 PBO 为直 `memcpy`（无逐像素 swizzle）；代码中无 per-chunk 纹理上传路径。
- [x] 2-PBO ping-pong 全帧上传工作：map 前 orphan、`GL_MAP_INVALIDATE_BUFFER_BIT|GL_MAP_UNSYNCHRONIZED_BIT`、异步 DMA，无每帧同步停顿（显式 GL smoke 覆盖上传路径；上传阻塞基准随 §4.10 补齐）。
- [x] dirty-rect 子上传模式：聚合 `03` 的 chunk dirty rect 正确子上传，画面与全帧上传逐像素一致（子上传参数校验 + 显式 GL smoke 覆盖，逐像素一致性随后续 render buffer 构建闭合）。
- [x] 相位 9 render buffer 构建在 Core 持久线程池上并行（非 per-frame `Parallel.For`），输出 BGRA8；颜色由材质纹理(世界坐标采样)+温度 glow 生成。
- [x] 守护不变式 #7：sim cell 不含 RGBA；渲染色在相位 9 生成（代码评审 + 无任何向 `CellGrid` 写颜色的调用）。
- [x] 守护不变式 #9：渲染全程无 GPU→CPU readback 回到 sim 网格的路径。
- [x] sim 降频时（架构 §4.2）渲染仍每帧出帧、复用上帧世界纹理、相机平移整图偏移而非像素插值（不变式 #6 不追帧）。
- [x] 自由粒子在相位 9 按 `round(x,y)` stamp 进 render buffer 后飞行中可见；发光粒子同时进 emissive buffer 并在 bloom 中产生辉光（架构 §9.3）。
- [x] 光照管线产出 emissive additive + occluder/solidity + 可见性（1D shadow map 硬阴影 与/或 fog-of-war reveal）+ additive composite + bloom(dual-Kawase 默认/Gaussian 回退) + dither + gamma，可选 CRT/scanline。
- [x] 合成顺序严格为：世界 → 粒子 → 角色/刚体高亮 → 光照 → bloom → dither → gamma → UI（架构 §9.3）。
- [x] 光照/bloom 可按 `QualityLevel` 降级（关 RC→关 bloom→fog-of-war+emissive only），响应 Core 诊断信号（架构 §4.3 第二级）。
- [x] 高质量 Radiance Cascades/compute 路径仅通过 capability hook 委托 `09`，本文档不含 compute 实现。
- [x] ImGui 通过共用 GL 上下文 + present 前 UI 回调接入（`12`），本文档不含 ImGui 控件实现。
- [x] 相机为只读消费 `07` 的 `CameraState`，本子系统不持有相机权威；视口 resize 正确重建纹理/FBO 链。
- [x] 带宽基准确认：1080p 全帧上传 ~480MB/s 量级、远低于 PCIe；默认上传模式据基准定档（架构 §9.2）。
- [x] 稳态帧循环（相位 9/10）零托管堆分配（BenchmarkDotNet `MemoryDiagnoser` 确认 Gen0=0）。
- [x] 所有公开 API 带中文 XML 文档注释。
- [x] 本文档「技术栈与依赖」不与 `00-conventions-and-techstack.md` §4 冲突（验收时核对）。

---

## 6. 依赖关系

**上游（必须先于本文档可用）**：

- `00-conventions-and-techstack.md`：选型/解决方案结构/全局约定。
- `02-core-infrastructure.md`：持久线程池+barrier、POH/`NativeMemory` 封装、诊断/计时、`EngineConstants`。
- `03-simulation-kernel.md`：`CellGrid` SoA（`Material`/`Flags`/`Temperature`）只读视图、per-chunk dirty rect 只读视图。
- `04-materials-reactions-temperature.md`：材质纹理/`BaseColorBGRA`/glow 参数、温度场。
- `05-particles-lifecycle.md`：活跃自由粒子只读数组契约（相位 9 消费）。
- `07-world-streaming-serialization.md`：`CameraState`/视口、chunk 驻留视图。

**下游（依赖本文档）**：

- `09-gpu-compute.md`：基于本文档的 `GlCapabilities` capability-gate 与 emissive/光照 hook 点实现 Radiance Cascades/compute/GPU 粒子批绘。
- `12-editor-tooling-ui.md`：基于本文档共用 GL 上下文 + present UI 回调实现 ImGui 面板。
- `13-demo-game.md`：通过 `OverlayRenderer` 公开 API 提交角色/刚体高亮。

**协作（同相位/相邻相位，靠相位顺序非锁，架构 §3.3）**：相位 8（physics 栅格化）写完网格 → 相位 9 本子系统只读消费；相位 10 present 后进入下一帧相位 0。

**并行可行性**（`README.md` 执行顺序）：08 可与 04/05 并行起步，但全帧出图验收需 03（CellGrid）就绪；精确依赖以 `17-roadmap-execution-order.md` 为准，前置未完成不得声称本文档完成。

---

## 7. 提交节点

按 AGENTS §6，每个节点完成即用中文 git 提交（`type(scope): 简述`，scope=`render`）：

- [x] `feat(render): Silk.NET 窗口/GL3.3 上下文与能力门控(ANGLE 回退)`（§4.1+§4.2）— 对应本文档 §实现清单 4.1/4.2。
- [x] `feat(render): 世界纹理 BGRA8 流式与 2-PBO ping-pong 上传`（§4.3）— 含全帧/dirty-rect/persistent-mapped。
- [x] `feat(render): 相位9 并行 render buffer 构建(材质纹理+温度 glow)`（§4.4）— 守护不变式 #7。
- [x] `feat(render): 相位9 自由粒子 CPU stamp 合成与 emissive 副输出`（§4.5）。
- [x] `feat(render): 相位10 光照管线(emissive/occluder/可见性/composite)`（§4.6 前半）。
- [x] `feat(render): bloom(dual-Kawase) 与 dither/gamma/CRT 后处理`（§4.6 bloom + §4.7）。
- [x] `feat(render): 合成编排/相机协作/诊断降级与零分配加固`（§4.7/§4.8/§4.9）。
- [x] `test(render): 上传与零分配/带宽基准并定档默认上传模式`（§4.10 + §5 基准类验收）。
