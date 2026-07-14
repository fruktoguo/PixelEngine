namespace PixelEngine.Editor.Automation.Client;

/// <summary>
/// .NET automation Client 连接与请求配置。
/// </summary>
public sealed record AutomationClientOptions
{
    /// <summary>hello 中公开的客户端名称。</summary>
    public required string ClientName { get; init; }

    /// <summary>hello 中公开的客户端版本。</summary>
    public required string ClientVersion { get; init; }

    /// <summary>请求的 permission scopes。</summary>
    public required string[] RequestedScopes { get; init; }

    /// <summary>Named Pipe connect timeout。</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>单次请求默认 deadline。</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>可选显式 credential 文件；为空时使用经过 discovery 校验的路径。</summary>
    public string? CredentialPath { get; init; }

    /// <summary>客户端允许接收的最大 frame payload。</summary>
    public int MaxFrameBytes { get; init; } = Protocol.AutomationProtocolConstants.DefaultMaxFrameBytes;

    /// <summary>可测试 UTC 时钟。</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
