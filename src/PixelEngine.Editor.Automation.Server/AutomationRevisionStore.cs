using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 线程安全的 global/resource optimistic revision 权威存储。
/// </summary>
public sealed class AutomationRevisionStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, long> _resources = new(StringComparer.Ordinal);
    private long _globalRevision;

    /// <summary>
    /// 创建 revision store。
    /// </summary>
    /// <param name="initialGlobalRevision">初始 global revision。</param>
    public AutomationRevisionStore(long initialGlobalRevision = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialGlobalRevision);
        _globalRevision = initialGlobalRevision;
    }

    /// <summary>当前 global revision。</summary>
    public long GlobalRevision
    {
        get
        {
            lock (_sync)
            {
                return _globalRevision;
            }
        }
    }

    /// <summary>
    /// 捕获指定资源；空集合只返回 global revision。
    /// </summary>
    /// <param name="resourceIds">稳定资源 id。</param>
    /// <returns>不可变 revision snapshot。</returns>
    public AutomationRevisionSnapshot Capture(IEnumerable<string>? resourceIds = null)
    {
        string[] normalized = NormalizeResourceIds(resourceIds);
        lock (_sync)
        {
            return CaptureLocked(normalized);
        }
    }

    /// <summary>
    /// 捕获全部已登记资源，供 transaction before revision 使用。
    /// </summary>
    /// <returns>按 resource id 稳定排序的 snapshot。</returns>
    public AutomationRevisionSnapshot CaptureAll()
    {
        lock (_sync)
        {
            return CaptureLocked([.. _resources.Keys.Order(StringComparer.Ordinal)]);
        }
    }

    /// <summary>
    /// 校验请求前置条件；冲突时抛出包含当前 revision 的结构化错误。
    /// </summary>
    /// <param name="precondition">调用方观察到的 revision。</param>
    public void Validate(AutomationRevisionPrecondition precondition)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ValidatePreconditionShape(precondition);
        lock (_sync)
        {
            List<AutomationResourceRevisionConflict> conflicts = [];
            for (int i = 0; i < precondition.Resources.Length; i++)
            {
                AutomationExpectedResourceRevision expected = precondition.Resources[i];
                long current = _resources.GetValueOrDefault(expected.ResourceId);
                if (current != expected.Revision)
                {
                    conflicts.Add(new AutomationResourceRevisionConflict
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        ResourceId = expected.ResourceId,
                        ExpectedRevision = expected.Revision,
                        CurrentRevision = current,
                    });
                }
            }

            bool globalConflict = precondition.GlobalRevision.HasValue &&
                precondition.GlobalRevision.Value != _globalRevision;
            if (!globalConflict && conflicts.Count == 0)
            {
                return;
            }

            AutomationRevisionConflictDetails details = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                ExpectedGlobalRevision = precondition.GlobalRevision,
                CurrentGlobalRevision = _globalRevision,
                ResourceConflicts = [.. conflicts],
            };
            throw new AutomationRequestException(new AutomationError
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Code = AutomationErrorCodes.RevisionConflict,
                Category = AutomationErrorCategory.Conflict,
                Message = "Automation write 的 expected revision 已过期。",
                Details = JsonSerializer.SerializeToElement(
                    details,
                    AutomationJsonContext.Default.AutomationRevisionConflictDetails),
                Transient = false,
                CurrentRevision = _globalRevision,
            });
        }
    }

    /// <summary>
    /// 原子推进 global revision，并推进所有受影响资源一次。
    /// </summary>
    /// <param name="resourceIds">受影响资源。</param>
    /// <returns>推进后的 snapshot。</returns>
    public AutomationRevisionSnapshot Advance(IEnumerable<string>? resourceIds)
    {
        string[] normalized = NormalizeResourceIds(resourceIds);
        lock (_sync)
        {
            // 先验证全部计数器，再写入任何字段，保证极限值失败时 store 仍保持原子不变。
            _ = checked(_globalRevision + 1);
            for (int i = 0; i < normalized.Length; i++)
            {
                _ = checked(_resources.GetValueOrDefault(normalized[i]) + 1);
            }

            _globalRevision++;
            for (int i = 0; i < normalized.Length; i++)
            {
                string resourceId = normalized[i];
                _resources[resourceId] = _resources.GetValueOrDefault(resourceId) + 1;
            }

            return CaptureLocked(normalized);
        }
    }

    /// <summary>
    /// 在执行可逆 semantic mutation 后、登记 Undo 前验证下一次推进一定不会因计数器边界失败。
    /// 该方法与紧随其后的 <see cref="Advance" /> 必须位于同一 Editor 主线程 safe point。
    /// </summary>
    /// <param name="resourceIds">将受影响的资源。</param>
    public void EnsureCanAdvance(IEnumerable<string>? resourceIds)
    {
        string[] normalized = NormalizeResourceIds(resourceIds);
        lock (_sync)
        {
            _ = checked(_globalRevision + 1);
            for (int i = 0; i < normalized.Length; i++)
            {
                _ = checked(_resources.GetValueOrDefault(normalized[i]) + 1);
            }
        }
    }

    private AutomationRevisionSnapshot CaptureLocked(ReadOnlySpan<string> resourceIds)
    {
        AutomationResourceRevision[] resources = new AutomationResourceRevision[resourceIds.Length];
        for (int i = 0; i < resourceIds.Length; i++)
        {
            string resourceId = resourceIds[i];
            resources[i] = new AutomationResourceRevision
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                ResourceId = resourceId,
                Revision = _resources.GetValueOrDefault(resourceId),
            };
        }

        return new AutomationRevisionSnapshot
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = _globalRevision,
            Resources = resources,
        };
    }

    private static string[] NormalizeResourceIds(IEnumerable<string>? resourceIds)
    {
        if (resourceIds is null)
        {
            return [];
        }

        string[] normalized =
        [
            .. resourceIds
                .Select(static resourceId =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
                    return resourceId.Length <= AutomationProtocolConstants.MaxResourceIdLength &&
                        !resourceId.Any(char.IsControl)
                        ? resourceId
                        : throw new ArgumentException(
                            "Automation resource id 长度或字符无效。",
                            nameof(resourceIds));
                })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        return normalized.Length <= AutomationProtocolConstants.MaxRevisionResources
            ? normalized
            : throw new ArgumentException(
                $"Automation revision resource 数不得超过 {AutomationProtocolConstants.MaxRevisionResources}。",
                nameof(resourceIds));
    }

    private static void ValidatePreconditionShape(AutomationRevisionPrecondition precondition)
    {
        if (precondition.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            precondition.GlobalRevision < 0 || precondition.Resources is null ||
            precondition.Resources.Length > AutomationProtocolConstants.MaxRevisionResources ||
            precondition.Resources.Any(static resource =>
                resource is null ||
                resource.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                string.IsNullOrWhiteSpace(resource.ResourceId) ||
                resource.ResourceId.Length > AutomationProtocolConstants.MaxResourceIdLength ||
                resource.ResourceId.Any(char.IsControl) || resource.Revision < 0) ||
            precondition.Resources.Select(static resource => resource.ResourceId)
                .Distinct(StringComparer.Ordinal).Count() != precondition.Resources.Length)
        {
            throw new AutomationRequestException(new AutomationError
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Code = AutomationErrorCodes.InvalidRequest,
                Category = AutomationErrorCategory.Validation,
                Message = "Automation revision precondition schema、值或 resource id 无效。",
                Transient = false,
            });
        }
    }
}
