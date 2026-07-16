# Editor 外部自动化 API

PixelEngine Editor v1 提供 Windows Named Pipe 本地控制面、公开 .NET Client 和独立
`pixelengine-editor` CLI。外部进程通过稳定 capability ID 操作与 UI 相同的 workspace、
Scene、Hierarchy、Inspector、Project、Play、runtime、build 和 player 语义；不需要 MCP、
屏幕坐标、Computer Use 或 `--scripted-*` 探针。

当前 Windows 实现只发布真实可用的 Named Pipe endpoint。协议为 Unix Domain Socket 保留
transport kind，但 Editor 不会宣告未实现的 endpoint。

## 1. 构建与启动

从源码构建 Editor、Client 和 CLI：

```powershell
dotnet build PixelEngine.sln -c Release
```

framework-dependent CLI 位于：

```text
tools/PixelEngine.Editor.Cli/bin/Release/net10.0/pixelengine-editor.exe
```

SDK 包与 CLI 版本可独立验证：

```powershell
dotnet pack src/PixelEngine.Editor.Automation.Protocol -c Release -o artifacts/packages
dotnet pack src/PixelEngine.Editor.Automation.Client -c Release -o artifacts/packages
pixelengine-editor --version
```

普通 Editor 默认启用 automation。CI 应为 user-data、discovery、artifact 和允许导入的源目录
使用隔离 root：

```powershell
$runRoot = Join-Path $env:TEMP ("pixelengine-automation-" + [Guid]::NewGuid().ToString("N"))
$userData = Join-Path $runRoot "user-data"
$discovery = Join-Path $runRoot "discovery"
$artifacts = Join-Path $runRoot "artifacts"
$imports = Join-Path $runRoot "imports"
New-Item -ItemType Directory -Force $userData, $discovery, $artifacts, $imports | Out-Null

dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj `
  -c Release --no-build -- `
  --project demo/PixelEngine.Demo/project.pixelproj `
  --user-data-dir $userData `
  --automation-discovery-root $discovery `
  --automation-artifact-root $artifacts `
  --automation-import-root $imports
```

Editor 为每次进程启动创建不可复用的 instance ID 和高熵 credential。descriptor 只保存
credential 文件路径，不保存 secret。不要把 credential 内容放进 argv、日志或环境变量；CLI
会从经过 PID、process-start identity、root 和 reparse-point 校验的 descriptor 读取它。

## 2. 发现与能力

源码工作流可用 `dotnet run` 调 CLI；发布后将前缀替换为 `pixelengine-editor`：

```powershell
$cli = "tools/PixelEngine.Editor.Cli/PixelEngine.Editor.Cli.csproj"

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery discover

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery capabilities

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery --output json capabilities --matrix

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery help workspace.get
```

只有一个 live Editor 时 CLI 自动选择它；发现多个实例时必须传 `--instance <stable-id>`。
`capabilities` 来自 Server 的可执行 registry，descriptor 给出 request/response schema、最小
scope、revision、transaction、execution phase、event 和 artifact 契约。不要把显示标题或
数组下标当 ID。

`capabilities --matrix` 返回 capability 与真实菜单、快捷键、工具栏、panel 和 context command
的双向闭包。Client 会独立校验稳定 ID 严格排序、双向链接、canonical SHA256，并把 runtime
capability digest 与 discovery descriptor 绑定；任一不一致都会拒绝结果。发布快照与其 schema
分别位于
[`schema/editor-automation-capabilities.v1.json`](../schema/editor-automation-capabilities.v1.json)
和
[`schema/editor-automation-capabilities.schema.json`](../schema/editor-automation-capabilities.schema.json)。
快照只能由 production registry 生成，提交前必须检查无漂移：

```powershell
$matrixTool = "tools/PixelEngine.Editor.Automation.Matrix/PixelEngine.Editor.Automation.Matrix.csproj"
$matrix = "schema/editor-automation-capabilities.v1.json"

dotnet run --project $matrixTool -c Release -- --check $matrix
# 只有 production registry 有意变化时才执行：
dotnet run --project $matrixTool -c Release -- --output $matrix
```

CLI 默认只申请 `editor.read`。写工程、改设置、构建和启动玩家时显式申请 capability 所需的
最小 scope：

```text
editor.read
editor.control
project.write
settings.write
process.build
process.launch
automation.admin
```

## 3. CLI 调用

读取 workspace：

```powershell
dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery `
  --output json `
  call workspace.get
```

