using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// 实例发布、进程身份、credential 边界与 stale 清理测试。
/// </summary>
public sealed class AutomationDiscoveryTests
{
    /// <summary>验证 live Server 发布进程绑定 descriptor，并在退出时清理。</summary>
    [Fact]
    public async Task LiveServerPublishesDiscoverableProcessBoundDescriptorAndRemovesItOnDispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string descriptorPath;
        string credentialPath;
        await using (EditorAutomationServer server = CreateServer(temporary.Path))
        {
            await server.StartAsync();
            AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(temporary.Path);
            AutomationDiscoveredInstance instance = Assert.Single(snapshot.Instances);
            Assert.Empty(snapshot.Diagnostics);
            Assert.Equal(server.InstanceId, instance.Descriptor.InstanceId);
            Assert.Equal(Environment.ProcessId, instance.Descriptor.ProcessId);
            Assert.Equal(AutomationTransportKind.WindowsNamedPipe, instance.Descriptor.Endpoint.Kind);
            Assert.Equal(64, instance.Descriptor.CapabilityDigest.Length);
            Assert.True(File.Exists(instance.CredentialPath));
            descriptorPath = instance.DescriptorPath;
            credentialPath = instance.CredentialPath;
        }

        Assert.False(File.Exists(descriptorPath));
        Assert.False(File.Exists(credentialPath));
    }

    /// <summary>验证 descriptor、credential 与父目录 ACL 都被收紧到当前用户。</summary>
    [Fact]
    public async Task PublishedDiscoveryAndCredentialUseProtectedCurrentUserAcl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path);
        await server.StartAsync();
        AutomationDiscoveredInstance instance = Assert.Single(
            (await AutomationDiscovery.DiscoverAsync(temporary.Path)).Instances);

        VerifyCurrentUserOnlyFileAcl(instance.DescriptorPath);
        VerifyCurrentUserOnlyFileAcl(instance.CredentialPath);
        VerifyCurrentUserOnlyDirectoryAcl(Path.GetDirectoryName(instance.DescriptorPath)!);
        VerifyCurrentUserOnlyDirectoryAcl(Path.GetDirectoryName(instance.CredentialPath)!);

        string descriptorJson = await File.ReadAllTextAsync(instance.DescriptorPath);
        string credentialText = await File.ReadAllTextAsync(instance.CredentialPath);
        Assert.DoesNotContain(credentialText.Trim(), descriptorJson, StringComparison.Ordinal);
    }

    /// <summary>验证越界 credential 被拒绝，prune 不删除越界 token。</summary>
    [Fact]
    public async Task DiscoveryRejectsCredentialEscapeAndPruneDoesNotDeleteEscapedToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string instances = Path.Combine(temporary.Path, "instances");
        _ = Directory.CreateDirectory(instances);
        string escapedToken = Path.Combine(temporary.Path, "outside.token");
        await File.WriteAllTextAsync(escapedToken, Convert.ToBase64String(new byte[32]));
        AutomationInstanceDescriptor descriptor = CreateDescriptor(
            instanceId: "escaped",
            processId: Environment.ProcessId,
            processStartUtc: Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            credentialPath: escapedToken);
        string descriptorPath = Path.Combine(instances, "escaped.json");
        await File.WriteAllBytesAsync(
            descriptorPath,
            JsonSerializer.SerializeToUtf8Bytes(
                descriptor,
                AutomationJsonContext.Default.AutomationInstanceDescriptor));

        AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(temporary.Path);
        Assert.Empty(snapshot.Instances);
        Assert.Contains(snapshot.Diagnostics, static item =>
            item.Code == "invalid_descriptor" && item.Message.Contains("越出", StringComparison.Ordinal));

        Assert.Equal(1, await AutomationDiscovery.PruneStaleAsync(temporary.Path));
        Assert.False(File.Exists(descriptorPath));
        Assert.True(File.Exists(escapedToken));
    }

    /// <summary>验证 dead PID 被标记 stale，prune 同时安全删除 descriptor 与其进程绑定凭据。</summary>
    [Fact]
    public async Task DiscoveryClassifiesDeadPidAsStaleAndPrunesDescriptorAndBoundCredential()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string instances = Path.Combine(temporary.Path, "instances");
        string credentials = Path.Combine(temporary.Path, "credentials");
        _ = Directory.CreateDirectory(instances);
        _ = Directory.CreateDirectory(credentials);
        string token = Path.Combine(credentials, "stale.token");
        await File.WriteAllTextAsync(token, Convert.ToBase64String(new byte[32]));
        AutomationInstanceDescriptor descriptor = CreateDescriptor(
            instanceId: "stale",
            processId: int.MaxValue,
            processStartUtc: DateTimeOffset.UnixEpoch,
            credentialPath: token);
        string descriptorPath = Path.Combine(instances, "stale.json");
        await File.WriteAllBytesAsync(
            descriptorPath,
            JsonSerializer.SerializeToUtf8Bytes(
                descriptor,
                AutomationJsonContext.Default.AutomationInstanceDescriptor));

        AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(temporary.Path);
        Assert.Empty(snapshot.Instances);
        Assert.Contains(snapshot.Diagnostics, static item => item.Code == "stale_descriptor");
        Assert.Equal(1, await AutomationDiscovery.PruneStaleAsync(temporary.Path));
        Assert.False(File.Exists(descriptorPath));
        Assert.False(File.Exists(token));
    }

    /// <summary>验证 descriptor 不能借 stale prune 删除同根目录内其他实例的 token。</summary>
    [Fact]
    public async Task DiscoveryRejectsCredentialNotBoundToInstanceIdAndPruneKeepsToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string instances = Path.Combine(temporary.Path, "instances");
        string credentials = Path.Combine(temporary.Path, "credentials");
        _ = Directory.CreateDirectory(instances);
        _ = Directory.CreateDirectory(credentials);
        string otherToken = Path.Combine(credentials, "other.token");
        await File.WriteAllTextAsync(otherToken, Convert.ToBase64String(new byte[32]));
        AutomationInstanceDescriptor descriptor = CreateDescriptor(
            instanceId: "stale",
            processId: int.MaxValue,
            processStartUtc: DateTimeOffset.UnixEpoch,
            credentialPath: otherToken);
        string descriptorPath = Path.Combine(instances, "stale.json");
        await File.WriteAllBytesAsync(
            descriptorPath,
            JsonSerializer.SerializeToUtf8Bytes(
                descriptor,
                AutomationJsonContext.Default.AutomationInstanceDescriptor));

        AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(temporary.Path);
        Assert.Empty(snapshot.Instances);
        Assert.Contains(snapshot.Diagnostics, static item =>
            item.Code == "invalid_descriptor" && item.Message.Contains("credentialPath", StringComparison.Ordinal));

        Assert.Equal(1, await AutomationDiscovery.PruneStaleAsync(temporary.Path));
        Assert.False(File.Exists(descriptorPath));
        Assert.True(File.Exists(otherToken));
    }

    private static EditorAutomationServer CreateServer(string root)
    {
        return new EditorAutomationServer(new AutomationServerOptions
        {
            DiscoveryRoot = root,
            EditorVersion = "test",
            SupportedScopes = AutomationScopes.All,
        });
    }

    private static AutomationInstanceDescriptor CreateDescriptor(
        string instanceId,
        int processId,
        DateTimeOffset processStartUtc,
        string credentialPath)
    {
        return new AutomationInstanceDescriptor
        {
            Schema = AutomationProtocolConstants.InstanceDescriptorSchema,
            InstanceId = instanceId,
            ProcessId = processId,
            ProcessStartUtc = processStartUtc,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            EditorVersion = "test",
            ProtocolVersions = [AutomationProtocolConstants.CurrentVersion],
            Endpoint = new AutomationEndpointDescriptor
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Kind = AutomationTransportKind.WindowsNamedPipe,
                Address = $"test-{instanceId}",
            },
            CredentialPath = credentialPath,
            CapabilityDigest = new string('0', 64),
            LivenessMode = "processIdentity",
        };
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyCurrentUserOnlyFileAcl(string path)
    {
        FileSecurity security = new FileInfo(path).GetAccessControl();
        VerifyCurrentUserOnlyRules(security);
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyCurrentUserOnlyDirectoryAcl(string path)
    {
        DirectorySecurity security = new DirectoryInfo(path).GetAccessControl();
        VerifyCurrentUserOnlyRules(security);
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyCurrentUserOnlyRules(FileSystemSecurity security)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        SecurityIdentifier owner = identity.User
            ?? throw new InvalidOperationException("无法读取测试用户 SID。");
        Assert.True(security.AreAccessRulesProtected);
        FileSystemAccessRule[] rules =
        [
            .. security.GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>(),
        ];
        FileSystemAccessRule rule = Assert.Single(rules);
        Assert.Equal(owner, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.True(rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
    }
}
