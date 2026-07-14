using System.Security.Cryptography;
using System.Text;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// challenge/HMAC 认证的共享 canonicalization 与加密原语。
/// </summary>
public static class AutomationAuthentication
{
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
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>
    /// 计算认证 proof。
    /// </summary>
    /// <param name="secret">共享 secret。</param>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="clientNonce">客户端 nonce。</param>
    /// <param name="serverNonce">服务端 nonce。</param>
    /// <param name="version">协商版本。</param>
    /// <param name="requestedScopes">要绑定进 proof 的权限集合。</param>
    /// <returns>base64 HMAC-SHA256。</returns>
    public static string ComputeProof(
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientNonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverNonce);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(requestedScopes);
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
        StringBuilder canonicalBuilder = new(256);
        AppendCanonical(canonicalBuilder, instanceId);
        AppendCanonical(canonicalBuilder, clientNonce);
        AppendCanonical(canonicalBuilder, serverNonce);
        AppendCanonical(canonicalBuilder, $"{version.Major}.{version.Minor}");
        AppendCanonical(canonicalBuilder, scopes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (string scope in scopes)
        {
            AppendCanonical(canonicalBuilder, scope);
        }

        string canonical = canonicalBuilder.ToString();
        byte[] bytes = Encoding.UTF8.GetBytes(canonical);
        byte[] proof = HMACSHA256.HashData(secret, bytes);
        CryptographicOperations.ZeroMemory(bytes);
        return Convert.ToBase64String(proof);
    }

    /// <summary>
    /// 用 constant-time 比较验证 proof。
    /// </summary>
    /// <param name="secret">共享 secret。</param>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="clientNonce">客户端 nonce。</param>
    /// <param name="serverNonce">服务端 nonce。</param>
    /// <param name="version">协商版本。</param>
    /// <param name="requestedScopes">要绑定进 proof 的权限集合。</param>
    /// <param name="proof">待验证 base64 proof。</param>
    /// <returns>proof 是否有效。</returns>
    public static bool VerifyProof(
        ReadOnlySpan<byte> secret,
        string instanceId,
        string clientNonce,
        string serverNonce,
        AutomationProtocolVersion version,
        IEnumerable<string> requestedScopes,
        string proof)
    {
        if (string.IsNullOrWhiteSpace(proof))
        {
            return false;
        }

        string expected = ComputeProof(
            secret,
            instanceId,
            clientNonce,
            serverNonce,
            version,
            requestedScopes);
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
