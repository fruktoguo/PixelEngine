using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>公开 Client 对 capability/UI 矩阵完整性与双向闭包的 fail-closed 验证。</summary>
public sealed class AutomationCapabilityMatrixTests
{
    /// <summary>canonical、排序且双向引用一致的矩阵可被接受。</summary>
    [Fact]
    public void CanonicalBidirectionalMatrixIsAccepted()
    {
        AutomationCapabilityMatrixSnapshot matrix = CreateMatrix();

        EditorAutomationClient.ValidateCapabilityMatrix(matrix);
    }

    /// <summary>任一已发布 digest 与内容不一致时，Client 必须拒绝响应。</summary>
    [Fact]
    public void TamperedCanonicalDigestIsRejected()
    {
        AutomationCapabilityMatrixSnapshot matrix = CreateMatrix() with
        {
            MatrixDigest = new string('0', 64),
        };

        AutomationConnectionException exception = Assert.Throws<AutomationConnectionException>(
            () => EditorAutomationClient.ValidateCapabilityMatrix(matrix));

        Assert.Contains("SHA256", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>即使攻击者重算 digest，未严格排序的 capability 仍不能被接受。</summary>
    [Fact]
    public void RehashedButUnsortedCapabilitiesAreRejected()
    {
        AutomationCapabilityMatrixSnapshot original = CreateMatrix();
        AutomationCapabilityDescriptor second = original.Capabilities[0] with
        {
            Id = "automation.a",
            UiCommandIds = [],
        };
        AutomationCapabilityMatrixSnapshot matrix = WithCanonicalDigests(original with
        {
            Capabilities = [original.Capabilities[0], second],
        });

        AutomationConnectionException exception = Assert.Throws<AutomationConnectionException>(
            () => EditorAutomationClient.ValidateCapabilityMatrix(matrix));

        Assert.Contains("未严格排序或重复", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>即使三个 digest 均正确，单向伪造的 UI→capability 引用仍必须失败。</summary>
    [Fact]
    public void RehashedButAsymmetricReferenceIsRejected()
    {
        AutomationCapabilityMatrixSnapshot original = CreateMatrix();
        AutomationCapabilityMatrixSnapshot matrix = WithCanonicalDigests(original with
        {
            UiCommands =
            [
                original.UiCommands[0] with
                {
                    CapabilityIds = ["automation.missing"],
                },
            ],
        });

        AutomationConnectionException exception = Assert.Throws<AutomationConnectionException>(
            () => EditorAutomationClient.ValidateCapabilityMatrix(matrix));

        Assert.Contains("不存在或不对称", exception.Message, StringComparison.Ordinal);
    }

    private static AutomationCapabilityMatrixSnapshot CreateMatrix()
    {
        return WithCanonicalDigests(new AutomationCapabilityMatrixSnapshot
        {
            CapabilityDigest = string.Empty,
            UiCommandDigest = string.Empty,
            MatrixDigest = string.Empty,
            Capabilities =
            [
                new AutomationCapabilityDescriptor
                {
                    Id = "automation.test",
                    Domain = "test",
                    OperationKind = AutomationOperationKind.Read,
                    RequestSchema = "#/$defs/emptyRequest",
                    ResponseSchema = "#/$defs/emptyResponse",
                    RequiredScopes = [AutomationScopes.EditorRead],
                    SupportedModes = ["edit", "paused", "play"],
                    ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                    UiCommandIds = ["menu.test"],
                },
            ],
            UiCommands =
            [
                new AutomationUiCommandDescriptor
                {
                    Id = "menu.test",
                    SurfaceId = "editor.test",
                    HandlerId = "PixelEngine.Editor/Test.Handler",
                    CapabilityIds = ["automation.test"],
                },
            ],
        });
    }

    private static AutomationCapabilityMatrixSnapshot WithCanonicalDigests(
        AutomationCapabilityMatrixSnapshot matrix)
    {
        string capabilityDigest = ComputeDigest(
            matrix.Capabilities,
            AutomationJsonContext.Default.AutomationCapabilityDescriptorArray);
        string uiCommandDigest = ComputeDigest(
            matrix.UiCommands,
            AutomationJsonContext.Default.AutomationUiCommandDescriptorArray);
        byte[] matrixBytes = Encoding.UTF8.GetBytes($"v1\n{capabilityDigest}\n{uiCommandDigest}\n");
        string matrixDigest;
        try
        {
            matrixDigest = Convert.ToHexStringLower(SHA256.HashData(matrixBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(matrixBytes);
        }

        return matrix with
        {
            CapabilityDigest = capabilityDigest,
            UiCommandDigest = uiCommandDigest,
            MatrixDigest = matrixDigest,
        };
    }

    private static string ComputeDigest<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
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
}
