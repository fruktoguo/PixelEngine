# AGENTS.md — PixelEngine 开发规范

本文件是本仓库所有贡献者（人类与 AI agent）必须遵守的开发宪法。任何代码、计划、提交都以本文件为准。与本文件冲突的做法一律视为错误。

---

## 0. 项目是什么

PixelEngine 是一个自研的、对标 Noita 世界模拟的高性能 2D 像素游戏引擎，配一个在引擎之上开发的落沙游戏 Demo。

- **引擎**（`src/PixelEngine.*`）：可复用、无玩法的内核。全像素 falling-sand 细胞自动机、自由粒子、像素级精确碰撞、刚体、材质反应、生命周期、渲染、音频、脚本系统、内嵌编辑器。
- **Demo**（`demo/PixelEngine.Demo`）：一个具体游戏，**只依赖引擎的公开 API**，关系等同「Unity 游戏之于 Unity 引擎」。Demo 是引擎公开 API 的 dogfood 验证——若 Demo 需要某能力却只能靠引擎内部类实现，说明引擎 API 设计有缺陷，必须修引擎而非在 Demo 里开后门。
- **权威设计**：`docs/PixelEngine-架构与需求设计.md`（19 章，带置信度标注）。所有 plan 与实现以它为技术依据；plan 条目大量引用其 `§x.y` 章节号。

---

## 1. 不可违背的架构不变式（Invariants）

以下来自架构文档，是引擎正确性与性能的承重墙，**任何改动不得破坏**：

1. **四大相互强化的基石**：64×64 chunk hash-map + 单缓冲原地更新 + per-chunk dirty rectangle + 32px 移动上限。四者缺一，全屏 60fps 不成立。（§5）
2. **4-pass checkerboard 无锁调度**：CA 多线程必须按 2×2 chunk parity 分 4 遍、遍间 barrier；同遍内任意两线程写区域永不重叠（32px halo < 64px 间隔）。绝不在 cell 级别加锁。（§5.7）
3. **单缓冲 + parity 时钟位**：禁止 double-buffer 整世界（会毁掉 dirty-rect）。用每帧翻转的 parity bit 防止 cell 一帧移动/反应两次。（§5.2、§5.3）
4. **跨界写入恒在 32px halo 内**：移动与反应都只触及 von Neumann/halo 邻居；边界唤醒走 KeepAlive；border ring 保证跨界目标必驻留。（§5.5、§5.8、§7.4）
5. **CA↔刚体双向耦合**：刚体像素每帧真往返于网格（erase→step→inverse-sample re-stamp）；body-local mask 作不可变权威形状源，绝不让形状被往返侵蚀。（§8.3）
6. **帧节奏 = 固定逻辑步长 + 时间膨胀，绝不追帧**：sim/physics 每个被执行的 tick 至多一步；过载只降帧/降频，绝不用 accumulator 补步（否则 death spiral）。（§4）
7. **颜色不入 cell**：渲染色由材质纹理采样 + 温度 glow 在渲染相位生成；sim cell 不存 RGBA。（§7.1）
8. **material 以稳定字符串键入盘**：运行时数值 id 仅作索引、绝不入盘；存档存 name↔id 表并在读档时 remap。（§11.2）
9. **CPU sim 权威**：模拟主体在 CPU；GPU 仅承担渲染、光照、粒子合成、以及**可选**的非权威计算 pass。像素碰撞需要网格随时可读，不接受 GPU→CPU readback 卡流水线。（§9.5）
10. **权威 sim 热路径的静态 vendored native 收敛到 Box2D 一个依赖**：进入 sim/physics 权威热路径、随引擎静态 vendored 并纳入 dual-build 静态链的 native，永远仅 Box2D 一个。其余一律归为**门控类可选 native**——即非 sim 权威、可选、带纯托管默认回退、按 RID 动态/系统分发的 UI/渲染/音频内核（OpenAL/ANGLE，以及新增的游戏内 UI 内核 RmlUi/Ultralight 等）——它们**不计入本条「单 native 硬约束」**，但每一个都必须：(a) 走动态/系统分发，绝不进入 Box2D 的静态 dual-build fan-out；(b) 可经开关整体禁用并回退到纯托管基线（如 `PixelEngine.UI` 的 `ManagedFallbackBackend`），禁用后引擎在无该 native 时仍可运行。本条核心不变：Box2D 是唯一的 sim-native、唯一被 dual-build 静态承载的 native 依赖。（§14.4，门控依赖清单见 `plan/00-conventions-and-techstack.md` §4.1）

