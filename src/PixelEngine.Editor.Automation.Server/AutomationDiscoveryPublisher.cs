using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

internal sealed class AutomationDiscoveryPublisher
{
    private readonly string _instancesDirectory;
    private readonly string _credentialsDirectory;

    public AutomationDiscoveryPublisher(string discoveryRoot)
    {
        string root = Path.GetFullPath(discoveryRoot);
        _instancesDirectory = Path.Combine(root, "instances");
        _credentialsDirectory = Path.Combine(root, "credentials");
        AutomationSecureStorage.EnsurePrivateDirectory(root);
        AutomationSecureStorage.EnsurePrivateDirectory(_instancesDirectory);
        AutomationSecureStorage.EnsurePrivateDirectory(_credentialsDirectory);
    }

    public string GetDescriptorPath(string instanceId)
    {
        return Path.Combine(_instancesDirectory, $"{instanceId}.json");
    }

    public string GetCredentialPath(string instanceId)
    {
        return Path.Combine(_credentialsDirectory, $"{instanceId}.token");
    }

    public async ValueTask WriteCredentialAsync(
        string instanceId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken)
    {
        string path = GetCredentialPath(instanceId);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        byte[] encoded = Encoding.ASCII.GetBytes(Convert.ToBase64String(secret.Span));
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, encoded, cancellationToken).ConfigureAwait(false);
            AutomationSecureStorage.EnsurePrivateFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            AutomationSecureStorage.EnsurePrivateFile(path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
            TryDelete(temporaryPath);
        }
    }

    public async ValueTask PublishAsync(
        AutomationInstanceDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        string path = GetDescriptorPath(descriptor.InstanceId);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            descriptor,
            AutomationJsonContext.Default.AutomationInstanceDescriptor);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            AutomationSecureStorage.EnsurePrivateFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            AutomationSecureStorage.EnsurePrivateFile(path);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public void Remove(string instanceId)
    {
        TryDelete(GetDescriptorPath(instanceId));
        TryDelete(GetCredentialPath(instanceId));
    }

    public static string ComputeSystemCapabilityDigest()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            $"{AutomationProtocolConstants.HelloMethod}\n{AutomationProtocolConstants.AuthenticateMethod}\n{AutomationProtocolConstants.CancelMethod}\n{AutomationProtocolConstants.PingMethod}\n{AutomationProtocolConstants.DescribeMethod}");
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(source));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(source);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
