# 2026-07-10 ARCH-005 GPU air/smoke 状态收口

taskIds: `ARCH-005`
commit: `464f40a3`
runSessionId: `local-20260710-arch005-air-smoke-status`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; AMD Radeon RX 7900 XT driver 32.0.31021.5001; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 决策

ARCH-005 选择“从基线能力中降级为未启用组件”，而不是把当前独立 air/smoke pass 伪装成生产运行时能力。当前状态为 `deferred_not_enabled`。

现有 `AirSmokePass`、`GpuAirSmokePipeline`、`AirSmokeResources` 和 shader 保留为可复用源码与直接契约测试资产，但生产 `RenderPipeline` 不创建或持有它们，也没有 CPU→GPU seed、dispatch、density 合成或完整回退链。`ComputeCapabilityGate` 现在会把 `NonAuthoritativeAirEnabled` 强制为 `false`，因此 Core 诊断/HUD 不会报告一个没有生产消费者的已启用特性。

## 文档与产品边界

- `plan/09-gpu-compute.md` 顶部新增 ARCH-005 当前状态和解除条件，并明确后续旧 checkbox 是迁移快照，不能覆盖当前状态。
- `plan/tasks/20-scope-decisions.md` 新增当前能力声明，明确 air/smoke 不属于 Windows-first 1.0 运行时能力。
- `docs/target-hardware-matrix.md` 新增功能支持矩阵：`win-x64`、`win-arm64` 和其他 RID 均为“未启用”，状态为 `deferred_not_enabled`。
- `docs/rendering-computesharp-resource-contract.md` 明确资源契约记录不等于 air/smoke 运行时支持。

## 验证命令与结果

```powershell
dotnet build PixelEngine.sln -c Release --no-restore -m:1
dotnet test tests/PixelEngine.Rendering.Tests/PixelEngine.Rendering.Tests.csproj -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-build --no-restore -m:1 --filter "FullyQualifiedName~HostingProjectDisciplineTests|FullyQualifiedName~EngineBuilderTests" --logger "console;verbosity=minimal"
pwsh -NoProfile -File tools/validate-task-catalog.ps1
pwsh -NoProfile -File tools/validate-target-hardware-matrix.ps1
git diff --check
```

结果：

```text
Build: 32 projects, 0 warnings, 0 errors
PixelEngine.Rendering.Tests: 172 passed, 0 failed, 20 skipped, 192 total
PixelEngine.Hosting.Tests (HostingProjectDisciplineTests + EngineBuilderTests): 67 passed, 0 failed, 3 skipped, 70 total
Task catalog: valid; 72 canonical tasks, 34 done, 14 open, 1 active, 23 blocked
Target hardware matrix: valid; 6 RIDs; active=win-arm64,win-x64; conditional=win-arm64; observed_local=win-x64
git diff --check: clean before evidence files
```

Rendering 的 20 个 skipped 是既有 native/GL 条件 smoke；其中 `CanRunAirSmokeDiffusePassWhenExplicitlyEnabled` 未在本机环境执行，因此本报告不把它当作生产接线或窗口运行证据。新静态边界测试和 gate 回归验证的是“当前未启用”决策及其防误报行为。

## 解除条件与边界

重新激活 ARCH-005 前，必须在生产 `RenderPipeline`/`Hosting` 中补齐 pass 生命周期、CPU→GPU seed、density 合成、质量/降级回退、窗口/GL smoke，并同步 plan、产品范围、支持矩阵和证据。远端 CI、其他 RID 真机和外部 GPU 性能证据未在本次执行；本决定不改变那些独立门禁的状态。
