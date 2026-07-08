# Plan 10 — 音频子系统（PixelEngine.Audio）

> 范围：引擎音频全部。权威设计依据：架构文档 §10（§10.1 选型、§10.2 材质驱动事件钩子、§10.3 与帧节奏关系），并受 §1.4（帧时间预算「游戏逻辑 + 音频派发 ≤1ms」）、§3.3（帧相位）、§4.2（sim 降频）、§7.3（`MaterialDef.AudioCues` 字段）、§3.1（Core 事件总线供音频消费）约束。技术栈以 `00-conventions-and-techstack.md` 为准，开发宪法见 `../AGENTS.md`。
> 状态约定：`- [x]` 已有源码、测试、工具、报告或 plan 证据；`- [ ]` 未完成目标；`- [!]` 阻塞、证据债、人工验收或外部环境限制。

---

## 1. 目标与范围

本子系统把模拟世界中发生的「值得发声」的离散事件，转化为有空间定位、随材质变化、且永不拖垮帧预算的声音。它是「引擎 + Demo」交付物的一等组成部分：材质化音效（沙落、火噼啪、液体飞溅、爆炸、刚体破碎、熔岩咕嘟）是 Noita 级观感不可分割的听感一半（架构 §10 起首）。

设计的根本约束有三条，全部来自架构不变式与性能纪律。其一，**音频绝不进入 sim 热循环**：混音与解码留在 OpenAL Soft 自身的音频线程 / 回调线程，主线程每帧只做廉价的「事件入队 + 排空 + 设源参数 + 触发播放」，计入 §1.4 的「游戏逻辑 + 音频派发 ≤1ms」预算（架构 §10.1、§10.3）。其二，**事件粒度恒为 per-event，绝不 per-cell**：满屏沙落不得逐 cell 发声，否则瞬间数百万事件压垮一切——必须强制限频去重（架构 §10.2）。其三，**稳态帧循环内零托管堆分配**：派发路径用预分配 ring、对象池、`Span<T>`，无 LINQ / 闭包 / 装箱（AGENTS §3）。

本文档**只**覆盖音频：OpenAL 封装（设备 / 上下文 / listener / 3D positional source 池 / 距离衰减）、从 Core 事件总线消费粗粒度音频事件、材质化事件类型与发声规则、`MaterialDef.AudioCues` 映射、强制限频去重、按相机 / 听者的 positional pan 与衰减、与帧节奏（sim 降频）的一致性、以及音效资产的加载与播放接口。

明确**不在本文档范围**：事件的产生（相位钩子的调用点位于 plan/03 CA 内核、plan/05 粒子、plan/06 物理，本文档仅定义其发声契约与事件分类）；事件 ring buffer 的底层实现与 `AudioEvent` 物理类型定义（位于 plan/02 Core 事件总线，本文档为消费方并列出所需契约）；音效资产文件本身（由 Content 资产管线提供，本文档定义加载与播放接口）；GPU、渲染、物理、脚本、编辑器。

不追求：HRTF 双耳渲染、动态混响 / 卷积、音乐自适应编排（DSP 链与音乐系统不在本期；接口预留但不实现，不写假实现）。

---

## 2. 技术栈与依赖

音频后端按 `00 §4` 与架构 §10.1 / §13 定稿为 **Silk.NET.OpenAL（OpenAL Soft 运行时）**：与 Silk.NET 渲染栈一致、MIT、跨 Win/Linux/Mac、内建 3D positional source 与距离衰减模型、AOT/trim 友好。OpenAL 走系统 / 动态分发，**不新增 native 静态依赖**，符合不变式 10「native 面收敛到 Box2D 一个依赖」。互操作全部由 Silk.NET.OpenAL 的 `[LibraryImport]` 绑定承担，本子系统不写任何 `DllImport`（AGENTS §3 互操作纪律）。

混音：OpenAL Soft 在其内部音频线程混音与重采样，本子系统不实现混音器。解码：短音效在加载期解码为 PCM 静态 buffer；长循环 / ambient 走 OpenAL 队列 buffer 流式播放，refill 在专属解码线程，**均不在主线程帧循环内**（架构 §10.1）。

