# 2026-07-10 ARCH-003 Box2D task callback 异常传播验收

taskIds: `ARCH-003`
commit: `f3b91ab9`
runSessionId: `local-20260710-arch003-task-fault-propagation`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 目标

确保 Box2D task callback 内的 JobSystem 异常不会穿过 unmanaged 边界或被静默吞掉；异常必须在当前 `b2World_Step` 返回后传播，并阻止 PhysicsSystem 继续消费未完整推进的 transform、contact 与刚体 inverse-sample 状态。

## 实现结果

- `Box2DTaskBridge` 为每个 bridge 持有托管 fault state，callback 捕获并保留本 tick 首个 `ExceptionDispatchInfo`；累计 fault count 仍作为诊断指标，但不再是唯一处理机制。
- `BeginTick()` 在 tick 起点清理上次已处理状态，`ThrowIfFaulted()` 在 `b2World_Step` 返回后重新抛出首异常；异常不再跨越 unmanaged callback frame。
- `PhysicsSystem.SyncStep` 在 native step 后、`RecordSub`/contact 消费/transform 读回和 inverse-sample restamp 前立即检查 fault；失败后锁存 `PhysicsTickFaulted`，后续 tick 拒绝继续消费 Box2D 状态。
- 新增单线程 callback fault injection、worker callback fault injection，以及 Physics 编排顺序纪律测试。

## 验证命令与结果

```powershell
dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1
dotnet test tests/PixelEngine.Physics.Tests/PixelEngine.Physics.Tests.csproj -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
# 对 tests 下 13 个测试程序集逐一执行同样的 Release/no-build/no-restore/-m:1 命令
pwsh tools/validate-task-catalog.ps1
git diff --check
```

结果：

```text
Build: 32 projects, 0 warnings, 0 errors
Physics tests: 79 passed, 0 failed
Hosting tests: 450 passed, 0 failed, 4 skipped
All 13 test assemblies: 1466 passed, 0 failed, 34 skipped
Task catalog: valid; 72 canonical tasks, 32 done, 16 open, 1 active, 23 blocked
git diff --check: clean before evidence files
```

Physics 测试中的两个 fault injection 均验证首异常可在 tick 边界重新抛出；Hosting 的源纪律回归验证 `ThrowIfFaulted()` 位于 `b2World_Step` 与 world-state 消费之间。34 个 skipped 均为既有未启用 native/图形条件 smoke，不是本次任务失败。首次 solution 聚合命令在超时窗口内未返回，随后停止其遗留仓库 testhost，并以程序集级串行方式完成同一测试覆盖；程序集级结果全部通过。

## 边界

本报告证明 Windows 本地托管 callback fault propagation、physics tick 失败锁存和回归测试；未执行远端 push、CI runner 或外部 native 硬件矩阵验证。
