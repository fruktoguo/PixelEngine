using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// BGRA 颜色混合器测试：验证 SIMD/标量路径与 oracle 结果一致。
/// </summary>
public sealed class BgraColorMixerTests
{
    [Theory]
    [InlineData(1, 16)]
    [InlineData(3, 16)]
    [InlineData(4, 16)]
    [InlineData(17, 16)]
    [InlineData(67, 16)]
    [InlineData(67, 256)]
    public void ApplyColorNoiseMatchesScalarOracle(int length, int materialCount)
    {
        ushort[] materials = new ushort[length];
        byte[] noise = new byte[materialCount];
        uint[] scalar = new uint[length];
        uint[] accelerated = new uint[length];

        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = (byte)((i * 37) & 0xFF);
        }

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = (ushort)(((i * 11) + (i >> 2)) & (materialCount - 1));
            scalar[i] = 0xFF202040u + (uint)(i * 0x00030507u);
            accelerated[i] = scalar[i];
        }

        BgraColorMixer.ApplyColorNoiseScalar(materials, noise, scalar, worldX: -19, worldY: 23);
        BgraColorMixer.ApplyColorNoise(materials, noise, accelerated, worldX: -19, worldY: 23);

        Assert.Equal(scalar, accelerated);
    }
}