容器格式：**WAV / PCM 为强制内建路径**（纯托管解码，无新依赖，在本文档范围内实现）。压缩格式 Ogg Vorbis 经纯托管解码器（NVorbis，无 native，符合不变式 10）支持；NVorbis 已列入 `00 §4` 可选增强技术栈。

本子系统位于 `src/PixelEngine.Audio/`，依赖方向遵循 `00 §5`：`Audio → {Content, Simulation, Core}`，不反向依赖任何上层。`AllowUnsafeBlocks` 开启（与热路径项目一致）；公开 API 全带中文 XML 文档注释（脚本 / Demo IntelliSense 依赖）。

跨文档契约（本子系统消费，非本文档实现）：

- plan/02 Core 提供：无锁 ring buffer 传输（MPSC，生产者为 sim/physics 多线程 worker、消费者为主线程派发）、blittable 事件结构 `AudioEvent` 与枚举 `AudioEventType`（按架构 §3.1「事件总线供音频消费」置于 Core 以便跨层生产者可见）、持久线程池（供解码 / 流式 worker 复用，避免与 sim 争用）、`EngineTime`（当前 sim tick / 帧序号）、相机视口 `CameraView`（listener 定位）、诊断 / 计时注册口、数学类型（`Vector2` 等）。
- plan/04 / Content 提供：materials JSON 中的 `audioCues` 字段加载；`MaterialDef.AudioCues` / `AudioCueSet` 类型按 `plan/00 §5.1` 归属 Simulation，Audio 只读取已加载的运行时材质定义并按 materialId 索引 cue。
- Content 资产管线提供：按 `assetId` 取音效字节流（来自 `content/` 音效目录）。
- plan/03、05、06 在各自相位调用发声契约（§3.2）把 `AudioEvent` 写入 ring；其调用点实现属于这些文档。
- Hosting 在帧循环装配「音频派发步骤」（§3.7）并提供 `CameraView`、`AudioSettings`。

---

## 3. 详细设计

### 3.1 子系统装配与生命周期

`AudioSystem` 是子系统门面，由 Hosting 在引擎装配期创建、在帧循环的音频派发步骤驱动、在关停时释放。它聚合：`OpenAlDevice`（设备 / 上下文 / listener）、`AudioVoicePool`（positional source 池）、`AmbientLoopManager`（材质化 ambient）、`AudioDispatcher`（每帧排空 + 限频 + 派发）、`MaterialAudioTable`（材质→cue 解析）、`AudioClipCache`（资产加载）、`AudioDiagnostics`（计时接入）。Hosting 创建的 `AudioSystem` 已纳入 `Engine` owned runtime resources；当 `AudioSystem` 拥有 `AudioClipCache` 时，`Shutdown` 会释放已上传的后端 buffer，避免反复创建 / 销毁引擎时残留 OpenAL buffer。

`AudioSystem` 公开：`Initialize(AudioSettings settings, IAudioBackend? backend = null)`、`AttachClipCache(AudioClipCache cache, bool takeOwnership = false)`、`Shutdown()` 等生命周期入口；每帧 listener / dispatch / ambient 推进由 Hosting 的 `AudioPhaseDriver` 组合 dispatcher、cue table 与 listener view 执行。所有可被 Demo / 脚本直接调用的播放入口属于本子系统的**公开 API**（Showcase Demo Game 仅依赖公开 API，AGENTS §0），见 §3.8。

`IAudioBackend` 抽象后端，默认实现 `OpenAlBackend`（OpenAL Soft）。抽象的目的不是做多后端 MVP，而是让单元测试可注入 `NullAudioBackend`（无声、记录调用）以验证限频 / 去重 / 派发逻辑而无需真实设备。

### 3.2 事件来源与发声契约（per-event，绝不 per-cell）

