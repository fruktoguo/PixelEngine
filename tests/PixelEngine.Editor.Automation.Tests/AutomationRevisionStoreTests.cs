using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>revision store 的输入边界与 optimistic concurrency 测试。</summary>
public sealed class AutomationRevisionStoreTests
{
    /// <summary>验证 global overflow 在写入任何 revision 前失败，不留下半推进 snapshot。</summary>
    [Fact]
    public void AdvanceOverflowLeavesStoreUnchanged()
    {
        AutomationRevisionStore store = new(long.MaxValue);

        _ = Assert.Throws<OverflowException>(() => store.Advance(["scene:main"]));

        AutomationRevisionSnapshot snapshot = store.Capture(["scene:main"]);
        Assert.Equal(long.MaxValue, snapshot.GlobalRevision);
        Assert.Equal(0, Assert.Single(snapshot.Resources).Revision);
    }

    /// <summary>验证直接 API 与 wire codec 使用相同的 resource 数量/字符边界。</summary>
    [Fact]
    public void StoreRejectsOversizedOrControlCharacterResourceIds()
    {
        AutomationRevisionStore store = new();
        string[] oversized =
        [
            .. Enumerable.Range(0, AutomationProtocolConstants.MaxRevisionResources + 1)
                .Select(static index => $"resource:{index}"),
        ];

        _ = Assert.Throws<ArgumentException>(() => store.Capture(oversized));
        _ = Assert.Throws<ArgumentException>(() => store.Advance(["scene:main\n"]));

        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(() =>
            store.Validate(new AutomationRevisionPrecondition
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Resources =
                [
                    new AutomationExpectedResourceRevision
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        ResourceId = new string('x', AutomationProtocolConstants.MaxResourceIdLength + 1),
                        Revision = 0,
                    },
                ],
            }));
        Assert.Equal(AutomationErrorCodes.InvalidRequest, exception.Error.Code);
    }
}
