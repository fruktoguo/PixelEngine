using System.Security.Cryptography;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

public sealed partial class EditorAutomationClient
{
    /// <summary>从当前 session 的权威 artifact catalog 读取稳定引用。</summary>
    /// <param name="artifactId">session 内 32 位小写十六进制 artifact ID。</param>
    /// <param name="options">分页请求选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Server 当前发布的 canonical artifact reference。</returns>
    public async ValueTask<AutomationArtifactReference> GetArtifactAsync(
        string artifactId,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string validatedId = ValidateStableId(artifactId, nameof(artifactId));
        AutomationArtifactReference? found = null;
        await foreach (AutomationArtifactReference candidate in EnumeratePagesAsync(
                           AutomationProtocolConstants.ArtifactListMethod,
                           new AutomationPageRequest
                           {
                               PageSize = 100,
                               Filter = new AutomationQueryFilter
                               {
                                   Clauses =
                                   [
                                       new AutomationFilterClause
                                       {
                                           Field = "artifactId",
                                           Operator = AutomationFilterOperator.Equals,
                                           Value = System.Text.Json.JsonSerializer.SerializeToElement(
                                               validatedId,
                                               AutomationJsonContext.Default.String),
                                       },
                                   ],
                               },
                           },
                           AutomationJsonContext.Default.AutomationArtifactListResponse,
                           static response => response.Items,
                           static response => response.Page,
                           options,
                           cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(candidate.ArtifactId, validatedId, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                throw new AutomationConnectionException(
                    $"Artifact catalog 对 ID '{validatedId}' 返回重复引用。");
            }

            found = candidate;
        }

        return found ?? throw new KeyNotFoundException(
            $"Artifact '{validatedId}' 不存在于当前 session 或已被淘汰。");
    }

    /// <summary>校验 artifact 引用路径、Server 索引、文件长度与流式 SHA256。</summary>
    /// <param name="artifact">Server 返回的 artifact reference。</param>
    /// <param name="verifyWithServer">是否先调用 artifact.verify 重验权威索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>远端与本地完整性结果。</returns>
    public async ValueTask<AutomationArtifactVerification> VerifyArtifactAsync(
        AutomationArtifactReference artifact,
        bool verifyWithServer = true,
        CancellationToken cancellationToken = default)
    {
        ValidateArtifactReference(artifact);
        bool? serverVerified = null;
        if (verifyWithServer)
        {
            AutomationTypedInvocationResult<AutomationArtifactVerifyResult> remote =
                await VerifyArtifactRemoteAsync(
                    artifact.ArtifactId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!string.Equals(remote.Response.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal))
            {
                throw new AutomationConnectionException("artifact.verify 返回了不匹配的 artifact ID。");
            }

            serverVerified = remote.Response.Verified;
            if (serverVerified == true)
            {
                AutomationArtifactReference canonical;
                try
                {
                    canonical = await GetArtifactAsync(
                        artifact.ArtifactId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (KeyNotFoundException)
                {
                    return ArtifactVerificationResult(
                        artifact,
                        serverVerified: false,
                        localVerified: false,
                        actualByteLength: null,
                        actualSha256: null,
                        "artifact 在 Server verify 后已被删除或淘汰。");
                }

                if (!ArtifactIdentityEquals(artifact, canonical))
                {
                    return ArtifactVerificationResult(
                        artifact,
                        serverVerified: false,
                        localVerified: false,
                        actualByteLength: null,
                        actualSha256: null,
                        "artifact reference 与 Server canonical metadata 不一致。");
                }
            }
        }

        string fullPath = Path.GetFullPath(artifact.Path);
        if (!File.Exists(fullPath))
        {
            return ArtifactVerificationResult(
                artifact,
                serverVerified,
                localVerified: false,
                actualByteLength: null,
                actualSha256: null,
                "artifact 文件不存在。");
        }

        EnsureArtifactPathIsSafe(fullPath, artifact.RelativePath);
        await using FileStream file = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long actualLength = file.Length;
        if (actualLength != artifact.ByteLength)
        {
            return ArtifactVerificationResult(
                artifact,
                serverVerified,
                localVerified: false,
                actualLength,
                actualSha256: null,
                "artifact 文件长度不匹配。");
        }

        byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        string actualSha256;
        try
        {
            actualSha256 = Convert.ToHexStringLower(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }

        bool localVerified = string.Equals(
            actualSha256,
            artifact.Sha256,
            StringComparison.Ordinal);
        string diagnostic = !localVerified
            ? "artifact SHA256 不匹配。"
            : serverVerified == false
                ? "本地文件匹配，但 Server 索引重验失败。"
                : "artifact 长度与 SHA256 已验证。";
        return ArtifactVerificationResult(
            artifact,
            serverVerified,
            localVerified,
            actualLength,
            actualSha256,
            diagnostic);
    }

    private static AutomationArtifactVerification ArtifactVerificationResult(
        AutomationArtifactReference artifact,
        bool? serverVerified,
        bool localVerified,
        long? actualByteLength,
        string? actualSha256,
        string diagnostic)
    {
        return new AutomationArtifactVerification
        {
            Artifact = artifact,
            ServerVerified = serverVerified,
            LocalVerified = localVerified,
            Verified = localVerified && serverVerified is not false,
            ActualByteLength = actualByteLength,
            ActualSha256 = actualSha256,
            Diagnostic = diagnostic,
        };
    }

    private static void ValidateArtifactReference(AutomationArtifactReference artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            artifact.ByteLength < 0 ||
            !Path.IsPathFullyQualified(artifact.Path) ||
            string.IsNullOrWhiteSpace(artifact.RelativePath) ||
            Path.IsPathFullyQualified(artifact.RelativePath) ||
            artifact.Sha256.Length != 64 ||
            artifact.Sha256.Any(static character =>
                !char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f')))
        {
            throw new AutomationConnectionException("Artifact reference identity/path/hash 无效。");
        }

        _ = ValidateStableId(artifact.ArtifactId, nameof(artifact));
    }

    private static void EnsureArtifactPathIsSafe(string fullPath, string relativePath)
    {
        if (OperatingSystem.IsWindows() && fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new AutomationConnectionException("Artifact path 不得位于远程或 device root。");
        }

        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
        {
            throw new AutomationConnectionException("Artifact relativePath 包含无效 segment。");
        }

        string root = fullPath;
        for (int i = 0; i < segments.Length; i++)
        {
            root = Path.GetDirectoryName(root) ??
                throw new AutomationConnectionException("Artifact relativePath 越出文件系统 root。");
        }

        string reconstructed = Path.GetFullPath(Path.Combine([root, .. segments]));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(reconstructed, fullPath, comparison))
        {
            throw new AutomationConnectionException("Artifact path 与 relativePath 不一致。");
        }

        string? ancestor = root;
        while (ancestor is not null)
        {
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
            {
                throw new AutomationConnectionException("Artifact path 的 ancestor 是 reparse point。");
            }

            ancestor = Path.GetDirectoryName(ancestor);
        }

        string current = root;
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new AutomationConnectionException("Artifact path 包含 reparse point。");
            }
        }
    }

    private static bool ArtifactIdentityEquals(
        AutomationArtifactReference supplied,
        AutomationArtifactReference canonical)
    {
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return supplied.SchemaVersion == canonical.SchemaVersion &&
            string.Equals(supplied.ArtifactId, canonical.ArtifactId, StringComparison.Ordinal) &&
            string.Equals(
                Path.GetFullPath(supplied.Path),
                Path.GetFullPath(canonical.Path),
                pathComparison) &&
            string.Equals(supplied.RelativePath, canonical.RelativePath, pathComparison) &&
            string.Equals(supplied.MediaType, canonical.MediaType, StringComparison.OrdinalIgnoreCase) &&
            supplied.ByteLength == canonical.ByteLength &&
            string.Equals(supplied.Sha256, canonical.Sha256, StringComparison.Ordinal) &&
            supplied.CreatedAtUtc == canonical.CreatedAtUtc &&
            supplied.Width == canonical.Width &&
            supplied.Height == canonical.Height &&
            string.Equals(supplied.Encoding, canonical.Encoding, StringComparison.OrdinalIgnoreCase);
    }
}