sim 不直接播声，而是在「被执行的 tick」内、在对应相位把**粗粒度** `AudioEvent` 写入 Core 的 MPSC ring（架构 §10.2）。每个事件携带世界 cell 坐标、材质 id、强度量（`Magnitude`）。事件分类 `AudioEventType` 与各自的相位钩子、发声规则如下（钩子调用属 plan/03/05/06，本文档定义分类与规则契约）：

`ParticleImpact`（impact）——自由粒子高速沉积时（相位 3，particle→cell）。仅当沉积速度超过该材质 `AudioCues.ImpactMinSpeed` 才发；`Magnitude` = 归一化冲击速度，用于选样本 / 调音量。架构 §3.3 相位 3、§7.6。

`FireCrackle`（fire crackle）——燃烧 / 点燃持续（相位 4 反应或相位 5 热场相变）。**绝不每火 cell 一声**：在产生侧按区域聚合（每 chunk 或粗网格每 N tick 至多一个事件），`Magnitude` = 区域燃烧 cell 占比；消费侧再作 ambient 化。架构 §3.3 相位 4/5、§7.4/§7.5。

`LiquidSplash`（splash）——液体大量铺开 / 飞溅落水（相位 3 粒子落水，或相位 4 液体跨阈位移聚合）。`Magnitude` = 飞溅规模。架构 §3.3 相位 3/4。

`Explosion`（explosion）——爆炸 / 冲击把 cell 抛为粒子（相位 7，cell→particle）。一次爆炸一个事件，`Magnitude` = 冲击半径 / 能量。架构 §3.3 相位 7、§7.6。

`RigidbodyShatter`（shatter）——刚体破碎 / 拆分（相位 8a，CCL 重建产生 N≥1 子体时）。每次破碎一个事件，`Magnitude` = 失去像素量 / 碎片数。架构 §3.3 相位 8a、§8.4。

`AmbientRegion`（材质化 ambient loop）——相机附近某材质占比高（如熔岩咕嘟、深水低频）。由低频粗采样产生侧（每 N tick 采样可见 chunk 的 top-K 材质直方图）发出 hint 事件，`MaterialId` = 主导材质、`Magnitude` = 占比、坐标 = 区域中心。粗采样实现属 plan/03/07（产生侧），本文档定义 ambient 的消费、淡入淡出与滞回（§3.5）。架构 §10.2。

发声所需的判定阈值（如 `ImpactMinSpeed`、ambient 占比阈值）由 `MaterialDef.AudioCues` 携带（§3.6），产生侧本就读 `MaterialDef`，不引入额外耦合。**所有发声规则均保证一个相位事件，不随 cell 数膨胀**；满屏同类事件的洪峰由消费侧限频去重兜底（§3.4），产生侧亦应作区域聚合作为第一道闸。

### 3.3 OpenAL 封装：设备、上下文、listener、source 池

`OpenAlDevice` 封装 Silk.NET.OpenAL 的 `ALContext` 与 `AL`：打开默认设备、创建并 make-current 上下文、设置全局距离模型（默认 `AL_INVERSE_DISTANCE_CLAMPED`，可经 `AudioSettings` 切线性）、配置 listener 增益。打开失败（无音频设备 / headless CI）时进入静默降级模式：构造成功但不出声、记录诊断，绝不抛使引擎崩溃的异常（库代码不吞致命异常但音频缺失属预期失败，走 `bool TryInitialize`）。

`AudioListenerState` 持有 listener 的位置 / 朝向 / 速度，`UpdateListener(in CameraView camera)` 每帧从相机中心更新。2D 场景约定：listener 置于相机中心 `(camX, camY, ListenerDepth)`，朝向 `(0,0,-1)`、up `(0,1,0)`；声源置于 `(worldX, worldY, 0)`，世界 cell 坐标经 `AudioSpace.ToMeters(cellX, cellY)` 按 `AudioSettings.PixelsPerMeter`（默认与可调常量，建库即定）换算为 OpenAL 米空间。如此 pan 由 x 偏移、衰减由欧氏距离自然 emergent，无需手算声像。

