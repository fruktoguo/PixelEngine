using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

public sealed partial class EditorAutomationClient
{
    /// <summary>逐页读取任何 pageRequest capability，并拒绝无进展或循环 cursor。</summary>
    /// <typeparam name="TResponse">分页响应 DTO。</typeparam>
    /// <typeparam name="TItem">条目 DTO。</typeparam>
    /// <param name="method">稳定 list method。</param>
    /// <param name="request">首屏 filter/sort/page size；可携带恢复 cursor。</param>
    /// <param name="responseTypeInfo">响应 source-generated metadata。</param>
    /// <param name="getItems">取得本页条目。</param>
    /// <param name="getPage">取得分页元数据。</param>
    /// <param name="options">每一页的请求选项。</param>
    /// <param name="cancellationToken">停止枚举。</param>
    /// <returns>按服务端稳定顺序展开的条目流。</returns>
    public async IAsyncEnumerable<TItem> EnumeratePagesAsync<TResponse, TItem>(
        string method,
        AutomationPageRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        Func<TResponse, IReadOnlyList<TItem>> getItems,
        Func<TResponse, AutomationPageInfo> getPage,
        AutomationInvocationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
        ArgumentNullException.ThrowIfNull(getItems);
        ArgumentNullException.ThrowIfNull(getPage);
        if (request.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            request.PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Page request schema 或 pageSize 无效。");
        }

        string? cursor = request.Cursor;
        HashSet<string> visitedCursors = new(StringComparer.Ordinal);
        int? expectedTotal = null;
        int returnedTotal = 0;
        if (cursor is not null)
        {
            _ = visitedCursors.Add(cursor);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationTypedInvocationResult<TResponse> invocation = await InvokeDetailedAsync(
                method,
                request with { Cursor = cursor },
                AutomationJsonContext.Default.AutomationPageRequest,
                responseTypeInfo,
                options,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<TItem> items = getItems(invocation.Response) ??
                throw new AutomationConnectionException($"Automation method '{method}' 返回 null items。");
            AutomationPageInfo page = getPage(invocation.Response) ??
                throw new AutomationConnectionException($"Automation method '{method}' 返回 null page。");
            ValidatePage(method, request.PageSize, items.Count, page);
            expectedTotal ??= page.Total;
            if (page.Total != expectedTotal)
            {
                throw new AutomationConnectionException(
                    $"Automation method '{method}' 在分页期间返回漂移的 total。");
            }

            if (items.Count > int.MaxValue - returnedTotal)
            {
                throw new AutomationConnectionException(
                    $"Automation method '{method}' 返回的累计条目数溢出。");
            }

            returnedTotal += items.Count;
            if (returnedTotal > page.Total)
            {
                throw new AutomationConnectionException(
                    $"Automation method '{method}' 返回的累计条目数超过 total。");
            }

            for (int i = 0; i < items.Count; i++)
            {
                yield return items[i];
            }

            if (page.NextCursor is null)
            {
                if (request.Cursor is null && returnedTotal != page.Total)
                {
                    throw new AutomationConnectionException(
                        $"Automation method '{method}' 结束分页时累计条目数与 total 不一致。");
                }

                yield break;
            }

            if (!visitedCursors.Add(page.NextCursor))
            {
                throw new AutomationConnectionException(
                    $"Automation method '{method}' 返回重复 cursor，分页无进展。");
            }

            cursor = page.NextCursor;
        }
    }

    private static void ValidatePage(
        string method,
        int pageSize,
        int itemCount,
        AutomationPageInfo page)
    {
        if (page.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            page.Returned != itemCount ||
            page.Returned < 0 ||
            page.Returned > pageSize ||
            page.Total < page.Returned ||
            (page.Returned == 0 && page.NextCursor is not null) ||
            page.NextCursor is { Length: > 4096 })
        {
            throw new AutomationConnectionException(
                $"Automation method '{method}' 返回无效分页元数据。");
        }
    }
}
