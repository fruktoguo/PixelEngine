using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// 发布 JSON Schema 与 source-generated wire 名称一致性测试。
/// </summary>
public sealed class AutomationSchemaTests
{
    /// <summary>验证 schema 包含 envelope、descriptor 与保留 transport。</summary>
    [Fact]
    public void PublishedSchemaDefinesEnvelopeDescriptorAndReservedTransport()
    {
        string schemaPath = FindRepositoryFile("schema/editor-automation-protocol.v1.schema.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        JsonElement definitions = document.RootElement.GetProperty("$defs");

        Assert.True(definitions.TryGetProperty("envelope", out _));
        Assert.True(definitions.TryGetProperty("instanceDescriptor", out _));
        Assert.True(definitions.TryGetProperty("helloRequest", out _));
        Assert.True(definitions.TryGetProperty("helloChallenge", out _));
        Assert.True(definitions.TryGetProperty("authenticateRequest", out _));
        Assert.True(definitions.TryGetProperty("sessionInfo", out _));
        Assert.True(definitions.TryGetProperty("cancelRequest", out _));
        Assert.True(definitions.TryGetProperty("pingResponse", out _));
        Assert.Contains(
            "schemaVersion",
            definitions.GetProperty("envelope").GetProperty("required")
                .EnumerateArray().Select(static item => item.GetString()));
        JsonElement transports = definitions
            .GetProperty("endpoint")
            .GetProperty("properties")
            .GetProperty("kind")
            .GetProperty("enum");
        Assert.Contains("WindowsNamedPipe", transports.EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains("UnixDomainSocket", transports.EnumerateArray().Select(static item => item.GetString()));
    }

    /// <summary>验证 source-generated descriptor 使用 schema 的 camelCase 名称。</summary>
    [Fact]
    public void SourceGeneratedDescriptorMatchesPublishedPropertyNames()
    {
        AutomationInstanceDescriptor descriptor = new()
        {
            Schema = AutomationProtocolConstants.InstanceDescriptorSchema,
            InstanceId = "instance",
            ProcessId = 123,
            ProcessStartUtc = DateTimeOffset.UnixEpoch,
            PublishedAtUtc = DateTimeOffset.UnixEpoch,
            EditorVersion = "1.0",
            ProtocolVersions = [AutomationProtocolConstants.CurrentVersion],
            Endpoint = new AutomationEndpointDescriptor
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Kind = AutomationTransportKind.WindowsNamedPipe,
                Address = "pipe",
            },
            CredentialPath = "credential.token",
            CapabilityDigest = new string('0', 64),
            LivenessMode = "processIdentity",
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            descriptor,
            AutomationJsonContext.Default.AutomationInstanceDescriptor);

        Assert.Equal(AutomationProtocolConstants.InstanceDescriptorSchema, json.GetProperty("schema").GetString());
        Assert.Equal(
            AutomationProtocolConstants.WireSchemaVersion,
            json.GetProperty("endpoint").GetProperty("schemaVersion").GetInt32());
        Assert.Equal("WindowsNamedPipe", json.GetProperty("endpoint").GetProperty("kind").GetString());
        Assert.Equal(1, json.GetProperty("protocolVersions")[0].GetProperty("major").GetInt32());
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件 {relativePath}。");
    }
}
