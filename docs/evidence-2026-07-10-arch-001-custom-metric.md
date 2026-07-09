# 2026-07-10 ARCH-001 通用自定义 metric channel 验收

taskIds: `ARCH-001`  
commit: `72d90ad29c42f700185e27bf3a21662653131cfd`  
runSessionId: `local-20260710-arch001-custom-metric`  
runIdentityStatus: `captured`  
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 目标

移除 Core/Editor 中 Demo 专属的 `LavaActiveAreaCells` 诊断语义，改用不解释玩法含义的 `CustomMetricChannel`。Demo 仍可通过公开 `EngineCounters.SetCustomMetric` 发布自己的 label/value，Editor 和窗口 probe 只消费通用字段。

## 实现结果

- `PixelEngine.Core.Diagnostics.CustomMetricChannel` 提供稳定 label、整数 value、版本一致快照读取和多 writer gate；读取稳态不创建托管对象。
- `EngineCounters` 只暴露 `CustomMetric`/`SetCustomMetric`，不再包含 `LavaActiveAreaCells` 或任何关卡语义字段。
- `PerformanceHudSample`、`PerformanceHudPanel` 和 load trend 使用 `CustomMetricName`/`CustomMetricValue`，不再显示或绘制 lava 专属字段。
- Demo 的 `DemoLoadCountersPhaseDriver` 通过公开 API 发布 `lava_active_area_cells`；DemoWindowFrameTimeProbe/DemoProgram 以通用 `custom_metric` 记录和输出，玩法 label 留在 Demo 层。
- Core API 设计说明已同步到 `plan/02-core-infrastructure.md §3.7`。

## 验证命令与结果

```powershell
dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1
dotnet test PixelEngine.sln -c Release --no-build --no-restore -m:1
rg -n -i "\blava\b|熔岩" src/PixelEngine.Core src/PixelEngine.Hosting src/PixelEngine.Editor -g '*.cs'
```

结果：

```text
Build: 0 warnings, 0 errors, 32 projects
Tests: 1460 passed, 0 failed, 34 skipped
Core/Hosting/Editor exact lava search: no matches (rg exit 1 is the expected no-match result)
```

受影响测试的独立结果：`PixelEngine.Core.Tests` 30/30、`PixelEngine.Editor.Tests` 82/82、`PixelEngine.Demo.Tests` 130 passed / 1 expected native skip。全量 skipped 均为未启用的 native/条件 smoke，不是本次迁移失败。

## 边界

本报告证明诊断 API 的职责边界、公开发布路径和现有行为回归；不把 Demo 的玩法 metric 变成 Core/Editor 的产品语义，也不改变 `CI-002` 远端 workflow 或 native smoke 的外部证据状态。
