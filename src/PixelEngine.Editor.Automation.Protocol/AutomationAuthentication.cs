using System.Security.Cryptography;
using System.Text;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// challenge/HMAC 认证的共享 canonicalization 与加密原语。
/// </summary>
public static class AutomationAuthentication
{
    private static readonly byte[] PrincipalLabel = "pixelengine-editor-automation-principal-v1"u8.ToArray();

    /// <summary>
    /// 生成 256-bit 会话 secret。
    /// </summary>
    /// <returns>原始 secret 字节。</returns>
    public static byte[] GenerateSecret()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// 生成 256-bit base64 nonce。
    /// </summary>
    /// <returns>base64 nonce。</returns>
    public static string GenerateNonce()
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(nonce);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    /// <summary>
    /// 从实例 secret 派生不可逆 principal id，供审计和跨连接幂等命名空间使用。
    /// </summary>
    /// <param name="secret">共享 secret。</param>
    /// <returns>64 位小写 HMAC-SHA256。</returns>
    public static string ComputePrincipalId(ReadOnlySpan<byte> secret)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("Automation secret 不能为空。", nameof(secret));
        }

        byte[] principal = HMACSHA256.HashData(secret, PrincipalLabel);
        try
        {
            return Convert.ToHexStringLower(principal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(principal);
        }
    }

    /// <summary>
    /// 计算认证 proof。
    /// </summary>
    /// <param name="secret">共享 secret。</param>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="clientInstanceId">外部客户端实例 id。</param>
    /// <param name="clientName">客户端名称。</param>
    /// <param name="clientVersion">客户端版本。</param>
    /// <param name="clientNonce">客户端 nonce。</param>
    /// <param name="serverNonce">服务端 nonce。</param>
    /// <param name="version">协商版本。</param>
    /// <param name="requestedScopes">要绑定进 proof 的权限集合。</param>
    /// <returns>base64 HMAC-SHA256。</returns>
    public static string ComputeProof(
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientInstanceId,
        string clientName,
        string clientVersion,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes)
    {
        return ComputeProofCore(
            "client",
            secret,
            instanceId,
            clientInstanceId,
            clientName,
            clientVersion,
            clientNonce,
            serverNonce,
            version,
            requestedScopes,
            supportedScopes: [],
            maxFrameBytes: 0);
    }

    /// <summary>
    /// 计算角色域分离的 Server proof，使 Client 在发送自身 proof 前验证 Pipe 对端持有 secret。
    /// </summary>
    /// <param name="secret">共享 secret。</param>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="clientInstanceId">外部客户端实例 id。</param>
    /// <param name="clientName">客户端名称。</param>
    /// <param name="clientVersion">客户端版本。</param>
    /// <param name="clientNonce">客户端 nonce。</param>
    /// <param name="serverNonce">服务端 nonce。</param>
    /// <param name="version">协商版本。</param>
    /// <param name="requestedScopes">Client 请求的权限集合。</param>
    /// <param name="supportedScopes">Server 声明可授予的权限集合。</param>
    /// <param name="maxFrameBytes">Server 声明的 frame 上限。</param>
    /// <returns>base64 HMAC-SHA256。</returns>
    public static string ComputeServerProof(
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientInstanceId,
        string clientName,
        string clientVersion,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes,
        IEnumerable<string> supportedScopes,
        int maxFrameBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        return ComputeProofCore(
            "server",
            secret,
            instanceId,
            clientInstanceId,
            clientName,
            clientVersion,
            clientNonce,
            serverNonce,
            version,
            requestedScopes,
            supportedScopes,
            maxFrameBytes);
    }

    private static string ComputeProofCore(
        string role,
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientInstanceId,
        string clientName,
        string clientVersion,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes,
        IEnumerable<string> supportedScopes,
        int maxFrameBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientNonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverNonce);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(requestedScopes);
        ArgumentNullException.ThrowIfNull(supportedScopes);
        if (secret.IsEmpty)
        {
            throw new ArgumentException("Automation secret 不能为空。", nameof(secret));
        }

        string[] scopes =
        [
            .. requestedScopes
                .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] serverScopes =
        [
            .. supportedScopes
                .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        StringBuilder canonicalBuilder = new(256);
        AppendCanonical(canonicalBuilder, role);
        AppendCanonical(canonicalBuilder, instanceId);
        AppendCanonical(canonicalBuilder, clientInstanceId);
        AppendCanonical(canonicalBuilder, clientName);
        AppendCanonical(canonicalBuilder, clientVersion);
        AppendCanonical(canonicalBuilder, clientNonce);
        AppendCanonical(canonicalBuilder, serverNonce);
        AppendCanonical(canonicalBuilder, $"{version.Major}.{version.Minor}");
        AppendCanonical(canonicalBuilder, scopes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (string scope in scopes)
        {
            AppendCanonical(canonicalBuilder, scope);
        }

        AppendCanonical(canonicalBuilder, serverScopes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (string scope in serverScopes)
        {
            AppendCanonical(canonicalBuilder, scope);
        }

        AppendCanonical(canonicalBuilder, maxFrameBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        string canonical = canonicalBuilder.ToString();
        byte[] bytes = Encoding.UTF8.GetBytes(canonical);
        byte[] proof = HMACSHA256.HashData(secret, bytes);
        try
        {
            return Convert.ToBase64String(proof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            CryptographicOperations.ZeroMemory(proof);
        }
    }

    /// <summary>
    /// 用 constant-time 比较验证 proof。
    /// </summary>
    /// <param name="secret">共享 secret。</param>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="clientInstanceId">外部客户端实例 id。</param>
    /// <param name="clientName">客户端名称。</param>
    /// <param name="clientVersion">客户端版本。</param>
    /// <param name="clientNonce">客户端 nonce。</param>
    /// <param name="serverNonce">服务端 nonce。</param>
    /// <param name="version">协商版本。</param>
    /// <param name="requestedScopes">要绑定进 proof 的权限集合。</param>
    /// <param name="proof">待验证 base64 proof。</param>
    /// <returns>proof 是否有效。</returns>
    public static bool VerifyProof(
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientInstanceId,
        string clientName,
        string clientVersion,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes,
        string proof)
    {
        string expected = ComputeProof(
            secret,
            instanceId,
            clientInstanceId,
            clientName,
            clientVersion,
            clientNonce,
            serverNonce,
            version,
            requestedScopes);
        return VerifyEncodedProof(expected, proof);
    }

    /// <summary>验证角色域分离的 Server proof。</summary>
    /// <param name="secret">共享 secret。</param>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="clientInstanceId">外部客户端实例 id。</param>
    /// <param name="clientName">客户端名称。</param>
    /// <param name="clientVersion">客户端版本。</param>
    /// <param name="clientNonce">客户端 nonce。</param>
    /// <param name="serverNonce">服务端 nonce。</param>
    /// <param name="version">协商版本。</param>
    /// <param name="requestedScopes">Client 请求的权限集合。</param>
    /// <param name="supportedScopes">Server 声明可授予的权限集合。</param>
    /// <param name="maxFrameBytes">Server 声明的 frame 上限。</param>
    /// <param name="proof">待验证 base64 proof。</param>
    /// <returns>proof 是否有效。</returns>
    public static bool VerifyServerProof(
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientInstanceId,
        string clientName,
        string clientVersion,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes,
        IEnumerable<string> supportedScopes,
        int maxFrameBytes,
        string proof)
    {
        string expected = ComputeServerProof(
            secret,
            instanceId,
            clientInstanceId,
            clientName,
            clientVersion,
            clientNonce,
            serverNonce,
            version,
            requestedScopes,
            supportedScopes,
            maxFrameBytes);
        return VerifyEncodedProof(expected, proof);
    }

    private static bool VerifyEncodedProof(string expected, string proof)
    {
        if (string.IsNullOrWhiteSpace(proof))
        {
            return false;
        }

        byte[] expectedBytes = Convert.FromBase64String(expected);
        byte[] actualBytes;
        try
        {
            actualBytes = Convert.FromBase64String(proof);
        }
        catch (FormatException)
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }

    private static void AppendCanonical(StringBuilder builder, string value)
    {
        _ = builder.Append(value.Length).Append(':').Append(value).Append(';');
    }
}
