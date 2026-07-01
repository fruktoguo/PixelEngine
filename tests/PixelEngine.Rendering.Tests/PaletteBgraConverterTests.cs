using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class PaletteBgraConverterTests
{
    [Fact]
    public void ConvertMatchesScalarForFullVectorAndTail()
    {
        ushort[] materials = new ushort[37];
        uint[] palette = new uint[16];
        uint[] scalar = new uint[materials.Length];
        uint[] accelerated = new uint[materials.Length];

        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = 0xFF000000u | (uint)(i * 0x00010101u);
        }

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = (ushort)((i * 7) & 15);
        }

        PaletteBgraConverter.ConvertScalar(materials, palette, scalar);
        PaletteBgraConverter.Convert(materials, palette, accelerated);

        Assert.Equal(scalar, accelerated);
    }

    [Fact]
    public void Avx2ExperimentalMatchesScalar()
    {
        ushort[] materials = new ushort[37];
        uint[] palette = new uint[16];
        uint[] scalar = new uint[materials.Length];
        uint[] avx2 = new uint[materials.Length];

        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = 0xFF000000u | (uint)(i * 0x00030201u);
        }

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = (ushort)((i * 5) & 15);
        }

        PaletteBgraConverter.ConvertScalar(materials, palette, scalar);
        PaletteBgraConverter.ConvertAvx2Experimental(materials, palette, avx2);

        Assert.Equal(scalar, avx2);
    }

    [Fact]
    public void ConvertRejectsShortDestination()
    {
        ushort[] materials = [0, 1, 2];
        uint[] palette = [0, 1, 2];
        uint[] destination = new uint[2];

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PaletteBgraConverter.Convert(materials, palette, destination));

        Assert.Contains("目标 BGRA span", exception.Message, StringComparison.Ordinal);
    }
}