通用 capability 使用 `call <capability-id>`，payload 可内联或从有界 JSON 文件读取：

```powershell
dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery `
  --scopes 'editor.read,editor.control' `
  --client-instance-id my-ci-run-42 `
  call hierarchy.selection.set `
  --payload-file request.json `
  --expected-global 17 `
  --expected-resource editor:selection=4 `
  --idempotency-key select-player-01
```

写 capability 若声明 revision 或 idempotency 要求，CLI 会从紧邻调用的 snapshot/catalog 填充
缺省值；可重试 workflow 仍应显式传入稳定 `--client-instance-id` 和
`--idempotency-key`。同一幂等 key 只有在相同 client identity、method 与 payload 下才能安全
重放。`--transaction <id>` 只适用于 descriptor 标记为 reversible 的操作。

每个请求支持全局 `--timeout <seconds>`，单次调用可用
`--request-timeout <seconds>` 覆盖。Ctrl+C 会发送 best-effort protocol cancel；Server 只在
排队项已移除或正在执行的 semantic operation 实际观察到取消时报告 cancelled。

分页列表使用服务端签名的 opaque cursor：

```powershell
dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery `
  build list --page-size 100 --sort startedAtUtc:desc
```

`--filter-json` 接受协议 schema 中的 `queryFilter`。cursor 绑定实例、filter/sort、revision 和
租期；不要解析或修改它。

## 4. Build 与 Player

命名命令复用 Editor Build Settings 面板的真实 build service：

```powershell
$buildScopes = "editor.read,process.build"

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery --scopes $buildScopes build preflight

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery --scopes $buildScopes --output json `
  build start --wait --idempotency-key build-ci-42
```

`build start` 返回稳定 build ID；`build get|wait|cancel <id>` 查询、等待或请求取消。
使用 `build start --launch --wait` 时，只有产物构建成功且受信 launcher 真正启动才退出 0；
启动失败会保留成功 build，并在 `playerLaunchError` 返回原因，之后可显式重试 `player launch`。
`build logs <id>` 将有界结构化日志导出为 artifact，并默认同时让 Server 和 Client 重验长度及
SHA256。只有明确不需要本地完整性检查时才使用 `--no-verify`。

Build 子进程 stdout/stderr 始终并发排空；单条原始输出最多保留 16 Ki 字符，进度消息最多
8 Ki 字符，结构化失败原因最多 32 Ki 字符，磁盘 `build.log` 最多 16 MiB。超过上限会保留明确
截断标记并继续排空子进程，既不允许无界内存/磁盘增长，也不因停止读取造成子进程死锁。

成功 build 的 launcher 只能通过受信 build result 启动，不接受任意 executable 或 argv：

```powershell
$playerScopes = "editor.read,process.build,process.launch"

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery --scopes $playerScopes `
  player launch <build-id> --wait

dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery --scopes $playerScopes `
  player terminate <player-process-id> --wait
```

`player list|get|wait` 使用 Editor 分配的稳定 process ID；PID 只作诊断，并与进程启动时间一起
防止 PID reuse。

## 5. 事件重连

事件是流，使用 compact 或 NDJSON；`--output json` 会被拒绝，避免输出多个对象却伪装成一个
JSON document：

```powershell
dotnet run --project $cli -c Release --no-build -- `
  --discovery-root $discovery `
  --output ndjson `
  events follow `
  --subscription-key ci-observer-42 `
  --types editor.scene.changed,editor.play.changed `
  --max-events 100
```

CLI 输出 subscription 的 resume token 和最后 ack sequence。断线后以相同
`--subscription-key`、`--resume-token` 和 `--after-sequence` 重连。若 replay window 已淘汰，
Server 返回 `resync_required`；客户端必须重新抓取 snapshot，不能跳过缺失事件。

## 6. 输出与退出码

- 默认 compact 输出适合终端和管道；revision、resume state 等控制信息写 stderr。
- `--output json` 为一次性命令输出一个 JSON document；`call/help` 使用
  `{payload, revision}` envelope，discovery、catalog、matrix 与 artifact 使用各自发布 schema。
- `--output ndjson` 为事件和其他流式消费逐行输出 JSON。
- artifact 的 compact 输出为 `path<TAB>sha256<TAB>byteLength`；大型结果不进入 wire payload。

稳定退出码：

| Code | 含义 |
|---:|---|
| 0 | 请求或查询成功；等待的 build/player 目标也成功 |
| 1 | CLI 未预期内部错误，且不输出 stack trace |
| 2 | 参数、JSON 或本地输入无效 |
| 3 | 无 live instance、发现或连接失败 |
| 4 | Server 返回结构化 remote error |
| 5 | deadline/timeout |
| 6 | artifact 长度或 SHA256 验证失败 |
| 7 | build preflight 失败，或等待的 build Failed/Cancelled |
| 8 | 等待的 player 未确认正常退出或退出码非 0 |
| 130 | 用户取消 |

`build get` 和 `player get` 只查询状态，所以查询成功时返回 0，即使目标本身已失败；使用
`wait` 或解析 JSON state 判断目标结果。

## 7. .NET Client

应用引用 `PixelEngine.Editor.Automation.Client` 和
`PixelEngine.Editor.Automation.Protocol`。连接必须显式提供客户端 identity、版本、scope 与
timeout：

```csharp
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;