`AudioVoicePool` 管理 positional source 池：启动期预创建固定数量 `AudioVoice`（每个封装一个 AL source id、`AL_SOURCE_RELATIVE=false`、可配 `AL_REFERENCE_DISTANCE` / `AL_MAX_DISTANCE` / `AL_ROLLOFF_FACTOR`）。`Acquire(byte priority, AudioEventType type)` 取空闲 voice；池满时按**voice stealing** 策略抢占：优先抢已停止的，其次抢优先级更低、距 listener 更远、播放更久者（`AudioVoice.StealScore` 综合距离 + 优先级 + 年龄）。voice 不足是常态而非错误，被丢弃的事件计入诊断。`AudioVoice.Play(AudioBuffer, in Vector3 pos, float gain, float pitch)`、`Stop()`、`IsFinished` 轮询基于 `AL_SOURCE_STATE`。

source 数量上限：OpenAL Soft 单声道 source 充裕（通常上百），池大小（默认如 64 positional + 若干 ambient + UI）由 `AudioSettings.MaxVoices` 配置，按目标机实测定档。

### 3.4 限频去重（强制）

`AudioEventCoalescer` 是防过载的核心，与 sim 性能纪律同理（架构 §10.2、AGENTS §3）。它在每帧排空 ring 时对事件流做两级压制，全程零分配（固定容量结构）：

按类型每帧上限：每个 `AudioEventType` 持有本帧计数与上限 `PerTypeCap`（如 impact ≤ 8、splash ≤ 6、shatter ≤ 4，可配）。超上限的同类事件被合并到既有桶或丢弃（保留 `Magnitude` 最大者），计入 `Dropped` 诊断。

近坐标合并：把世界坐标量化到合并网格（`CoalesceBucketSize`，如 64px = 1 chunk）形成 `(type, bucketX, bucketY)` 键，落同桶的同类事件合并为一个代表事件——取最大 `Magnitude`、累加 `Count`（供消费侧据密度微调音量 / 选择更「群体」的样本）、坐标取桶中心或加权均值。空间哈希用预分配的开放寻址表（容量按 `PerTypeCap × 类型数` 上界），帧末整体清空（翻转 / memset，不重建）。

冷却去重：`CooldownTracker` 对 `(materialId, type)` 维护上次发声 tick，间隔小于 `AudioCue.CooldownTicks` 的二次触发被抑制，防同一材质同一处高频抖动刷声。冷却表为定容环形 / 小哈希，零分配。

去重在派发**之前**完成；产生侧的区域聚合是第一道闸、消费侧的 coalescer 是强制兜底闸，二者叠加保证「满屏沙落」不会产生与画面像素数成正比的音频负载。

### 3.5 材质化 ambient loop

`AmbientLoopManager` 消费 `AmbientRegion` 事件，维护按 `materialId` 键的循环声部 `AmbientVoice`（持久 AL source + `AL_LOOPING` 或队列流式）。逻辑为**带滞回的交叉淡变**：当某材质占比 hint 高于 `EnterThreshold` 且本帧仍有 hint，目标增益升向 `Magnitude` 映射值；连续若干帧无 hint 或低于 `ExitThreshold` 则目标增益降至 0，归零后回收声部。增益每帧朝目标线性插值（`AmbientFadeRate`），位置跟随该材质区域中心做缓动，避免相机移动时 ambient 跳变。`EnterThreshold > ExitThreshold` 形成滞回，防边界处反复启停。

ambient 声部独立于 §3.3 的一次性 voice 池，使用单独的小型 source 集合（`AudioSettings.MaxAmbientVoices`），不参与 voice stealing，保证背景音稳定。

### 3.6 `MaterialDef.AudioCues` 映射

`MaterialAudioTable` 把 materialId 映射到稳定 cue handle 与默认播放参数，是事件→具体 buffer 的解析层。`ResolveCue(ushort materialId, AudioEventType type)` 返回一个 `MaterialAudioPlayback`，包含 cue handle、音量、音高和优先级；其中音量 / 音高按 `Magnitude` / `Count` 插值并带确定性 pitch 抖动。表在初始化时由 `MaterialDef[]` 一次性扁平化为按 `materialId × AudioEventType` 索引的数组，热路径零字典 / 零字符串（与架构 §7.4「加载期展开、运行时零字符串」同理）。

