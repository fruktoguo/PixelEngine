# ComputeSharp/DX12 资源契约决策记录

## 结论

当前 `plan/09` 的 GL compute、bloom、光照、Radiance Cascades、air/smoke 与 GPU 粒子路径绑定的是 `plan/08` 的 OpenGL 资源契约：`GpuComputeResources` 保存 OpenGL texture handle，`IComputeBackend.BindTexture` / `BindImage` / `BindStorageBuffer` 接收的 `uint` 在当前实现中是 GL texture / image / SSBO 名称。`RenderPipeline` 也持有 Silk.NET `GL` 实例和同一个 OpenGL context。

代码层面已用 `GpuResourceContractKind` 把 OpenGL texture-name 契约、D3D12 render graph 契约和 GL-DX12 shared resource/fence 契约分开；当前 `GpuComputeResources.ResourceContractKind` 恒为 `OpenGlTextureNames`，`CanBeConsumedByComputeSharp` 恒为 `false`。`ComputeSharpResourceContract` 只接受 `D3D12RenderGraph` 或 `GlDx12SharedResources`，拒绝 OpenGL 和未知枚举值，并强制 device、command queue、全部 render target resource 与 fence 句柄非零。`PixelEngine.Rendering` 默认不引用 ComputeSharp；只有显式设置 `EnableComputeSharpBackend=true` 时才条件引用包、定义 `PIXELENGINE_COMPUTESHARP` 并编译 `ComputeSharpSupport` 的 DX12 device 探测分支。`GpuCapabilities` 也必须同时声明 `HasComputeSharpResourceContract=true` 与非 OpenGL 的 `ComputeSharpResourceContractKind`，`ComputeCapabilityGate` 不接受仅把 bool 改成 true 的伪契约。

因此 ComputeSharp/DX12 不能直接消费这些句柄。把 GL texture name 强行解释为 DX12 resource pointer、descriptor handle 或 ComputeSharp resource 是错误实现；它不会建立跨 API ownership、memory visibility、layout transition 或 queue synchronization，也无法保证 resize 后资源生命周期一致。

## 不可接受方案

不得在 `ComputeSharpBackend` 中把 `uint textureHandle` 当作 DX12 资源使用。不得为了让 G2 变绿而新增 `ComputeSharp` 包引用后仍复用 GL render graph 的 texture handle。不得引入 GPU→CPU readback 作为 GL/DX12 间同步桥，因为 CPU sim 权威要求渲染增强不能把 GPU 结果回读进 sim tick。

## 可接受解法

第一条路线是引入 D3D 渲染后端，使世界纹理、emissive、occluder、visibility、scene、lit 与 post-process target 都由 D3D resource 拥有。ComputeSharp 只能在该后端下绑定同一批 D3D resource，并由 D3D barrier/queue fence 管理 compute 与 graphics 可见性。OpenGL 后端仍保持 GL compute 或 fragment/CPU fallback。

第二条路线是设计显式 GL-DX12 共享资源层。该层必须定义跨 API resource owner、共享 handle 创建、导入、格式映射、同步 fence、resize/recreate 生命周期、失败回退路径和调试验证。只有当所有共享资源都能通过该层导出/导入并完成同步，ComputeSharp 才能接入现有 OpenGL 渲染路径。

第三条路线是保持当前默认：ComputeSharp 继续作为门控可选后端占位，`IsComputeSharpCompiled=false` 时 G2 恒 false；Windows/DX12 即使可用，也只有在上述资源契约真实落地后才能启用。该路线不阻塞 GL compute 与 GL 3.3 fallback。

## 最小启用条件

启用 ComputeSharp 必须同时满足这些条件：`Directory.Packages.props` 明确登记 ComputeSharp 包版本；Rendering 项目只在 `EnableComputeSharpBackend=true` 的条件编译边界中引用 ComputeSharp，且非 Windows/未启用发行不触碰其类型；`GpuCapabilities.IsComputeSharpCompiled` 由真实编译条件设置为 true，`IsDx12Available` 由 `ComputeSharpSupport` 的 DX12 device 探测提供；`GpuCapabilities.HasComputeSharpResourceContract` 只能在 D3D-only 或 GL-DX12 shared resource/fence 契约真实落地后为 true；`ComputeSharpBackend.IsExecutable` 只能在后端不再是隔离占位、能真实执行 kernel 后为 true；资源绑定 API 不再把 GL texture name 传给 DX12；对应测试覆盖 D3D-only 或 GL-DX12 shared resource 的 resize、barrier、fallback 与 no-readback 路径；plan/15 发行矩阵覆盖 win-x64/win-arm64 R2R/AOT 的 ComputeSharp 依赖和 AOT 行为。

在这些条件满足前，plan/09 的 ComputeSharp/DX12 项保持 `[!]`。当前仓库的正确状态是：GL compute 与 GL point-sprite 路径可用，ComputeSharp 后端为隔离 stub；默认构建 `IsComputeSharpCompiled=false`，显式启用构建最多只能证明 ComputeSharp/DX12 device 可探测。`ComputeCapabilityGate` 即使在测试中看到 Windows/DX12/compiled 标志，也会因为缺少资源契约与可执行后端而回退 GL compute 或基线路径，不能把该 stub 记为完成。
