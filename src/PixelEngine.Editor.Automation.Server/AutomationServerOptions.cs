using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// Named Pipe automation Server 配置。
/// </summary>
public sealed record AutomationServerOptions
{
    /// <summary>实例 discovery/credential 根目录。</summary>
    public required string DiscoveryRoot { get; init; }

    /// <summary>Editor/Server 产品版本。</summary>
    public required string EditorVersion { get; init; }

    /// <summary>可选固定 instance id；测试外通常自动生成。</summary>
    public string? InstanceId { get; init; }

    /// <summary>可选固定 pipe name；测试外通常由 instance id 派生。</summary>
    public string? PipeName { get; init; }

    /// <summary>调用方注入的 credential 文件；为空时安全生成。</summary>
    public string? CredentialInputPath { get; init; }

    /// <summary>服务端允许授予的 scopes。</summary>
    public string[] SupportedScopes { get; init; } = AutomationScopes.All;

    /// <summary>控制面最大 frame payload。</summary>
    public int MaxFrameBytes { get; init; } = AutomationProtocolConstants.DefaultMaxFrameBytes;

    /// <summary>单连接最大并发请求数。</summary>
    public int MaxConcurrentRequestsPerConnection { get; init; } = 64;

    /// <summary>同时连接上限。</summary>
    public int MaxConnections { get; init; } = 16;

    /// <summary>未认证连接完成 hello/challenge/HMAC 的总时限。</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>允许客户端声明的最长单请求 deadline 窗口。</summary>
    public TimeSpan MaxRequestLifetime { get; init; } = TimeSpan.FromHours(24);

    /// <summary>能力矩阵 SHA256；节点二尚未装配 capability registry 时使用协议系统面 digest。</summary>
    public string? CapabilityDigest { get; init; }

    /// <summary>当前项目摘要。</summary>
    public AutomationProjectSummary? Project { get; init; }

    /// <summary>可测试 UTC 时钟。</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
