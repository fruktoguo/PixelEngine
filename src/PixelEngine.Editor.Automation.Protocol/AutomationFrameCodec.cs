using System.Buffers.Binary;
using System.Text.Json;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// 为 stream transport 提供固定 header 与有界 JSON payload 编解码。
/// </summary>
public static class AutomationFrameCodec
{
    private const uint Magic = 0x31414550u; // little-endian ASCII "PEA1"

    /// <summary>
    /// 写入一个完整 envelope frame。
    /// </summary>
    /// <param name="stream">目标 stream。</param>
    /// <param name="envelope">要写入的 envelope。</param>
    /// <param name="maxFrameBytes">payload 上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async ValueTask WriteAsync(
        Stream stream,
        AutomationEnvelope envelope,
        int maxFrameBytes = AutomationProtocolConstants.DefaultMaxFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateMaxFrameBytes(maxFrameBytes);
        ValidateEnvelope(envelope);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AutomationJsonContext.Default.AutomationEnvelope);
        if (payload.Length == 0 || payload.Length > maxFrameBytes)
        {
            throw new AutomationProtocolException(
                $"Automation frame payload 必须位于 1..{maxFrameBytes} 字节，实际为 {payload.Length}。");
        }

        byte[] header = GC.AllocateUninitializedArray<byte>(AutomationProtocolConstants.FrameHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), AutomationProtocolConstants.FrameHeaderVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), checked((uint)payload.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), 0);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取并验证一个完整 envelope frame。
    /// </summary>
    /// <param name="stream">源 stream。</param>
    /// <param name="maxFrameBytes">payload 上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的 envelope。</returns>
    public static async ValueTask<AutomationEnvelope> ReadAsync(
        Stream stream,
        int maxFrameBytes = AutomationProtocolConstants.DefaultMaxFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateMaxFrameBytes(maxFrameBytes);

        byte[] header = GC.AllocateUninitializedArray<byte>(AutomationProtocolConstants.FrameHeaderSize);
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4)) != Magic)
        {
            throw new AutomationProtocolException("Automation frame magic 无效。");
        }

        ushort headerVersion = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4, 2));
        if (headerVersion != AutomationProtocolConstants.FrameHeaderVersion)
        {
            throw new AutomationProtocolException($"不支持 automation frame header v{headerVersion}。");
        }

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        if (flags != 0 || reserved != 0)
        {
            throw new AutomationProtocolException("Automation frame 使用了 v1 未定义的 flags/reserved 位。");
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        if (payloadLength == 0 || payloadLength > maxFrameBytes)
        {
            throw new AutomationProtocolException(
                $"Automation frame payload 必须位于 1..{maxFrameBytes} 字节，实际为 {payloadLength}。");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(checked((int)payloadLength));
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        try
        {
            AutomationEnvelope? envelope = JsonSerializer.Deserialize(
                payload,
                AutomationJsonContext.Default.AutomationEnvelope) ?? throw new AutomationProtocolException("Automation frame payload 不能反序列化为 envelope。");
            ValidateEnvelope(envelope);
            return envelope;
        }
        catch (JsonException exception)
        {
            throw new AutomationProtocolException("Automation frame JSON 无效。", exception);
        }
    }

    private static void ValidateMaxFrameBytes(int maxFrameBytes)
    {
        if (maxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes), "最大 frame 字节数必须为正数。");
        }
    }

    private static void ValidateEnvelope(AutomationEnvelope envelope)
    {
        if (envelope.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion)
        {
            throw new AutomationProtocolException($"不支持 envelope schema v{envelope.SchemaVersion}。");
        }

        if (envelope.Protocol is null || envelope.Protocol.Major <= 0 || envelope.Protocol.Minor < 0)
        {
            throw new AutomationProtocolException("Automation envelope protocol version 无效。");
        }

        ValidateIdentifier(envelope.MessageId, "messageId", 128, required: true);
        ValidateIdentifier(envelope.CorrelationId, "correlationId", 128, required: false);
        ValidateIdentifier(envelope.Method, "method", 256, required: false);
        ValidateIdentifier(envelope.SessionId, "sessionId", 128, required: false);
        if (!Enum.IsDefined(envelope.Kind))
        {
            throw new AutomationProtocolException("Automation envelope kind 无效。");
        }

        if (envelope.Kind is AutomationMessageKind.Request or AutomationMessageKind.Cancel && envelope.Error is not null)
        {
            throw new AutomationProtocolException("Request/Cancel envelope 不得携带 error。");
        }

        if (envelope.Error is not null && envelope.Payload is not null)
        {
            throw new AutomationProtocolException("Automation response 不得同时携带 payload 与 error。");
        }

        if (envelope.Error is not null &&
            (envelope.Error.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
             string.IsNullOrWhiteSpace(envelope.Error.Code) || string.IsNullOrWhiteSpace(envelope.Error.Message) ||
             !Enum.IsDefined(envelope.Error.Category) || envelope.Error.RetryAfterMilliseconds < 0 ||
             envelope.Error.CurrentRevision < 0))
        {
            throw new AutomationProtocolException("Automation envelope error contract 无效。");
        }
    }

    private static void ValidateIdentifier(string? value, string field, int maxLength, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new AutomationProtocolException($"Automation envelope {field} 不能为空。");
            }

            return;
        }

        if (value.Length > maxLength || value.Any(char.IsControl))
        {
            throw new AutomationProtocolException($"Automation envelope {field} 长度或字符无效。");
        }
    }
}
