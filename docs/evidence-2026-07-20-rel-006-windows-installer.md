# REL-006 Windows 安装器与产品入口证据

## 结论

`REL-006` 的本机 Windows x64 安装交付已闭合。来源提交 `d12ade9d18bf352166aacdb131d44f1f5fe91f25` 在干净已跟踪工作树上生成 `PixelEngine-Setup-0.1.0-win-x64.msi`；安装器使用 WiX 4.0.6，将自包含 ReadyToRun Editor 和 .NET runtime 封装在单一 MSI 中。安装向导支持自定义路径，安装后提供当前用户桌面与开始菜单 `PixelEngine` 快捷方式，用户入口为 `PixelEngine.exe`。

最终 MSI 在同时包含空格和非 ASCII 字符的隔离路径完成真实静默安装、产品登记核对、安装版 Editor 启动和卸载。安装、Editor、卸载退出码均为 0；卸载后安装目录、快捷方式、产品登记和 `HKCU\Software\PixelEngine` 均无残留。

## 来源与环境

| 字段 | 值 |
|---|---|
| task | `REL-006` |
| source commit | `d12ade9d18bf352166aacdb131d44f1f5fe91f25` |
| source tracked worktree clean | `true` |
| tested at UTC | `2026-07-20T12:35:34.4235158+00:00` |
| OS | Microsoft Windows 11 专业版 build 26100 |
| CPU | AMD Ryzen 7 5800X，8 cores / 16 logical processors |
| PowerShell | 7.6.3 |
| .NET SDK | 10.0.108 |
| RID / configuration | `win-x64` / `Release` |
| installer toolchain | `WixToolset.Sdk/4.0.6` |
| signing | `false`，仅本机开发测试 |

原始 lifecycle JSON schema 没有单独的 `runSessionId` 字段，因此不补造 session identity；`testedAtUtc`、MSI SHA256、MSI ProductCode 和随机隔离安装目录共同保留本次实际运行身份。

## 安装包

| 字段 | 值 |
|---|---|
| final-output path | `最终输出/安装器/PixelEngine-Setup-0.1.0-win-x64.msi` |
| size | 73,750,911 bytes |
| SHA256 | `8b111acff406e70461ce15126556ecdb197a222d5dc36e64751587dbb5ad7ccb` |
| ProductName / Manufacturer | `PixelEngine` / `PixelEngine` |
| ProductVersion | `0.1.0` |
| ProductCode | `{B9E3812D-7F22-422E-8F4B-13EB7B6BDDC4}` |
| UpgradeCode | `{6FCA8784-80DC-4E02-B8F4-93B5677C1E87}` |
| default install directory | `%LocalAppData%\Programs\PixelEngine` |
| custom directory persistence | `true`，由 `AppSearch` / `RegLocator` 在维护和卸载时恢复 |
| MSI File rows | 277 |
| shortcuts | 2，Desktop + Start Menu |
| embedded cabinets | 2，全部位于同一个 MSI 内 |

安装器目录同时包含 `manifest.json`、`verification.json` 和 `SHA256SUMS`。manifest 记录 `editorSelfContained=true`、`editorReadyToRun=true`、`sourceTrackedWorktreeClean=true`；独立静态 verifier 核对产品元数据、稳定 UpgradeCode、自定义目录恢复、完整 self-contained runtime、旧 `PixelEngine.Editor.Shell.exe/.dll` 不存在、快捷方式目标、当前用户登记、277 个 File 行和两卷连续内嵌 CAB。

## 真实生命周期

测试安装目录：

```text
C:\Users\YuoHira\AppData\Local\Temp\PixelEngine Installer 测试\PixelEngine Test 安装 3e4ff6977bb64280b818a38a9c7f6a5e
```

| 断言 | 结果 |
|---|---|
| custom path contains space | `true` |
| custom path contains non-ASCII | `true` |
| install exit code | `0` |
| installed files verified | `true` |
| desktop / Start Menu shortcuts verified | `true` |
| Windows Installer product state before / installed / after | `-1` / `5` / `-1` |
| product registration verified | `true` |
| installed `PixelEngine.exe` launch exit code | `0` |
| Editor launch verified | `true` |
| uninstall exit code | `0` |
| install directory removed | `true` |
| shortcuts removed | `true` |
| product registration removed | `true` |
| lifecycle report `ok` | `true` |

原始可再生报告为 `artifacts/windows-installer-test/rel-006-d12ade9d.json`，SHA256 为 `80f5a555ff8fa1f4bf4fa4aa9eb396122faa442891af069afd730e91f6e92645`；该路径属于 volatile artifact，本文件保留其所有验收字段。

## 命令与回归

```pwsh
pwsh -NoProfile -File tools/update-final-output-fast.ps1 -Rid win-x64 -Configuration Release
pwsh -NoProfile -File tools/test-windows-installer.ps1 -MsiPath 最终输出/安装器/PixelEngine-Setup-0.1.0-win-x64.msi -ExpectedVersion 0.1.0 -ReportPath artifacts/windows-installer-test/rel-006-d12ade9d.json
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~FinalOutput|FullyQualifiedName~OneClickBuildBatchPackagesEditorAndGameWithoutTestsOrProductProbes|FullyQualifiedName~WindowsInstallerPinsToolchainAndVerifiesCustomPathLifecycle" --disable-build-servers
pwsh -NoProfile -File tools/validate-task-catalog.ps1
```

最终输出相关回归为 20 passed、0 failed、0 skipped；另一次针对多卷 CAB、根 checksum 绕过和安装器纪律的聚焦回归为 3 passed、0 failed、0 skipped。WiX 构建为 0 warnings / 0 errors。快速最终输出按合同没有运行完整产品 probe 或正式根 verifier，因此 `_快速构建/manifest.json` 保持 `verified=false`；MSI 自身的静态数据库 verifier 与真实安装生命周期均已独立通过。

## 边界

本机 MSI 未做 Authenticode 签名，只是当前源码的可安装手动测试入口。它不替代 `REL-002` 的确定性复构证据、`REL-003` 的 GitHub Release 上传、正式证书签名、SmartScreen 信誉或外部机器验收。
