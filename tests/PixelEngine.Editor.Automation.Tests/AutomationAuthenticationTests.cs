using System.Security.Cryptography;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// HMAC challenge 绑定与 malformed proof 测试。
/// </summary>
public sealed class AutomationAuthenticationTests
{
    /// <summary>
    /// 验证 proof 同时绑定 secret、instance、双方 nonce、协议版本与请求 scope。
    /// </summary>
    [Fact]
    public void ProofIsBoundToInstanceNoncesAndVersion()
    {
        byte[] secret = AutomationAuthentication.GenerateSecret();
        try
        {
            string proof = AutomationAuthentication.ComputeProof(
                secret,
                "instance-a",
                "client-nonce",
                "server-nonce",
                AutomationProtocolConstants.CurrentVersion,
                [AutomationScopes.EditorRead]);

            Assert.True(AutomationAuthentication.VerifyProof(
                secret,
                "instance-a",
                "client-nonce",
                "server-nonce",
                AutomationProtocolConstants.CurrentVersion,
                [AutomationScopes.EditorRead],
                proof));
            Assert.False(AutomationAuthentication.VerifyProof(
                secret,
                "instance-b",
                "client-nonce",
                "server-nonce",
                AutomationProtocolConstants.CurrentVersion,
                [AutomationScopes.EditorRead],
                proof));
            Assert.False(AutomationAuthentication.VerifyProof(
                secret,
                "instance-a",
                "client-nonce",
                "server-nonce",
                new AutomationProtocolVersion(1, 1),
                [AutomationScopes.EditorRead],
                proof));
            Assert.False(AutomationAuthentication.VerifyProof(
                secret,
                "instance-a",
                "client-nonce",
                "server-nonce",
                AutomationProtocolConstants.CurrentVersion,
                [AutomationScopes.EditorControl],
                proof));
            Assert.False(AutomationAuthentication.VerifyProof(
                secret,
                "instance-a",
                "client-nonce",
                "server-nonce",
                AutomationProtocolConstants.CurrentVersion,
                [AutomationScopes.EditorRead],
                "not-base64"));
            Assert.False(AutomationAuthentication.VerifyProof(
                secret,
                "instance-a",
                "client-nonce",
                "server-nonce",
                AutomationProtocolConstants.CurrentVersion,
                [AutomationScopes.EditorRead],
                string.Empty));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}
