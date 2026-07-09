# 2026-07-10 TEST-001 native smoke validation

taskIds: TEST-001
commit: bdeb5aa578c101c6cf514136f13e42e1d80f4c6d
runSessionId: local-20260710-test001-native-smoke
hardware: Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; AMD Radeon RX 7900 XT; driver 32.0.31021.5001; .NET SDK 10.0.108; win-x64
commands: `$env:PIXELENGINE_RENDERING_GL_SMOKE=$null; $env:PIXELENGINE_RENDERING_ANGLE_SMOKE=$null; dotnet test <four affected test projects> -c Release --no-build --no-restore --filter Category=NativeSmoke -m:1`; `$env:PIXELENGINE_RENDERING_GL_SMOKE='1'; $env:PIXELENGINE_RENDERING_ANGLE_SMOKE='1'; ./tools/run-native-smoke.ps1 -Configuration Release -ResultsDirectory artifacts/native-smoke-final`; `$env:GOSUMDB='off'; go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 .github/workflows/ci.yml`
rawReport: `artifacts/native-smoke-final/run-20260709T201822684Z-76584/summary.json`

## Result

普通单测在未启用 native 环境时没有进入测试体，xUnit discovery 报告明确 skipped：

| Project | Skipped | Failed | Passed | Total |
|---|---:|---:|---:|---:|
| `PixelEngine.Rendering.Tests` | 20 | 0 | 0 | 20 |
| `PixelEngine.UI.Tests` | 9 | 0 | 0 | 9 |
| `PixelEngine.Hosting.Tests` | 4 | 0 | 0 | 4 |
| `PixelEngine.Demo.Tests` | 1 | 0 | 0 | 1 |

本机 Windows 真实 GL/ANGLE smoke 执行由 `tools/run-native-smoke.ps1` 运行四个项目并解析 TRX，结果为：

```text
[rendering] total=20 passed=20 failed=0 skipped=0 notExecuted=0 exit=0
[ui] total=9 passed=9 failed=0 skipped=0 notExecuted=0 exit=0
[hosting] total=4 passed=4 failed=0 skipped=0 notExecuted=0 exit=0
[demo] total=1 passed=1 failed=0 skipped=0 notExecuted=0 exit=0
Native smoke summary: total=34 passed=34 failed=0 skipped=0 notExecuted=0
```

`.github/workflows/ci.yml` 新增 `native-smoke (win-x64)` job，显式设置 `PIXELENGINE_RENDERING_GL_SMOKE=1` 与 `PIXELENGINE_RENDERING_ANGLE_SMOKE=1`，构建 native/solution 后执行同一汇总脚本，并始终上传 TRX/summary artifact。脚本对空执行、TRX 缺失、测试非零退出和失败用例均返回失败，不能静默降级为通过。

`NativeSmokeFactAttribute` 使用 xUnit 2 discovery 阶段的 `FactAttribute.Skip`，兼容当前 xUnit 2.9.3 + VisualStudio runner 3.1.5；运行时 capability 缺失或真实 native 初始化失败仍作为失败处理。workflow 的 PyYAML 与 actionlint v1.7.12 静态检查通过。

本报告只记录本机真实执行和 workflow 静态验证；未执行 push 或 GitHub hosted runner run，CI-002/CI-003 的远端证据仍保持各自阻塞状态。
