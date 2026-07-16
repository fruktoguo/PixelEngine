# PixelEngine Editor Automation .NET Client

该包为外部 .NET 进程提供 PixelEngine Editor v1 自动化 Client：

- 校验 PID/process-start identity、descriptor、credential root 与 Named Pipe 双向 HMAC；
- 支持并发 correlation、deadline、显式 cancel 和结构化 remote error；
- 提供 source-generated typed invocation、完整 capability catalog 与安全分页；
- 支持 event ack/resume state、断线续订及 `resync_required`；
- 提供 build/player typed API，以及 Server canonical metadata + 本地流式 SHA256 artifact 验证。

连接时必须显式提供稳定 `ClientInstanceId`、名称、版本和最小 scope。CLI 与脚本默认只应申请
`editor.read`，写入、build 和 player launch 再按 capability descriptor 增加权限。跨进程重试
同一写操作时，必须同时保持 client identity 和 idempotency key 不变。

```csharp
AutomationDiscoverySnapshot discovery = await AutomationDiscovery.DiscoverAsync(discoveryRoot);
AutomationDiscoveredInstance instance = discovery.Instances.Single();

await using EditorAutomationClient client = await EditorAutomationClient.ConnectAsync(
    instance,
    new AutomationClientOptions
    {
        ClientInstanceId = "my-tool-run-42",
        ClientName = "my-tool",
        ClientVersion = "1.0.0",
        RequestedScopes = [AutomationScopes.EditorRead],
    });

AutomationCapabilityCatalog capabilities = await client.GetCapabilitiesAsync();
```

示例需要同时引用 `PixelEngine.Editor.Automation.Protocol` namespace。完整使用说明见 PixelEngine
仓库的 `docs/editor-automation-api.md`。