AutomationDiscoverySnapshot discovery = await AutomationDiscovery.DiscoverAsync(discoveryRoot);
AutomationDiscoveredInstance instance = discovery.Instances.Single();

await using EditorAutomationClient client = await EditorAutomationClient.ConnectAsync(
    instance,
    new AutomationClientOptions
    {
        ClientInstanceId = "my-tool-session-42",
        ClientName = "my-pixelengine-tool",
        ClientVersion = "1.0.0",
        RequestedScopes = [AutomationScopes.EditorRead],
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromSeconds(30),
    });

AutomationPingResponse ping = await client.PingAsync();
AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync();
AutomationTypedInvocationResult<AutomationCapabilityMatrixSnapshot> matrix =
    await client.GetCapabilityMatrixAsync();
```

`InvokeDetailedAsync` 返回 payload 与同一安全点的 revision；
`AutomationRevisionPreconditions.FromSnapshot` 生成 optimistic precondition；
`EnumeratePagesAsync` 拒绝循环/无进展 cursor；`SubscribeOrResumeEventsAsync` 和带 subscription
参数的 `AcknowledgeEventsAsync` 返回下一次重连可持久化的状态。Build/player 有专用 typed
方法，其他 capability 可用公开 Protocol DTO 与 source-generated `AutomationJsonContext` 调用
typed generic overload。

`GetCapabilitiesAsync` 会重算 canonical descriptor digest 并与 discovery descriptor 比较；
`GetCapabilityMatrixAsync` 进一步校验 capability/UI command 严格排序、唯一性、双向闭包与三组
SHA256。调用方不应自行解析或信任未经过 Client 校验的矩阵 JSON。

artifact 必须调用 `VerifyArtifactAsync` 后再消费。该方法拒绝 path/relative path 不一致、UNC/
device root 和任一路径段的 reparse point，并流式重算 SHA256，不把大型文件读入托管数组。

## 8. Codex Skill

仓库中的权威 Skill 源位于 [`skills/pixelengine-editor`](../skills/pixelengine-editor)，安装目标为
`$CODEX_HOME/skills/pixelengine-editor`（未设置 `CODEX_HOME` 时使用
`$HOME/.codex/skills/pixelengine-editor`）。Skill 的 `scripts/invoke.ps1` 只解析并调用独立
`pixelengine-editor` CLI，不读取 credential、不直接连接 Named Pipe，也不使用 MCP、Computer
Use、屏幕坐标或 `--scripted-*` probe。

```powershell
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' }
$pe = Join-Path $codexHome 'skills/pixelengine-editor/scripts/invoke.ps1'
& $pe --discovery-root $discovery capabilities --matrix
& $pe --discovery-root $discovery --scopes 'editor.read,editor.control' help workspace.exit
```

PowerShell 调用 `.ps1` wrapper 时，含逗号的 `--scopes` CSV 必须作为一个带引号的参数传入。
Skill 发布前必须通过 `$skill-creator` 的 validator，并在全新 Editor OS 进程上完成
discover、matrix、help 和 public `workspace.exit` forward test；只运行 `--version` 不算验证。

## 9. 发布协议与诊断

wire schema 位于
[`schema/editor-automation-protocol.v1.schema.json`](../schema/editor-automation-protocol.v1.schema.json)。
major 不兼容会拒绝连接；minor 由 hello negotiation 协商。错误包含稳定 code/category、
correlation ID、transient/retry 信息和可用 revision，不应按本地化 message 编写控制流。

Server 的 JSONL audit 记录 principal/session/request/capability/result/duration/revision，不记录
payload、HMAC proof 或 secret。自动化 Server 可用 `--disable-automation` 显式关闭；player
产物不包含 Editor automation Server。