`AudioCueSet` 的数据形状沿用 plan/04 已完成契约：`ImpactCue` / `FireCue` / `SplashCue` / `ExplosionCue` / `ShatterCue` / `AmbientCue` 均为内容侧稳定 cue handle，0 表示未配置。`MaterialAudioPlayer` 通过 `IAudioCueBufferResolver` 把 cue handle 解析为已加载 OpenAL buffer；真实音频资产、clip cache、WAV 解码和流式播放由 §3.8 / 节点 4 接入。未配置某类型 cue 的材质对该类型静默（`ReactionCount==0` 式早退，零开销）。

### 3.7 与帧节奏的关系与音频派发步骤

音频派发是主线程步骤，紧随相位 8（physics 同步）之后、相位 10（present）之前执行，与相位 9–10 的 render buffer 构建 / 上传逻辑上并列于帧尾，计入 §1.4 的「游戏逻辑 + 音频派发 ≤1ms」（即架构 §17.1 overlay 中的「audio 派发」分项）。该步骤只做：`UpdateListener` → `AudioDispatcher.Dispatch`（排空 MPSC ring → coalescer 限频去重 → 冷却过滤 → `MaterialAudioTable` 解析 → `AudioVoicePool.Acquire` + 设位置 / 增益 / 音高 + `Play`）→ `AmbientLoopManager.Update`。全部为廉价的 AL 调用与定容结构操作，混音 / 解码不在此发生。

与 sim 降频一致（架构 §4.2、§10.3）：音频事件只在「被执行的 sim tick」产生（产生侧相位 3–8 仅在该 tick 运行），故 sim 降到 30Hz 时事件密度自然减半，听感与画面一致，无需音频侧特殊处理。当某帧 `simSteppedThisFrame==false`（render-only 帧），派发步骤仍每帧运行以推进 voice 状态 / ambient 淡变 / listener 跟随相机，但 ring 通常为空——保证画面平移时声场连续。

### 3.8 资产加载与播放接口（公开 API）

`AudioClipCache` 提供 `ValueTask<AudioClip> LoadAsync(string assetId)`：经 Content 资产管线取字节，按容器选 `IAudioDecoder`（`WavDecoder` 内建；`OggVorbisDecoder` 使用 NVorbis 纯托管路径）解码为 PCM，上传为 `AudioBuffer`（封装 AL buffer id + 采样率 / 声道 / 位深）。短音效全量解码为静态 buffer；长循环 / ambient 经 `AudioStreamPlayer` 用 OpenAL 队列 buffer 流式（双 / 三 buffer），refill 在解码 worker（Core 线程池后台或专属 `AudioStreamThread`），**绝不在主线程**。`AudioClip` 引用计数，单个 clip 可由 `Unload` 释放；cache 也实现 `IDisposable`，由 owning `AudioSystem` 在 shutdown 时批量删除仍缓存的后端 buffer。解码异步，加载中以静默占位（不阻塞，不假实现——未加载完则该次播放跳过并记诊断）。

公开播放 API（供 Demo / 脚本，仅公开 API）：`AudioSystem.PlayOneShot(AudioClip clip, in Vector2 worldPos, float volume = 1, float pitch = 1)`（positional，走 voice 池）、`PlayUi(AudioClip clip, float volume = 1)`（非定位、`AL_SOURCE_RELATIVE` listener 处）、`PlayAmbient(ushort materialId)` / 内部由事件驱动。`AudioSettings` 公开主音量、各类别音量、`MaxVoices` / `MaxAmbientVoices`、`PixelsPerMeter`、距离模型参数、`PerTypeCap` / `CoalesceBucketSize`，运行时可调（编辑器 / Demo 设置）。

### 3.9 诊断

