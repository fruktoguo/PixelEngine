using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>有界、可恢复 JSONL 审计日志测试。</summary>
public sealed class AutomationAuditLogTests
{
    /// <summary>验证轮转不会超过全局文件数/单文件容量，且每条记录保持完整 JSON。</summary>
    [Fact]
    public async Task AuditLogRotatesWithinConfiguredBoundsWithoutPartialRecords()
    {
        using TemporaryDirectory temporary = new();
        const long maxFileBytes = 4L * 1024 * 1024;
        AutomationRevisionSnapshot largeRevision = CreateLargeRevision();
        string currentPath;
        await using (AutomationAuditLog log = new(
                         temporary.Path,
                         "bounded-audit",
                         maxFileBytes,
                         maxFiles: 2))
        {
            currentPath = log.CurrentPath;
            for (int index = 0; index < 7; index++)
            {
                await log.AppendAsync(CreateRecord($"request-{index}", largeRevision));
            }
        }

        string rotatedPath = $"{currentPath}.1";
        Assert.True(File.Exists(currentPath));
        Assert.True(File.Exists(rotatedPath));
        Assert.False(File.Exists($"{currentPath}.2"));
        Assert.InRange(new FileInfo(currentPath).Length, 1, maxFileBytes);
        Assert.InRange(new FileInfo(rotatedPath).Length, 1, maxFileBytes);

        string[] lines =
        [
            .. File.ReadAllLines(rotatedPath),
            .. File.ReadAllLines(currentPath),
        ];
        Assert.NotEmpty(lines);
        Assert.All(lines, static line =>
        {
            using JsonDocument document = JsonDocument.Parse(line);
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        });
    }

    /// <summary>验证进程异常留下的尾部半条记录会在 reopen 时截断。</summary>
    [Fact]
    public async Task AuditLogRepairsTrailingPartialRecordBeforeAppend()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "repair-audit.jsonl");
        await File.WriteAllTextAsync(path, "{}\n{\"partial\"");

        await using (AutomationAuditLog log = new(
                         temporary.Path,
                         "repair-audit",
                         4L * 1024 * 1024,
                         maxFiles: 2))
        {
            await log.AppendAsync(CreateRecord("after-repair", revision: null));
        }

        string[] lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal("{}", lines[0]);
        using JsonDocument document = JsonDocument.Parse(lines[1]);
        Assert.Equal("after-repair", document.RootElement.GetProperty("requestId").GetString());
    }

    private static AutomationAuditRecord CreateRecord(
        string requestId,
        AutomationRevisionSnapshot? revision)
    {
        return new AutomationAuditRecord
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            TimestampUtc = DateTimeOffset.UnixEpoch,
            InstanceId = "bounded-audit",
            SessionId = "session",
            PrincipalId = "principal",
            ClientInstanceId = "client",
            RequestId = requestId,
            CorrelationId = requestId,
            Capability = "test.audit",
            Result = "success",
            DurationMicroseconds = 1,
            Revision = revision,
        };
    }

    private static AutomationRevisionSnapshot CreateLargeRevision()
    {
        string suffix = new('r', 220);
        AutomationResourceRevision[] resources =
        [
            .. Enumerable.Range(0, AutomationProtocolConstants.MaxRevisionResources)
                .Select(index => new AutomationResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = $"scene:{index:D4}:{suffix}",
                    Revision = index,
                }),
        ];
        return new AutomationRevisionSnapshot
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = 1,
            Resources = resources,
        };
    }
}
