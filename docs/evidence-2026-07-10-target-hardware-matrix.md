# 2026-07-10 Windows-first target hardware matrix validation

taskIds: EVID-003
commit: 796b57817b9136340b70ee0594fe743f1f094d97
runSessionId: local-20260710-evid003-target-matrix
hardware: Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X; AMD Radeon RX 7900 XT; driver 32.0.31021.5001; .NET SDK 10.0.108
commands: `Get-CimInstance Win32_OperatingSystem`; `Get-CimInstance Win32_Processor`; `Get-CimInstance Win32_VideoController`; `pwsh tools/validate-target-hardware-matrix.ps1`
reportPath: `docs/target-hardware-matrix.md`

## Result

```text
Target hardware matrix valid: 6 RIDs; active=win-arm64,win-x64; conditional=win-arm64; observed_local=win-x64.
```

The matrix records the current local Windows x64 identity and explicitly marks win-arm64, Linux, and macOS CPU/GPU/OS/driver values as `EXTERNAL_REQUIRED` until the corresponding target device or trusted runner reports them. It also preserves the CI/Release runner distinction for linux-arm64: CI uses `ubuntu-24.04-arm`, while `tools/release-rids.json` currently declares `ubuntu-latest` for the release workflow.

This is a control-plane inventory, not target performance, CI, signing, native-leak, or final release evidence. Those tasks still require their own same-run reports and external conditions.
