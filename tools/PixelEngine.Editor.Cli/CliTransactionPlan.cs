using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Cli;

/// <summary>CLI 单连接 transaction execute 的有界计划。</summary>
internal sealed record CliTransactionPlan
{
    public required int SchemaVersion { get; init; }

    public required string Name { get; init; }

    public required int LeaseMilliseconds { get; init; }

    public required string IdempotencyKey { get; init; }

    public required CliTransactionOperation[] Operations { get; init; }
}

/// <summary>transaction plan 中一个按序 staging 的 semantic write。</summary>
internal sealed record CliTransactionOperation
{
    public required string Method { get; init; }

    public JsonElement? Payload { get; init; }

    public required string IdempotencyKey { get; init; }
}

/// <summary>严格解析并验证 CLI transaction plan，不允许未知字段或无界 operation。</summary>
internal static class CliTransactionPlanReader
{
    public static CliTransactionPlan Parse(JsonElement json)
    {
        CliTransactionPlan plan = json.Deserialize(CliTransactionJsonContext.Default.CliTransactionPlan)
            ?? throw new CliUsageException("Transaction plan 不能是 null。");
        if (plan.SchemaVersion != 1)
        {
            throw new CliUsageException("Transaction plan schemaVersion 必须为 1。");
        }

        if (!IsBoundedText(plan.Name, 128))
        {
            throw new CliUsageException("Transaction plan name 必须为 1..128 个字符。");
        }

        if (plan.LeaseMilliseconds is <= 0 or > 300_000)
        {
            throw new CliUsageException("Transaction plan leaseMilliseconds 必须为 1..300000。");
        }

        if (!IsBoundedText(plan.IdempotencyKey, 128))
        {
            throw new CliUsageException("Transaction plan idempotencyKey 必须为 1..128 个字符。");
        }

        if (plan.Operations is not { Length: > 0 and <= 256 })
        {
            throw new CliUsageException("Transaction plan operations 数量必须为 1..256。");
        }

        HashSet<string> idempotencyKeys = new(StringComparer.Ordinal);
        for (int i = 0; i < plan.Operations.Length; i++)
        {
            CliTransactionOperation operation = plan.Operations[i] ??
                throw new CliUsageException($"Transaction plan operation[{i}] 不能为 null。");
            if (!IsBoundedText(operation.Method, 256))
            {
                throw new CliUsageException($"Transaction plan operation[{i}].method 必须为 1..256 个字符。");
            }

            if (!IsBoundedText(operation.IdempotencyKey, 128))
            {
                throw new CliUsageException(
                    $"Transaction plan operation[{i}].idempotencyKey 必须为 1..128 个字符。");
            }

            if (!idempotencyKeys.Add(operation.IdempotencyKey))
            {
                throw new CliUsageException(
                    $"Transaction plan operation idempotencyKey 重复：{operation.IdempotencyKey}。");
            }
        }

        return plan;
    }

    private static bool IsBoundedText(string? value, int maximumLength)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CliTransactionPlan))]
internal sealed partial class CliTransactionJsonContext : JsonSerializerContext;