任何 plan 条目若与以上冲突，停止并上报，不要自行变通。

---

## 2. 工程哲学：一步到位，无 MVP，无临时实现

- **一次性实现完整能力**，不做「先简化版以后再补」。不存在 MVP、占位、TODO-later、stub、mock 充当实现。
- 若某条目暂时无法完整实现，**不许写假实现糊弄**；标记 `- [!] 阻塞：原因` 并上报，等决策，不前进。
- 性能默认拉满：**能多线程就多线程，能省内存就省内存，能上 GPU 计算就上 GPU 计算**。不接受「先单线程跑通再说」。
- 接口先于实现：公开 API 一次设计到位，带完整 XML 文档注释（脚本系统的 IntelliSense 依赖它）。

---

## 3. 性能纪律（强制，非优化项）

- **数据布局**：sim 热数据一律 SoA（struct-of-arrays），不用 AoS 进热循环。每 cell 字节预算见 §7.1，新增字段需评审。
- **零分配**：稳态帧循环内**零托管堆分配**。用 `Span<T>`/`stackalloc`/`ArrayPool<T>`/对象池/`GC.AllocateArray(pinned:true)`/`NativeMemory`。禁止热路径里 LINQ、闭包捕获、装箱、`params`、迭代器、字符串拼接。
- **多线程**：CA 走 checkerboard + 持久线程池（非每帧 `Parallel.For`）。Box2D 走自建 task-callback 桥派发到同一线程池（§14.2）。所有可并行的离线工作（形状重建、CCL、序列化字节准备、render buffer 构建、粒子积分）都并行。
- **SIMD**：用 `System.Runtime.Intrinsics`（Avx2/Avx512/AVX10.2）+ `Vector<T>`，必备 scalar fallback。靠 CoreCLR+R2R 运行时 light-up，**不固定 ISA**。
- **GPU 计算**：光照（含可选 Radiance Cascades）、bloom、后处理、高密度粒子合成走 GPU；可选非权威 sim pass（如空气/烟扩散）可下放 compute，但权威网格始终在 CPU。
- **互操作**：仅 `[LibraryImport]`（source-gen），禁新 `DllImport`；blittable-only；`[SuppressGCTransition]` 只用于已证实 <1μs、无回调、不抛异常的叶子调用（绝不用于 `b2World_Step` 与 task 桥回调）。
- **校验而非臆断**：性能结论用 BenchmarkDotNet + 反汇编（`DOTNET_JitDisasm`/Disasmo）证实（bounds-check 消失、SIMD 寄存器出现），不靠感觉。瓶颈按**延迟+分支**而非带宽分析（§12.7）。

---

## 4. 代码规范

- 语言/运行时：C# 14 / .NET 10 LTS。`<Nullable>enable</Nullable>`、`<AllowUnsafeBlocks>true`（仅 sim/physics/interop 项目）、file-scoped namespace、`TreeatWarningsAsErrors` 在 CI 开启。
- 命名：类型/方法/属性 `PascalCase`，私有字段 `_camelCase`，常量与编译期值 `PascalCase`，局部变量 `camelCase`，泛型 `T`/`TKey`。
- 注释：公开 API 必须有 XML 文档注释（中文，技术术语英文）。复杂算法（checkerboard、KeepAlive、inverse-sampling、凸分解）在实现处写「为什么」级注释并引用 `docs §x.y`。
- 不可变优先：能 `readonly`/`readonly struct`/`in` 就用。热结构体注意尺寸与拷贝成本。
- 错误处理：库代码不吞异常；预期失败用返回值/`bool TryX`，意外失败抛明确异常。热路径不靠异常控制流。

