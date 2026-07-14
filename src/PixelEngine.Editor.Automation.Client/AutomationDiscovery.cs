using System.Diagnostics;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>
/// 通过安全校验的 Editor automation 实例。
/// </summary>
public sealed record AutomationDiscoveredInstance
{
    /// <summary>实例 descriptor。</summary>
    public required AutomationInstanceDescriptor Descriptor { get; init; }

    /// <summary>descriptor canonical path。</summary>
    public required string DescriptorPath { get; init; }

    /// <summary>已确认位于 discovery credential root 内的 token path。</summary>
    public required string CredentialPath { get; init; }
}

/// <summary>
/// discovery 中被忽略条目的结构化诊断。
/// </summary>
public sealed record AutomationDiscoveryDiagnostic
{
    /// <summary>descriptor path。</summary>
    public required string Path { get; init; }

    /// <summary>稳定诊断码。</summary>
    public required string Code { get; init; }

    /// <summary>诊断文本。</summary>
    public required string Message { get; init; }

    /// <summary>仅在安全解析后记录的同根 credential path。</summary>
    public string? CredentialPath { get; init; }
}

/// <summary>
/// 一次无轮询 discovery snapshot。
/// </summary>
public sealed record AutomationDiscoverySnapshot
{
    /// <summary>通过 schema、路径和进程身份校验的 live instances。</summary>
    public required AutomationDiscoveredInstance[] Instances { get; init; }

    /// <summary>invalid/stale descriptors 的诊断。</summary>
    public required AutomationDiscoveryDiagnostic[] Diagnostics { get; init; }
}

/// <summary>
/// Editor automation 实例发现器。
/// </summary>
public static class AutomationDiscovery
{
    /// <summary>
    /// 枚举 discovery root 的一次性 snapshot；不启动 watcher 或 timer。
    /// </summary>
    /// <param name="discoveryRoot">实例 discovery 根目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>live instances 与 ignored diagnostics。</returns>
    public static async ValueTask<AutomationDiscoverySnapshot> DiscoverAsync(
        string discoveryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryRoot);
        string root = Path.GetFullPath(discoveryRoot);
        string instancesRoot = Path.Combine(root, "instances");
        string credentialsRoot = Path.Combine(root, "credentials");
        if (!Directory.Exists(instancesRoot))
        {
            return new AutomationDiscoverySnapshot { Instances = [], Diagnostics = [] };
        }

        if (TryContainsReparsePoint(instancesRoot, root))
        {
            return new AutomationDiscoverySnapshot
            {
                Instances = [],
                Diagnostics =
                [
                    new AutomationDiscoveryDiagnostic
                    {
                        Path = Path.GetFullPath(instancesRoot),
                        Code = "invalid_discovery_root",
                        Message = "discovery instances root 包含 reparse point。",
                    },
                ],
            };
        }

