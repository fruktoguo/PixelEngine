# 2026-07-10 PERF-007 Demo 坍塌扫描 scratch 证据

taskIds: `PERF-007`
implementationCommit: `0ee6bbd39b13dee2f766489a8738063f7e98313e`
testGateCommit: `2402789c2ec7c7caa911baf8f8e5bdf8a94182c1`
benchmarkRunId: `local-20260710-perf007-collapse-scratch`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

PERF-007 已完成：

- `PlayableProjectileTool` 持有一组实例级 `CollapseScanScratch`，共享 `visited`、`WorkingMask`、`queue`、`cells` 四个工作数组；主坍塌扫描、悬挑兜底和 impact fallback 串行使用，不再在每次扫描或跨帧重试中创建数组。
- scratch 按需扩容，容量至少覆盖本次窗口；增长策略优先采用 1.5 倍容量，但从默认窗口直接扩到最大窗口时使用精确面积，最大半径不会因指数扩容额外放大内存。
- 每次扫描只清理当前窗口前缀：主扫描清理 `visited`，每个连通块清理 `WorkingMask`；overhang/connected fallback 分别清理自身需要的 `visited` 前缀。Behaviour 销毁时释放四个数组引用。
- connected fallback 的局部函数已改为实例方法，扫描路径不产生闭包对象。

## 分配探针

探针使用 `GC.GetAllocatedBytesForCurrentThread()`，在已启动的 headless Demo 场景中直接执行真实坍塌扫描；每个半径先测首次扩容，再测同一实例的重复扫描。最大半径测试使用 704×704 全驻留世界、中心 `(352,352)`，确保 641×641 扫描窗完全落在 resident chunks 内。

| 半径 | 扫描面积 | scratch 容量 | 首次扫描分配 | 重复扫描分配 |
|---:|---:|---:|---:|---:|
| 40 | 6,561 | 6,561 | 65,728 B | 0 B |
| 320 | 410,881 | 410,881 | 4,108,928 B | 0 B |

最大半径四个数组的 payload 为 `2×410,881×1 + 2×410,881×4 = 4,108,810 B`；实测值只包含数组对象头和对齐开销，未出现原实现跨重试重复的约 4.1 MB 托管分配。

## 可复现命令

```powershell
dotnet build PixelEngine.sln -c Release --no-restore -m:1
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-build --no-restore -m:1 --filter "FullyQualifiedName~PlayableProjectileCollapseScanReuses"
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test PixelEngine.sln -c Release --no-build --no-restore -m:1
pwsh -NoProfile -File tools/validate-task-catalog.ps1
pwsh -NoProfile -File tools/validate-evidence-index.ps1
git diff --check
```

## 正确性与回归

- 分配测试：2 passed、0 failed；默认半径和最大半径重复扫描均为 0 B。
- `PixelEngine.Demo.Tests`：132 passed、1 skipped、0 failed；覆盖多岛、扫描窗上边界、外部支撑、继续开火、首个转换后继续扫描等既有回归。
- solution 全量：1,485 passed、34 skipped、0 failed。跳过项均为显式 GL/window smoke：Hosting 4、Rendering 20、Demo 1、UI 9。
- `dotnet build PixelEngine.sln -c Release --no-restore -m:1`：0 warnings、0 errors。
- `pwsh tools/validate-task-catalog.ps1`：canonical 72 total，40 done、8 open、0 active、24 blocked。

该证据是本机 headless allocation/回归验证，不解除需要目标硬件或真实窗口的 PERF-003、PERF-008、PERF-009、PERF-010 等外部证据任务。