`AudioDiagnostics` 向 Core 诊断 / 计时器注册分项（架构 §17.1、`00 §7`）：音频派发耗时、本帧事件 drained / coalesced / dropped 数、活跃 positional voice 数、ambient voice 数、voice-steal 次数、加载中 / 已加载 clip 数。供编辑器性能 HUD 与过载诊断使用。

---

## 4. 实现清单

OpenAL 封装与生命周期：

- [x] `AudioSystem` 门面：`Initialize` / `Update(in CameraView, long simTick, bool simSteppedThisFrame)` / `Shutdown`，聚合各组件（架构 §10、帧尾音频派发步骤）。
- [x] `IAudioBackend` 抽象 + `OpenAlBackend`（Silk.NET.OpenAL）+ `NullAudioBackend`（测试用，无声记录调用）。
- [x] `OpenAlDevice`：`TryInitialize` 打开默认设备 + 创建 / make-current `ALContext`，设置全局距离模型与 listener 增益；设备缺失走静默降级、不崩溃（架构 §10.1）。
- [x] `AudioListenerState` + `UpdateListener(in CameraView)`：2D listener 定位（相机中心、朝向 / up 约定）。
- [x] `AudioSpace.ToMeters(int cellX, int cellY)`：世界 cell→OpenAL 米空间，按 `AudioSettings.PixelsPerMeter`（建库即定常量）。
- [x] `AudioVoice`：封装 AL source（`Play`/`Stop`/`IsFinished`/`StealScore`），3D 位置 + 距离衰减参数（`ReferenceDistance`/`MaxDistance`/`RolloffFactor`）。
- [x] `AudioVoicePool`：预分配固定 voice，`Acquire(byte priority, AudioEventType)` + voice stealing（距离 + 优先级 + 年龄评分），池满丢弃计诊断（架构 §10.1 3D positional source 池）。

事件消费与限频去重（消费侧，主线程帧尾）：

- [x] `AudioEventType` 消费侧语义对齐 Core 契约枚举：`ParticleImpact`/`FireCrackle`/`LiquidSplash`/`Explosion`/`RigidbodyShatter`/`AmbientRegion`（架构 §10.2，相位 3/4/5/7/8a）。
- [x] `AudioDispatcher.Dispatch`：排空 Core MPSC ring → coalesce → 冷却过滤 → 解析 → 取 voice → 播放，全程零分配（AGENTS §3，相位 8 后）。
- [x] `AudioEventCoalescer`：按类型每帧上限 `PerTypeCap`（impact/splash/shatter… 各自上限），超限合并 / 丢弃保留最大 `Magnitude`（架构 §10.2 强制限频）。
- [x] 近坐标合并：世界坐标量化到 `CoalesceBucketSize` 桶，同 `(type,bucket)` 合并（取 max `Magnitude`、累加 `Count`），开放寻址定容哈希、帧末整体清空（架构 §10.2 防满屏过载）。
- [x] `CooldownTracker`：`(materialId,type)` 冷却 `CooldownTicks` 去重，定容零分配。

材质化映射与各事件类型：

- [x] `MaterialAudioTable`：初始化期把 `MaterialDef[].AudioCues` 扁平化为 `materialId × AudioEventType` 数组；`ResolveCue` 返回 cue handle + 音量 / 音高（按 `Magnitude`/`Count` 插值 + 确定性 pitch 抖动），热路径零字符串 / 零字典（架构 §7.3/§7.4）。
- [x] 规定并文档化 `AudioCueSet` 契约形状（`ImpactCue`/`FireCue`/`SplashCue`/`ExplosionCue`/`ShatterCue`/`AmbientCue` 稳定 cue handle，buffer 解析由节点 4 接入），作为对 plan/04 的契约（架构 §7.3）。
- [x] impact 播放：相位 3 高速沉积事件 → 按材质 cue handle + `Magnitude`/`Count` 调音（架构 §3.3 相位 3、§7.6）。
- [x] fire crackle 播放：相位 4/5 燃烧聚合区域 → 按合并后的区域事件播放 FireCue，绝不每火 cell 一声（架构 §3.3 相位 4/5、§7.4/§7.5）。
- [x] splash 播放：相位 3/4 液体飞溅 / 落水（架构 §3.3 相位 3/4）。
- [x] explosion 播放：相位 7 冲击事件（架构 §3.3 相位 7、§7.6）。
- [x] shatter 播放：相位 8a 刚体破碎 / 拆分（架构 §3.3 相位 8a、§8.4）。
- [x] `AmbientLoopManager` + `AmbientVoice`：材质化 ambient（熔岩咕嘟 / 深水低频），带滞回交叉淡变（`Enter/ExitThreshold`、`AmbientFadeRate`），独立 ambient source 集（架构 §10.2）。

