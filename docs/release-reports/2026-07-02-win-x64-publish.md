# 2026-07-02 win-x64 发布验证记录

本记录覆盖 `plan/15` 的本机可验证部分。目标机为 Windows / win-x64，当前只验证 `win-x64` 的 R2R 与 AOT 两条发行通道；不代表 6 RID 全矩阵、macOS codesign/notarization、Linux glibc 动态链或跨硬件 SIMD light-up 已完成。

## 执行命令

```pwsh
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/publish-r2r.ps1 -Rid win-x64
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/verify-publish.ps1 -Rid win-x64 -Channel r2r -SkipPublish
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/publish-aot.ps1 -Rid win-x64
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/verify-publish.ps1 -Rid win-x64 -Channel aot -SkipPublish
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/package.ps1 -Rid win-x64 -Channel r2r
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/package.ps1 -Rid win-x64 -Channel aot
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/audit-release-artifacts.ps1
```

## 结果

`tools/publish-r2r.ps1` 与 `tools/publish-aot.ps1` 均完成 native Box2D build、`dotnet publish` 与产物输出。`tools/verify-publish.ps1` 对两通道均完成 `--smoke`，输出显示内容包加载 18 个材质、22 条反应、19 个音频 clip，Physics 已接入，脚本程序集与 Hosting/Simulation 后端已接入，场景进入 `lava-mine` 第 1 帧。

`tools/package.ps1` 生成两个包：

| 产物 | 大小 |
|---|---:|
| `PixelEngine-Demo-0.1.0-win-x64-aot.zip` | 46,377,811 bytes |
| `PixelEngine-Demo-0.1.0-win-x64-r2r.zip` | 51,260,056 bytes |

`SHA256SUMS` 内容：

```text
434b70240a755e5f9c2b4f9e4b2ad448dd791e5e07d9b08a6a96e61e5baf82d3  PixelEngine-Demo-0.1.0-win-x64-aot.zip
346abf6d2f79e807e1d8941c4a6a39bb64104362f9acbfec640e759eec068e86  PixelEngine-Demo-0.1.0-win-x64-r2r.zip
```

`tools/audit-release-artifacts.ps1` 通过：

```text
Publish artifact audit passed: win-x64/r2r
Publish artifact audit passed: win-x64/aot
Package audit passed. Packages: 2.
Release artifact audit completed.
```

## 阻塞边界

以下 `plan/15` 验收仍不能由本机结果闭合：6 RID R2R/AOT 全矩阵、每个 AOT 产物 SIMD 探针、R2R 在 AVX2/AVX-512 目标机的 runtime light-up、6 RID Box2D 12 件 native 产物、Linux glibc 动态链、macOS codesign/notarization/staple、所有产物的确定性 hash 与 GitHub Release 上传。

## Release Evidence 预检入口

`tools/audit-release-artifacts.ps1 -RequireAll` 负责检查已下载到本地的发布目录与 package root；外部 runner、签名、公证、SIMD probe、R2R light-up、确定性 hash 和 GitHub Release 上传证据由 release evidence manifest 汇总：

```pwsh
./tools/release-evidence-preflight.ps1 -EvidenceManifestPath artifacts/release-evidence/evidence.json -AllowBlocked
```

未提供 manifest 时状态为 `blocked_missing_release_manifest`；manifest 存在但缺任一 RID/channel、AOT SIMD、macOS codesign/notarization、R2R light-up、确定性 hash 或 GitHub Release 上传报告时状态为 `blocked_missing_release_scope_evidence`；manifest 中每个 evidence path 必须带对应 `sha256` 字段，预检会重新计算并拒绝缺失或不匹配的 hash；证据文件齐全且 hash 匹配时状态为 `release_evidence_attached_pending_review`，仍需人工确认这些报告确实闭合 plan/15 的外部验收。AOT SIMD evidence 必须带 `simdProbeKind`，x64 为 `x64_ymm_zmm` 且报告包含 ymm/zmm，arm64 为 `arm64_neon` 且报告包含 NEON；skip 报告不能冒充 SIMD 证明。未传 `-AllowBlocked` 时，即使证据齐全也会以非零退出，避免把 pending review 误当成验收通过。

manifest schema 示例见 `docs/release-reports/release-evidence-manifest.example.json`。示例只描述字段契约，不作为验收证据。
