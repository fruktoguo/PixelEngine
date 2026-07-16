using System.Text.Json;
using PixelEngine.Editor.Cli;
using PixelEngine.Editor.Automation.Protocol;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>独立 CLI 的单连接 transaction plan 必须严格、有界且可重试标识唯一。</summary>
public sealed class EditorAutomationCliTransactionTests
{
    /// <summary>合法 plan 保留 operation 顺序、method、payload 与幂等键。</summary>
    [Fact]
    public void ValidTransactionPlanPreservesOrderedOperations()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "schemaVersion": 1,
              "name": "Create marker",
              "leaseMilliseconds": 60000,
              "idempotencyKey": "e2e-create-marker",
              "operations": [
                {
                  "method": "hierarchy.gameObject.create",
                  "payload": { "schemaVersion": 1, "name": "Marker" },
                  "idempotencyKey": "e2e-create-marker-operation"
                }
              ]
            }
            """);

        CliTransactionPlan plan = CliTransactionPlanReader.Parse(document.RootElement);

        Assert.Equal("Create marker", plan.Name);
        CliTransactionOperation operation = Assert.Single(plan.Operations);
        Assert.Equal("hierarchy.gameObject.create", operation.Method);
        Assert.Equal("Marker", operation.Payload?.GetProperty("name").GetString());
    }

    /// <summary>schema、lease 与 operation 数量任一越界都必须在连接前拒绝。</summary>
    [Theory]
    [InlineData(0, 60000, 1)]
    [InlineData(2, 60000, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 300001, 1)]
    [InlineData(1, 60000, 0)]
    [InlineData(1, 60000, 257)]
    public void InvalidTransactionBoundsAreRejected(int schemaVersion, int lease, int operationCount)
    {
        object[] operations =
        [
            .. Enumerable.Range(0, operationCount)
                .Select(index => (object)new
                {
                    method = "hierarchy.gameObject.create",
                    payload = new { schemaVersion = 1, name = "Marker" },
                    idempotencyKey = $"operation-{index}",
                }),
        ];
        JsonElement json = JsonSerializer.SerializeToElement(new
        {
            schemaVersion,
            name = "Bounds",
            leaseMilliseconds = lease,
            idempotencyKey = "bounds-plan",
            operations,
        });

        _ = Assert.Throws<CliUsageException>(() => CliTransactionPlanReader.Parse(json));
    }

    /// <summary>同一 transaction 内重复幂等键会让结果归因不唯一，必须拒绝。</summary>
    [Fact]
    public void DuplicateOperationIdempotencyKeysAreRejected()
    {
        JsonElement json = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            name = "Duplicate keys",
            leaseMilliseconds = 60000,
            idempotencyKey = "duplicate-plan",
            operations = new[]
            {
                new
                {
                    method = "hierarchy.gameObject.create",
                    payload = new { schemaVersion = 1, name = "A" },
                    idempotencyKey = "same-key",
                },
                new
                {
                    method = "hierarchy.gameObject.create",
                    payload = new { schemaVersion = 1, name = "B" },
                    idempotencyKey = "same-key",
                },
            },
        });

        CliUsageException exception = Assert.Throws<CliUsageException>(
            () => CliTransactionPlanReader.Parse(json));
        Assert.Contains("重复", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>plan 与 operation 均使用 strict JSON，未知成员不能静默忽略。</summary>
    [Fact]
    public void UnknownPlanMemberIsRejected()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "schemaVersion": 1,
              "name": "Unknown",
              "leaseMilliseconds": 60000,
              "idempotencyKey": "unknown-plan",
              "operations": [
                {
                  "method": "hierarchy.gameObject.create",
                  "idempotencyKey": "unknown-operation"
                }
              ],
              "unexpected": true
            }
            """);

        _ = Assert.Throws<JsonException>(() => CliTransactionPlanReader.Parse(document.RootElement));
    }

    /// <summary>status 查询可接受全部权威状态，但显式 rollback 回执只能是 RolledBack。</summary>
    [Theory]
    [InlineData(AutomationTransactionStatus.Active, true, true)]
    [InlineData(AutomationTransactionStatus.Committed, true, true)]
    [InlineData(AutomationTransactionStatus.RolledBack, true, true)]
    [InlineData(AutomationTransactionStatus.Expired, true, true)]
    [InlineData(AutomationTransactionStatus.Active, false, false)]
    [InlineData(AutomationTransactionStatus.Committed, false, false)]
    [InlineData(AutomationTransactionStatus.RolledBack, false, true)]
    [InlineData(AutomationTransactionStatus.Expired, false, false)]
    public void RecoveryStatusValidationDistinguishesStatusQueryFromRollbackReceipt(
        AutomationTransactionStatus status,
        bool allowActive,
        bool accepted)
    {
        AutomationTransactionInfo transaction = CreateTransactionInfo("transaction-1", status);

        if (accepted)
        {
            CliApplication.ValidateRecoveredTransaction(transaction, "transaction-1", allowActive);
        }
        else
        {
            _ = Assert.Throws<InvalidDataException>(
                () => CliApplication.ValidateRecoveredTransaction(transaction, "transaction-1", allowActive));
        }
    }

    /// <summary>即使终态合法，恢复回执也不能替换成另一个 transaction identity。</summary>
    [Fact]
    public void RecoveryStatusRejectsMismatchedTransactionIdentity()
    {
        AutomationTransactionInfo transaction = CreateTransactionInfo(
            "substituted-transaction",
            AutomationTransactionStatus.RolledBack);

        _ = Assert.Throws<InvalidDataException>(
            () => CliApplication.ValidateRecoveredTransaction(transaction, "transaction-1", allowActive: true));
    }

    private static AutomationTransactionInfo CreateTransactionInfo(
        string transactionId,
        AutomationTransactionStatus status)
    {
        return new AutomationTransactionInfo
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            TransactionId = transactionId,
            SessionId = "session-1",
            Name = "Transaction",
            Status = status,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            ExpiresAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
            OperationCount = 1,
            ResourceIds = [],
            BaseRevision = new AutomationRevisionSnapshot
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                GlobalRevision = 0,
                Resources = [],
            },
        };
    }
}
