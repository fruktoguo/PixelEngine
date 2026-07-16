using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

public sealed partial class EditorAutomationClient
{
    /// <summary>读取完整 capability catalog，并拒绝跨页 digest 漂移或重复 ID。</summary>
    /// <param name="pageSize">单页条目数，范围 1..500。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同一 digest 下的完整 catalog。</returns>
    public async ValueTask<AutomationCapabilityCatalog> GetCapabilitiesAsync(
        int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 500);
        AutomationPageRequest request = new() { PageSize = pageSize };
        List<AutomationCapabilityDescriptor> descriptors = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> cursors = new(StringComparer.Ordinal);
        string? cursor = null;
        string? digest = null;
        int? total = null;
        AutomationRevisionSnapshot? revision;
        while (true)
        {
            AutomationTypedInvocationResult<AutomationCapabilityListResponse> invocation =
                await InvokeDetailedAsync(
                    AutomationProtocolConstants.CapabilityListMethod,
                    request with { Cursor = cursor },
                    AutomationJsonContext.Default.AutomationPageRequest,
                    AutomationJsonContext.Default.AutomationCapabilityListResponse,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            AutomationCapabilityListResponse response = invocation.Response;
            revision = invocation.Revision;
            ValidatePage(
                AutomationProtocolConstants.CapabilityListMethod,
                pageSize,
                response.Items.Length,
                response.Page);
            digest ??= response.CapabilityDigest;
            total ??= response.Page.Total;
            if (!IsLowerSha256(response.CapabilityDigest) ||
                !string.Equals(digest, response.CapabilityDigest, StringComparison.Ordinal) ||
                total != response.Page.Total)
            {
                throw new AutomationConnectionException(
                    "Capability catalog 在分页期间发生 digest/total 漂移。");
            }

            for (int i = 0; i < response.Items.Length; i++)
            {
                AutomationCapabilityDescriptor descriptor = response.Items[i] ??
                    throw new AutomationConnectionException("Capability catalog 包含 null descriptor。");
                if (!ids.Add(descriptor.Id))
                {
                    throw new AutomationConnectionException(
                        $"Capability catalog 包含重复 ID '{descriptor.Id}'。");
                }

                descriptors.Add(descriptor);
            }

            cursor = response.Page.NextCursor;
            if (cursor is null)
            {
                break;
            }

            if (!cursors.Add(cursor))
            {
                throw new AutomationConnectionException("Capability catalog 返回重复 cursor。");
            }
        }

        if (descriptors.Count != total)
        {
            throw new AutomationConnectionException(
                $"Capability catalog 条目数 {descriptors.Count} 与 total {total} 不一致。");
        }

        AutomationCapabilityDescriptor[] items = [.. descriptors];
        for (int i = 1; i < items.Length; i++)
        {
            if (string.CompareOrdinal(items[i - 1].Id, items[i].Id) >= 0)
            {
                throw new AutomationConnectionException("Capability catalog IDs 未严格按 ordinal 排序。");
            }
        }

        string publishedDigest = digest ?? throw new AutomationConnectionException(
            "Capability catalog 缺少 digest。");
        string canonicalDigest = ComputeDigest(
            items,
            AutomationJsonContext.Default.AutomationCapabilityDescriptorArray);
        ValidatePublishedCapabilityDigest(publishedDigest, canonicalDigest, "catalog response");
        ValidatePublishedCapabilityDigest(
            publishedDigest,
            Instance.Descriptor.CapabilityDigest,
            "discovery descriptor");
        return new AutomationCapabilityCatalog
        {
            CapabilityDigest = publishedDigest,
            Items = items,
            Revision = revision,
        };
    }

    private static bool IsLowerSha256(string value)
    {
        return value is { Length: 64 } && value.All(static character =>
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    }

    private static void ValidatePublishedCapabilityDigest(
        string actual,
        string expected,
        string source)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new AutomationConnectionException(
                $"Capability digest 与 {source} 不匹配。");
        }
    }
}
