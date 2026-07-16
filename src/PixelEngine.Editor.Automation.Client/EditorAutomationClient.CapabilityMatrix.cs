using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

public sealed partial class EditorAutomationClient
{
    /// <summary>
    /// 读取并独立验证完整 capability/UI 双向矩阵，包括三个 canonical SHA256、排序、唯一性与反向引用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已验证矩阵及同一安全点 revision。</returns>
    public async ValueTask<AutomationTypedInvocationResult<AutomationCapabilityMatrixSnapshot>>
        GetCapabilityMatrixAsync(CancellationToken cancellationToken = default)
    {
        AutomationTypedInvocationResult<AutomationCapabilityMatrixSnapshot> invocation =
            await InvokeDetailedAsync(
                AutomationProtocolConstants.CapabilityMatrixGetMethod,
                AutomationJsonContext.Default.AutomationCapabilityMatrixSnapshot,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        ValidateCapabilityMatrix(invocation.Response);
        ValidatePublishedCapabilityDigest(
            invocation.Response.CapabilityDigest,
            Instance.Descriptor.CapabilityDigest,
            "discovery descriptor");
        return invocation;
    }

    internal static void ValidateCapabilityMatrix(AutomationCapabilityMatrixSnapshot matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            matrix.Capabilities is null ||
            matrix.UiCommands is null)
        {
            throw new AutomationConnectionException("Capability matrix schema 或数组无效。");
        }

        string capabilityDigest = ComputeDigest(
            matrix.Capabilities,
            AutomationJsonContext.Default.AutomationCapabilityDescriptorArray);
        string uiCommandDigest = ComputeDigest(
            matrix.UiCommands,
            AutomationJsonContext.Default.AutomationUiCommandDescriptorArray);
        string matrixDigest = ComputeMatrixDigest(capabilityDigest, uiCommandDigest);
        if (!string.Equals(matrix.CapabilityDigest, capabilityDigest, StringComparison.Ordinal) ||
            !string.Equals(matrix.UiCommandDigest, uiCommandDigest, StringComparison.Ordinal) ||
            !string.Equals(matrix.MatrixDigest, matrixDigest, StringComparison.Ordinal))
        {
            throw new AutomationConnectionException("Capability matrix canonical SHA256 不匹配。");
        }

        Dictionary<string, AutomationCapabilityDescriptor> capabilities = new(StringComparer.Ordinal);
        string? previousCapabilityId = null;
        for (int i = 0; i < matrix.Capabilities.Length; i++)
        {
            AutomationCapabilityDescriptor capability = matrix.Capabilities[i] ??
                throw new AutomationConnectionException("Capability matrix 包含 null capability。");
            if ((previousCapabilityId is not null &&
                 string.CompareOrdinal(previousCapabilityId, capability.Id) >= 0) ||
                !capabilities.TryAdd(capability.Id, capability))
            {
                throw new AutomationConnectionException("Capability matrix 的 capability IDs 未严格排序或重复。");
            }

            previousCapabilityId = capability.Id;
        }

        Dictionary<string, AutomationUiCommandDescriptor> uiCommands = new(StringComparer.Ordinal);
        string? previousUiCommandId = null;
        for (int i = 0; i < matrix.UiCommands.Length; i++)
        {
            AutomationUiCommandDescriptor command = matrix.UiCommands[i] ??
                throw new AutomationConnectionException("Capability matrix 包含 null UI command。");
            if ((previousUiCommandId is not null &&
                 string.CompareOrdinal(previousUiCommandId, command.Id) >= 0) ||
                !uiCommands.TryAdd(command.Id, command) ||
                command.CapabilityIds is not { Length: >= 1 } ||
                !command.CapabilityIds.SequenceEqual(command.CapabilityIds.Order(StringComparer.Ordinal)))
            {
                throw new AutomationConnectionException(
                    "Capability matrix 的 UI command IDs/capability IDs 未严格规范化。");
            }

            for (int capabilityIndex = 0; capabilityIndex < command.CapabilityIds.Length; capabilityIndex++)
            {
                string capabilityId = command.CapabilityIds[capabilityIndex];
                if (!capabilities.TryGetValue(capabilityId, out AutomationCapabilityDescriptor? capability) ||
                    !capability.UiCommandIds.Contains(command.Id, StringComparer.Ordinal))
                {
                    throw new AutomationConnectionException(
                        $"UI command '{command.Id}' 反向引用了不存在或不对称的 capability '{capabilityId}'。");
                }
            }

            previousUiCommandId = command.Id;
        }

        foreach (AutomationCapabilityDescriptor capability in matrix.Capabilities)
        {
            for (int commandIndex = 0; commandIndex < capability.UiCommandIds.Length; commandIndex++)
            {
                string commandId = capability.UiCommandIds[commandIndex];
                if (!uiCommands.TryGetValue(commandId, out AutomationUiCommandDescriptor? command) ||
                    !command.CapabilityIds.Contains(capability.Id, StringComparer.Ordinal))
                {
                    throw new AutomationConnectionException(
                        $"Capability '{capability.Id}' 引用了不存在或不对称的 UI command '{commandId}'。");
                }
            }
        }
    }

    private static string ComputeDigest<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static string ComputeMatrixDigest(string capabilityDigest, string uiCommandDigest)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes($"v1\n{capabilityDigest}\n{uiCommandDigest}\n");
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }
}
