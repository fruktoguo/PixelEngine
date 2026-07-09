# 2026-07-10 ARCH-004 Hosting/Scripting probe facade 验收

taskIds: `ARCH-004`
commit: `1280385b`
runSessionId: `local-20260710-arch004-probe-facade`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 目标

收敛 Demo 启动器、窗口 scripted probe 和 benchmark probe 的服务访问边界，使它们不再从 `EngineContext` 直接解析 `PhysicsSystem`、`RenderPipeline` 等具体实现；Hosting 负责组合公开 Scripting API、Physics/Rendering 诊断快照和受控 probe 操作。

## 实现结果

- `Engine.Probe` 成为稳定 Hosting 入口；`EngineProbeApi` 暴露 Physics 统计快照、脚本场景/输入/相机/光照 API、Rendering overlay/粒子模式结果和无 OpenGL 事件签名的 present hook。
- `Engine.Probe` 支持已注册脚本 API 的延迟绑定，兼容脚本上下文接入前由外部宿主注册 `ScriptInputApi`/`ScriptCameraApi` 的装配顺序。
- Demo 的启动器、窗口输入/光照/粒子/脚本化 probe 和负载计数驱动均改为消费 probe facade；程序集注册和当前场景读取改用 `Engine.RegisterScriptAssembly` / `Engine.CurrentScene`。
- `HostingProjectDisciplineTests` 锁定 Demo/benchmark 不出现 `Context.GetService/TryGetService` 或具体 Physics/Rendering service 解析；反射断言锁定 probe 公共属性不暴露 `PhysicsSystem`/`RenderPipeline` 本体。
- `EngineBuilderTests` 增加 resident world + Physics 装配后的 `Engine.Probe` 统计绑定行为回归；`plan/11`、`plan/13` 已同步 probe 边界说明。

## 验证命令与结果

```powershell
dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-build -- --headless --ticks 2 --content demo/PixelEngine.Demo/content --no-hot-reload
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
# 其余 11 个非 Hosting 测试程序集逐一以 Release/no-build/no-restore/-m:1 执行
# PixelEngine.Hosting.Tests 按 7 个类过滤组串行执行，覆盖全部测试类，避免聚合 testhost 超时
pwsh tools/validate-task-catalog.ps1
git diff --check
```

结果：

```text
Build: 32 projects, 0 warnings, 0 errors
Demo headless smoke: Engine frame 2, scene lava-mine
Non-Hosting test assemblies: 1016 passed, 0 failed, 30 skipped
Hosting class decomposition: 452 passed, 0 failed, 4 skipped, 456 total
All local test coverage represented by the 13 test projects: 1468 passed, 0 failed, 34 skipped
Task catalog: valid; 72 canonical tasks, 33 done, 15 open, 1 active, 23 blocked
git diff --check: clean before evidence files
```

Hosting 直接执行整个程序集的聚合命令曾在 6 分钟窗口内未返回并留下仓库 testhost；该进程已按仓库路径核验后停止，随后按全部 Hosting 测试类拆分串行执行，所有类过滤组均通过。30 个非 Hosting skipped 与 4 个 Hosting skipped 均为既有 native/图形条件 smoke，不是本任务失败。

## 边界

本报告证明 Windows 本地 Hosting/Scripting probe facade、Demo/benchmark 解析边界和 headless 运行接线；未执行远端 push、CI runner 或外部 native 硬件矩阵验证。