资产加载与播放接口：

- [x] `AudioBuffer`（AL buffer + PCM 元数据）/ `AudioClip`（引用计数资源句柄）。
- [x] `IAudioDecoder` + `WavDecoder`（纯托管 WAV/PCM，内建无新依赖）。
- [x] `AudioClipCache.LoadAsync(string assetId)` / `Unload` / `Dispose`：经 Content 取字节、异步解码、上传 buffer；单 clip 引用计数归零时删除 buffer，owning `AudioSystem.Shutdown` 可批量释放仍缓存的 buffer；加载中静默占位不阻塞（架构 §10、AGENTS §2 不写假实现）。
- [x] `AudioStreamPlayer` + 流式 refill worker（队列 buffer，解码在后台线程，不进主线程 / sim 热循环，架构 §10.1）。
- [x] 公开播放 API：`PlayOneShot(AudioClip, in Vector2 worldPos, float volume, float pitch)` / `PlayUi` / 事件驱动 ambient（Demo 仅依赖公开 API，AGENTS §0）。
- [x] `OggVorbisDecoder`（NVorbis 纯托管，符合不变式 10）。

帧节奏与诊断：

- [x] 音频派发步骤接入帧尾（相位 8 后、相位 10 前），仅事件入队 / 排空 / 设源参数，计入 §1.4「音频派发 ≤1ms」（架构 §10.3、§17.1）。
- [x] render-only 帧（`simSteppedThisFrame==false`）仍推进 voice / ambient / listener，ring 空，声场连续（架构 §4.2）。
- [x] `AudioDiagnostics` 向 Core 诊断注册：派发耗时、drained/coalesced/dropped、活跃 voice / ambient 数、steal 次数、clip 计数（架构 §17.1）。
- [x] `AudioSettings`：主 / 类别音量、`MaxVoices`/`MaxAmbientVoices`/`PixelsPerMeter`/ 距离模型 /`PerTypeCap`/`CoalesceBucketSize`，运行时可调。

---

## 5. 验收标准