---

## 5. plan/ 工作方式

- `plan/tasks/README.md` 是唯一的执行状态真相源；选择下一项、更新状态、判断完成和恢复中断工作都从该目录开始。
- `plan/00-*.md` 至 `plan/20-*.md` 是子系统详细设计与历史清单。原有 checkbox 只作迁移快照，不再作为 live task，也不得继续在其中新增执行状态。
- 每个 canonical task 必须有唯一 ID，且全目录只能有一个状态 checkbox：`- [ ]` 未开始；`- [~]` 进行中；`- [!]` 阻塞并写明解除条件；`- [x]` 实现、测试、验收和所需证据全部完成。
- 开始任务时先检查依赖，再把唯一状态改为 `[~]`；完成任务定义的提交节点就立即用中文 git 提交（见 §6），不得等整条轨道结束后合并提交。
- 若实现发现详细设计、产品目标或架构不变式存在冲突，先更新上层文档与 canonical task，再修改代码。所有计划文档改动必须通过 `tools/validate-task-catalog.ps1`。

---

## 6. Git 提交规范

- **每完成一个 canonical task 定义的提交节点，必须用中文写一次 git 提交。** 不攒大堆改动一次性提交。
- 不提交 `bin/`、`obj/`、构建产物、存档（见 `.gitignore`）。
- 提交信息格式（中文正文，类型前缀用英文便于工具识别）：

  ```
  <type>(<scope>): <中文简述>

  <中文正文：做了什么、为什么、对应 task 与设计章节>

  对应任务: PERF-002
  设计依据: plan/08-rendering.md §实现清单 第N项
  ```

  `type` ∈ `feat|fix|perf|refactor|test|docs|build|chore`；`scope` 如 `sim|physics|render|audio|script|editor|demo|core|world|build`。
- 示例：`feat(sim): 实现 64x64 chunk 与 per-chunk dirty rectangle`
- 以当前检出的本地分支为准；涉及高风险大改可开 `feat/<name>` 分支，并始终按提交节点小步提交。
- **不使用 `--no-verify`，不跳过 hook，不强推。** push 仅在用户明确要求时。

---

## 7. 测试与验证

- 单元/性质测试用 xUnit；性能基准用 BenchmarkDotNet。
- CA 内核重点测：质量守恒（含跨 chunk 边界）、反应守恒（双输出/定向反应在边界不翻倍/不丢失）、单线程 oracle 比对多线程统计性质、movement 规则、parity 防重复。（§16.2）
- physics 测：凸分解每片 ≤8 顶点且凸、覆盖原 mask、`radius=0`；inverse-sampling 旋转水密无洞。
- serialization 测：save→load 逐 cell 等价；改 materials.json 顺序/增删后旧档正确重映射；版本迁移链。
- canonical task 只有在其验收条件、测试和要求的证据全部满足后才能改为 `[x]`；旧 plan checkbox 不再用于判定完成。
- 改性能敏感代码后跑对应 benchmark，回归即视为 bug。

---

## 8. 常用命令（项目建立后）

```pwsh
dotnet build PixelEngine.sln -c Release
dotnet test
dotnet run --project demo/PixelEngine.Demo -c Release
dotnet run --project bench/PixelEngine.Benchmarks -c Release   # BenchmarkDotNet
# 反汇编校验热方法：设置环境变量 DOTNET_JitDisasm=<方法名> 再 run
```

技术栈完整定稿见 `plan/00-conventions-and-techstack.md`。
