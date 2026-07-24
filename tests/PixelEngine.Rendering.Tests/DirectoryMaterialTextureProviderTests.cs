using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 目录材质纹理加载与世界坐标平铺测试。
/// </summary>
public sealed class DirectoryMaterialTextureProviderTests
{
    /// <summary>
    /// 验证数值前缀绑定 TextureId，并且正负世界坐标按同一纹理周期采样。
    /// </summary>
    [Fact]
    public void ProviderLoadsNumericTextureIdAndTilesAcrossNegativeWorldCoordinates()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "textures", "00_sand.png");
        string temporary = Path.Combine(Path.GetTempPath(), $"pixelengine-material-textures-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(source, Path.Combine(temporary, "23_sand.png"));
            DirectoryMaterialTextureProvider provider = Assert.IsType<DirectoryMaterialTextureProvider>(
                DirectoryMaterialTextureProvider.TryLoad(temporary));
            MaterialDef material = new() { TextureId = 23 };

            Assert.True(provider.TrySample(in material, 0, 0, out uint origin));
            Assert.True(provider.TrySample(in material, 32, 32, out uint positivePeriod));
            Assert.True(provider.TrySample(in material, -32, -32, out uint negativePeriod));
            Assert.NotEqual(0u, origin);
            Assert.Equal(origin, positivePeriod);
            Assert.Equal(origin, negativePeriod);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    /// <summary>
    /// 验证同一 TextureId 的重复文件被明确拒绝，避免目录枚举顺序改变渲染结果。
    /// </summary>
    [Fact]
    public void ProviderRejectsDuplicateNumericTextureIds()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "textures", "00_sand.png");
        string temporary = Path.Combine(Path.GetTempPath(), $"pixelengine-material-textures-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(source, Path.Combine(temporary, "23_first.png"));
            File.Copy(source, Path.Combine(temporary, "23_second.png"));

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => DirectoryMaterialTextureProvider.TryLoad(temporary));
            Assert.Contains("id 23 重复", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PixelEngine.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }
}
