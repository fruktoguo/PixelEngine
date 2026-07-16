using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>公开 revision snapshot 到 optimistic precondition 的无损转换。</summary>
public static class AutomationRevisionPreconditions
{
    /// <summary>用 snapshot 的 global revision 与全部资源 revision 创建前置条件。</summary>
    /// <param name="snapshot">先前读取到的权威 snapshot。</param>
    /// <param name="includeGlobalRevision">是否同时约束 global revision。</param>
    /// <returns>可直接用于写请求的深拷贝前置条件。</returns>
    public static AutomationRevisionPrecondition FromSnapshot(
        AutomationRevisionSnapshot snapshot,
        bool includeGlobalRevision = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AutomationRevisionPrecondition
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = includeGlobalRevision ? snapshot.GlobalRevision : null,
            Resources =
            [
                .. snapshot.Resources.Select(static resource => new AutomationExpectedResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = resource.ResourceId,
                    Revision = resource.Revision,
                }),
            ],
        };
    }
}