        List<AutomationDiscoveredInstance> instances = [];
        List<AutomationDiscoveryDiagnostic> diagnostics = [];
        foreach (string path in Directory.EnumerateFiles(instancesRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (TryContainsReparsePoint(path, instancesRoot))
                {
                    AddDiagnostic(diagnostics, path, "invalid_descriptor", "descriptor path 包含 reparse point。");
                    continue;
                }

                long descriptorLength = new FileInfo(path).Length;
                if (descriptorLength is <= 0 or > AutomationProtocolConstants.MaxDiscoveryDescriptorBytes)
                {
                    AddDiagnostic(
                        diagnostics,
                        path,
                        "invalid_descriptor",
                        $"descriptor 大小必须位于 1..{AutomationProtocolConstants.MaxDiscoveryDescriptorBytes} 字节。");
                    continue;
                }

                byte[] json = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (json.Length is <= 0 or > AutomationProtocolConstants.MaxDiscoveryDescriptorBytes)
                {
                    AddDiagnostic(
                        diagnostics,
                        path,
                        "invalid_descriptor",
                        $"descriptor 大小必须位于 1..{AutomationProtocolConstants.MaxDiscoveryDescriptorBytes} 字节。");
                    continue;
                }

                AutomationInstanceDescriptor? descriptor = JsonSerializer.Deserialize(
                    json,
                    AutomationJsonContext.Default.AutomationInstanceDescriptor);
                if (descriptor is null)
                {
                    AddDiagnostic(diagnostics, path, "invalid_descriptor", "descriptor JSON 为空。");
                    continue;
                }

                string? validation = ValidateDescriptor(descriptor, path, credentialsRoot);
                if (validation is not null)
                {
                    AddDiagnostic(diagnostics, path, "invalid_descriptor", validation);
                    continue;
                }

                if (!IsProcessIdentityLive(descriptor))
                {
                    AddDiagnostic(
                        diagnostics,
                        path,
                        "stale_descriptor",
                        "PID 或 process start identity 不再匹配 live Editor。",
                        Path.GetFullPath(descriptor.CredentialPath));
                    continue;
                }

                instances.Add(new AutomationDiscoveredInstance
                {
                    Descriptor = descriptor,
                    DescriptorPath = Path.GetFullPath(path),
                    CredentialPath = Path.GetFullPath(descriptor.CredentialPath),
                });
            }
            catch (JsonException exception)
            {
                AddDiagnostic(diagnostics, path, "invalid_json", exception.Message);
            }
            catch (IOException exception)
            {
                AddDiagnostic(diagnostics, path, "descriptor_io_error", exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                AddDiagnostic(diagnostics, path, "descriptor_access_denied", exception.Message);
            }
            catch (ArgumentException exception)
            {
                AddDiagnostic(diagnostics, path, "invalid_descriptor", exception.Message);
            }
            catch (NotSupportedException exception)
            {
                AddDiagnostic(diagnostics, path, "invalid_descriptor", exception.Message);
            }
        }

        return new AutomationDiscoverySnapshot
        {
            Instances = [.. instances.OrderBy(static instance => instance.Descriptor.InstanceId, StringComparer.Ordinal)],
            Diagnostics = [.. diagnostics.OrderBy(static diagnostic => diagnostic.Path, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// 仅删除当前 discovery root 内、已经通过读取且被判定 stale/invalid 的 descriptor 文件；不跟随链接、不删除 credential 之外路径。
    /// </summary>
    /// <param name="discoveryRoot">实例 discovery 根目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除的 descriptor 数量。</returns>
    public static async ValueTask<int> PruneStaleAsync(
        string discoveryRoot,
        CancellationToken cancellationToken = default)
    {
        AutomationDiscoverySnapshot snapshot = await DiscoverAsync(discoveryRoot, cancellationToken).ConfigureAwait(false);
        string instancesRoot = Path.GetFullPath(Path.Combine(discoveryRoot, "instances"));
        string credentialsRoot = Path.GetFullPath(Path.Combine(discoveryRoot, "credentials"));
        int removed = 0;
        foreach (AutomationDiscoveryDiagnostic diagnostic in snapshot.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Code is not ("stale_descriptor" or "invalid_descriptor" or "invalid_json"))
            {
                continue;
            }

            string path = Path.GetFullPath(diagnostic.Path);
            if (!IsWithinRoot(path, instancesRoot) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryContainsReparsePoint(path, instancesRoot) && TryDelete(path))
            {
                removed++;
            }

            if (diagnostic.Code == "stale_descriptor" && diagnostic.CredentialPath is not null)
            {
                string credentialPath = Path.GetFullPath(diagnostic.CredentialPath);
                if (IsWithinRoot(credentialPath, credentialsRoot) &&
                    string.Equals(Path.GetExtension(credentialPath), ".token", StringComparison.OrdinalIgnoreCase) &&
                    !TryContainsReparsePoint(credentialPath, credentialsRoot))
                {
                    _ = TryDelete(credentialPath);
                }
            }
        }

        return removed;
    }

    private static string? ValidateDescriptor(
        AutomationInstanceDescriptor descriptor,
        string descriptorPath,
        string credentialsRoot)
    {
        if (!string.Equals(descriptor.Schema, AutomationProtocolConstants.InstanceDescriptorSchema, StringComparison.Ordinal))
        {
            return $"不支持 descriptor schema '{descriptor.Schema}'。";
        }

        if (string.IsNullOrWhiteSpace(descriptor.InstanceId) || descriptor.ProcessId <= 0 ||
            descriptor.ProtocolVersions is null || descriptor.ProtocolVersions.Length == 0 ||
            descriptor.ProtocolVersions.Any(static version => version is null || version.Major <= 0 || version.Minor < 0) ||
            descriptor.Endpoint is null ||
            descriptor.Endpoint.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            string.IsNullOrWhiteSpace(descriptor.Endpoint.Address) ||
            string.IsNullOrWhiteSpace(descriptor.CredentialPath) || string.IsNullOrWhiteSpace(descriptor.EditorVersion) ||
            string.IsNullOrWhiteSpace(descriptor.CapabilityDigest))
        {
            return "descriptor identity/version/endpoint 不完整。";
        }

        if (!string.Equals(
                Path.GetFileNameWithoutExtension(descriptorPath),
                descriptor.InstanceId,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return "descriptor 文件名与 instanceId 不匹配。";
        }

        if (descriptor.CapabilityDigest.Length != 64 ||
            descriptor.CapabilityDigest.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            return "capabilityDigest 不是 64 位小写 SHA256。";
        }

        if (!string.Equals(descriptor.LivenessMode, "processIdentity", StringComparison.Ordinal))
        {
            return $"不支持 liveness mode '{descriptor.LivenessMode}'。";
        }

        if (descriptor.Project is not null &&
            (descriptor.Project.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
             string.IsNullOrWhiteSpace(descriptor.Project.ProjectId) ||
             string.IsNullOrWhiteSpace(descriptor.Project.Name) ||
             string.IsNullOrWhiteSpace(descriptor.Project.RootPath)))
        {
            return "descriptor project summary 不完整或 schema 不受支持。";
        }

        string credentialPath = Path.GetFullPath(descriptor.CredentialPath);
        return !IsWithinRoot(credentialPath, Path.GetFullPath(credentialsRoot)) ||
            !string.Equals(
                Path.GetFileName(credentialPath),
                $"{descriptor.InstanceId}.token",
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(credentialPath), ".token", StringComparison.OrdinalIgnoreCase)
            ? "credentialPath 越出 discovery credentials root。"
            : !File.Exists(credentialPath)
            ? "credential 文件不存在。"
            : TryContainsReparsePoint(credentialPath, Path.GetFullPath(credentialsRoot))
            ? "credentialPath 包含 reparse point。"
            : descriptor.Endpoint.Kind switch
            {
                AutomationTransportKind.WindowsNamedPipe when OperatingSystem.IsWindows() => null,
                AutomationTransportKind.WindowsNamedPipe => "当前平台不能连接 Windows Named Pipe。",
                AutomationTransportKind.UnixDomainSocket => "v1 尚未发布 Unix Domain Socket transport。",
                _ => "未知 transport kind。",
            };
    }

    private static bool IsProcessIdentityLive(AutomationInstanceDescriptor descriptor)
    {
        try
        {
            using Process process = Process.GetProcessById(descriptor.ProcessId);
            DateTimeOffset actual = process.StartTime.ToUniversalTime();
            return Math.Abs((actual - descriptor.ProcessStartUtc).TotalMilliseconds) < 1000;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsWithinRoot(string path, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static bool ContainsReparsePoint(string path, string root)
    {
        string current = Path.GetFullPath(path);
        string normalizedRoot = Path.GetFullPath(root);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (IsWithinRoot(current, normalizedRoot) || string.Equals(current, normalizedRoot, comparison))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (string.Equals(current, normalizedRoot, comparison))
            {
                break;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("无法遍历 credential path parent。");
        }

        return false;
    }

    private static bool TryContainsReparsePoint(string path, string root)
    {
        try
        {
            return ContainsReparsePoint(path, root);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return !File.Exists(path);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static void AddDiagnostic(
        ICollection<AutomationDiscoveryDiagnostic> diagnostics,
        string path,
        string code,
        string message,
        string? credentialPath = null)
    {
        diagnostics.Add(new AutomationDiscoveryDiagnostic
        {
            Path = Path.GetFullPath(path),
            Code = code,
            Message = message,
            CredentialPath = credentialPath,
        });
    }
}
