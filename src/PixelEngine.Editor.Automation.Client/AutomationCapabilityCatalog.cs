using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>来自同一实例 digest 的完整 capability catalog。</summary>
public sealed record AutomationCapabilityCatalog
{
    /// <summary>descriptor canonical SHA256。</summary>
    public required string CapabilityDigest { get; init; }

    /// <summary>按服务端稳定顺序返回的全部 descriptors。</summary>
    public required AutomationCapabilityDescriptor[] Items { get; init; }

    /// <summary>最后一页响应对应的权威 revision，可用于紧随其后的 optimistic command。</summary>
    public AutomationRevisionSnapshot? Revision { get; init; }
}
