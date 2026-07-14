using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// 有界 frame codec 正确性与拒绝路径测试。
/// </summary>
public sealed class AutomationFrameCodecTests
{
    /// <summary>验证完整 envelope 与 Unicode payload 往返。</summary>
    [Fact]
    public async Task FrameRoundTripsEnvelopeAndPayload()
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 42, text = "像素" });
        AutomationEnvelope expected = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = AutomationProtocolConstants.CurrentVersion,
            MessageId = "request-1",
            Kind = AutomationMessageKind.Request,
            CorrelationId = "correlation-1",
            Method = "test.echo",
            SessionId = "session-1",
            DeadlineUtc = DateTimeOffset.Parse("2026-07-14T12:00:00Z"),
            Payload = payload,
        };

        await using MemoryStream stream = new();
        await AutomationFrameCodec.WriteAsync(stream, expected);
        stream.Position = 0;
        AutomationEnvelope actual = await AutomationFrameCodec.ReadAsync(stream);

        Assert.Equal(expected.Protocol, actual.Protocol);
        Assert.Equal(expected.MessageId, actual.MessageId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Method, actual.Method);
        Assert.Equal(42, actual.Payload!.Value.GetProperty("value").GetInt32());
        Assert.Equal("像素", actual.Payload.Value.GetProperty("text").GetString());
    }

    /// <summary>验证无效 magic 在读取 payload 前被拒绝。</summary>
    [Fact]
    public async Task FrameRejectsBadMagicBeforeAllocatingPayload()
    {
        byte[] header = new byte[AutomationProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0xDEADBEEFu);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(4, 2),
            AutomationProtocolConstants.FrameHeaderVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1);
        await using MemoryStream stream = new([.. header, (byte)'{']);

        AutomationProtocolException exception = await Assert.ThrowsAsync<AutomationProtocolException>(
            async () => await AutomationFrameCodec.ReadAsync(stream));

        Assert.Contains("magic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>验证 header 声明的超限 payload 被拒绝。</summary>
    [Fact]
    public async Task FrameRejectsOversizedPayloadFromHeader()
    {
        const int maximum = 64;
        byte[] header = new byte[AutomationProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x31414550u);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(4, 2),
            AutomationProtocolConstants.FrameHeaderVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), maximum + 1);
        await using MemoryStream stream = new(header);

        AutomationFrameSizeException exception = await Assert.ThrowsAsync<AutomationFrameSizeException>(
            async () => await AutomationFrameCodec.ReadAsync(stream, maximum));

        Assert.Contains("1..64", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>验证配置本身不能绕过 v1 控制面绝对上限诱导巨量分配。</summary>
    [Fact]
    public async Task FrameRejectsConfiguredMaximumAboveProtocolAbsoluteLimit()
    {
        await using MemoryStream stream = new();

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await AutomationFrameCodec.ReadAsync(
                stream,
                AutomationProtocolConstants.AbsoluteMaxFrameBytes + 1));
    }

    /// <summary>验证 write preflight 与实际 frame 写入共享同一 payload 边界。</summary>
    [Fact]
    public void ValidateWritableRejectsOversizedSemanticResponseBeforeIo()
    {
        AutomationEnvelope envelope = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = AutomationProtocolConstants.CurrentVersion,
            MessageId = "large-response",
            Kind = AutomationMessageKind.Response,
            CorrelationId = "large-request",
            Method = "test.large",
            Payload = JsonSerializer.SerializeToElement(new { value = new string('x', 4096) }),
        };

        _ = Assert.Throws<AutomationFrameSizeException>(
            () => AutomationFrameCodec.ValidateWritable(envelope, 1024));
    }

    /// <summary>验证截断 payload 不会产生部分 envelope。</summary>
    [Fact]
    public async Task FrameRejectsTruncatedPayload()
    {
        AutomationEnvelope envelope = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = AutomationProtocolConstants.CurrentVersion,
            MessageId = "truncated",
            Kind = AutomationMessageKind.Request,
            Method = "test",
        };
        await using MemoryStream complete = new();
        await AutomationFrameCodec.WriteAsync(complete, envelope);
        byte[] truncated = complete.ToArray()[..^1];
        await using MemoryStream stream = new(truncated);

        _ = await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await AutomationFrameCodec.ReadAsync(stream));
    }

    /// <summary>验证缺少显式 DTO schema version 的 envelope 被拒绝。</summary>
    [Fact]
    public async Task FrameRejectsEnvelopeWithoutSchemaVersion()
    {
        byte[] payload = Encoding.UTF8.GetBytes(
                                 /*lang=json,strict*/
                                 "{\"protocol\":{\"major\":1,\"minor\":0},\"messageId\":\"missing-schema\",\"kind\":\"Request\"}");
        byte[] header = new byte[AutomationProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x31414550u);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(4, 2),
            AutomationProtocolConstants.FrameHeaderVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), checked((uint)payload.Length));
        await using MemoryStream stream = new([.. header, .. payload]);

        AutomationProtocolException exception = await Assert.ThrowsAsync<AutomationProtocolException>(
            async () => await AutomationFrameCodec.ReadAsync(stream));

        Assert.Contains("JSON", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>验证 v1 schema 未声明字段不会被静默忽略。</summary>
    [Fact]
    public async Task FrameRejectsUnmappedEnvelopeMembers()
    {
        byte[] payload = Encoding.UTF8.GetBytes(
                                 /*lang=json,strict*/
                                 "{\"schemaVersion\":1,\"protocol\":{\"major\":1,\"minor\":0},\"messageId\":\"unknown-member\",\"kind\":\"Request\",\"shadowState\":true}");
        byte[] header = new byte[AutomationProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x31414550u);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(4, 2),
            AutomationProtocolConstants.FrameHeaderVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), checked((uint)payload.Length));
        await using MemoryStream stream = new([.. header, .. payload]);

        AutomationProtocolException exception = await Assert.ThrowsAsync<AutomationProtocolException>(
            async () => await AutomationFrameCodec.ReadAsync(stream));

        Assert.Contains("JSON", exception.Message, StringComparison.Ordinal);
    }
}