- [x] 设备打开成功时材质化音效按事件播放；headless / 无设备时静默降级、引擎不崩溃（架构 §10.1）。
- [x] impact / fire crackle / splash / explosion / shatter / 材质化 ambient 六类事件均能正确触发并听感区分（架构 §10.2）。
- [x] **限频生效**：单帧注入 1000 个同坐标同类 impact，实际取用 voice ≤ `PerTypeCap`，其余计入 `Dropped`（架构 §10.2，单元测试用 `NullAudioBackend`）。
- [x] **近坐标合并生效**：同帧大量相邻同类事件合并为按桶数量级的少量播放，非逐事件播放。
- [x] **冷却去重生效**：同 `(material,type)` 在 `CooldownTicks` 内的二次事件被抑制。
- [x] **零分配**：稳态帧的音频派发路径零托管堆分配（BenchmarkDotNet `MemoryDiagnoser` 或分配计数器验证，AGENTS §3/§7）。
- [x] **预算达标**：音频派发耗时计入并满足 §1.4「游戏逻辑 + 音频派发 ≤1ms」；混音 / 解码不出现在主线程帧时间分项（架构 §10.3，诊断 HUD 验证）。
- [x] **定位正确**：声源 pan 与衰减随 listener / 相机移动正确变化（左右像素声像、远近音量）。
- [x] **与降频一致**：sim 降到 30Hz 时事件密度随之下降、听感与画面同步；render-only 帧声场连续无跳变（架构 §4.2/§10.3）。
- [x] **voice stealing**：voice 池压满时按优先级 / 距离 / 年龄抢占，无爆音 / 无泄漏。
- [x] **ambient 交叉淡变**：材质区域进出时 ambient 平滑淡入淡出、滞回无反复启停。
- [x] **MPSC 线程安全**：多 worker 并发产生事件、主线程并发排空，无数据竞争 / 丢失 / 重复（与 Core ring 契约联测）。
- [x] **资产加载**：WAV/PCM 正确加载与播放；加载中不阻塞主线程；`Unload` 与 owning `AudioSystem.Shutdown` 释放缓存 buffer 无泄漏，`AudioPhaseDriverTests.EngineLoadsContentAudioAndInjectsScriptAudioApi` 断言 `Engine.Dispose()` 后 `NullAudioBackend.LiveObjectCount == 0`。
- [x] 不读取 sim 网格于热路径、不进 sim 热循环；混音 / 解码全在 OpenAL 音频线程 / 解码 worker（架构 §10.1）。
- [x] 未新增除 Silk.NET.OpenAL 外的 native 依赖（不变式 10）；无新 `DllImport`（AGENTS §3）。

---

## 6. 依赖关系

前置（必须先完成）：plan/01（项目骨架、CPM、`Directory.Build`）；plan/02 Core（**事件总线 MPSC ring + `AudioEvent`/`AudioEventType` 契约**、持久线程池、`EngineTime`、`CameraView`、诊断、数学）。

数据 / 资产前置：plan/04 / Content（materials JSON 的 `audioCues` 加载、name→id 表；`AudioCueSet` 类型按 plan/00 归属 Simulation）；Content 资产管线（音效字节流、`content/` 音效目录）。

产生侧契约（并行，钩子实现属对方文档）：plan/03 CA 内核（相位 3 沉积、相位 4 反应 / 点燃、相位 5 热场相变发声钩子；ambient 粗采样）、plan/05 粒子（相位 3 落定、相位 7 抛射发声钩子）、plan/06 物理（相位 8a 破碎发声钩子）。

装配方（下游）：Hosting（帧循环装配音频派发步骤、提供 `CameraView`/`AudioSettings`）；plan/12 编辑器（音量 / cue 调试面板、诊断 HUD 消费）；plan/13 Demo（仅经公开 API 播放）；plan/14 测试（限频 / 去重 / 线程安全 / 零分配验收）。

外部：Silk.NET.OpenAL（`00 §4`）；NVorbis（`00 §4`）。

阻塞：无。

---

## 7. 提交节点

对应架构 §18 里程碑 **M8（音频）**。按 AGENTS §6 每完成一个节点立即用中文 git 提交（`scope=audio`）：

- [x] 节点 1 `feat(audio): OpenAL 设备/上下文/listener + positional source 池`（§3.1/§3.3 对应实现清单第 1 组）。
- [x] 节点 2 `feat(audio): 事件消费派发 + 限频去重(每帧上限/近坐标合并/冷却)`（§3.2/§3.4/§3.7 第 2 组）。
- [x] 节点 3 `feat(audio): 材质化音效映射(AudioCues) + impact/fire/splash/explosion/shatter`（§3.5/§3.6 第 3 组）。
- [x] 节点 4 `feat(audio): 资产加载(WAV) + 流式播放 + 公开播放 API + 诊断接入`（§3.8/§3.9 第 4 组）。
- [x] 节点 5 `test(audio): 限频/去重/零分配/MPSC 线程安全/定位 验收测试`（§5 验收，随 plan/14）。

> 节点 1–4 顺序推进，每节点完成即勾选并提交；§5 验收全部勾选方视为本文档完成（AGENTS §7）。Ogg 解码与 ambient 粗采样产生侧若被阻塞，标 `- [!]` 上报，不写假实现绕过。
