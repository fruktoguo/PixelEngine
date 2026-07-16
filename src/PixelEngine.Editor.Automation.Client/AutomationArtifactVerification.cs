using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>Server 与本地文件两侧的 artifact 完整性结果。</summary>
public sealed record AutomationArtifactVerification
{
    /// <summary>被验证的原始引用。</summary>
    public required AutomationArtifactReference Artifact { get; init; }

    /// <summary>Server 重验结果；未请求远端重验时为空。</summary>
    public bool? ServerVerified { get; init; }

    /// <summary>本地 canonical path 的长度和 SHA256 是否匹配。</summary>
    public required bool LocalVerified { get; init; }

    /// <summary>只有所有已请求层都通过时为 true。</summary>
    public required bool Verified { get; init; }

    /// <summary>实际本地字节数；文件不存在时为空。</summary>
    public long? ActualByteLength { get; init; }

    /// <summary>实际本地 SHA256；未完成 hash 时为空。</summary>
    public string? ActualSha256 { get; init; }

    /// <summary>稳定诊断。</summary>
    public required string Diagnostic { get; init; }
}
