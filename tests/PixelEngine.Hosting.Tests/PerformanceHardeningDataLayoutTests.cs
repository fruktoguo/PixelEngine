using System.Reflection;
using PixelEngine.Core;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/16 SoA 与内存预算性能纪律测试。
/// </summary>
public sealed class PerformanceHardeningDataLayoutTests
{
    /// <summary>
    /// 验证权威 cell 热数据以 Material/Flags/Lifetime 三个独立连续数组存储。
    /// </summary>
    [Fact]
    public void ChunkStoresAuthorityCellStateAsSeparateSoAArrays()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));

        Assert.Equal(EngineConstants.ChunkArea, chunk.Material.Length);
        Assert.Equal(EngineConstants.ChunkArea, chunk.Flags.Length);
        Assert.Equal(EngineConstants.ChunkArea, chunk.Lifetime.Length);
        Assert.NotSame(chunk.Material, chunk.Flags);
        Assert.NotSame(chunk.Material, chunk.Lifetime);
        Assert.NotSame(chunk.Flags, chunk.Lifetime);

        chunk.Material[17] = 42;
        chunk.Flags[17] = 0b_0101_0000;
        chunk.Lifetime[17] = 9;

        Assert.Equal(42, chunk.Material[17]);
        Assert.Equal(0b_0101_0000, chunk.Flags[17]);
        Assert.Equal(9, chunk.Lifetime[17]);
    }

    /// <summary>
    /// 验证 sim chunk 不携带 BGRA / Color 等渲染颜色数组，颜色只在渲染相位生成。
    /// </summary>
    [Fact]
    public void ChunkDoesNotStoreRenderColorState()
    {
        Type chunkType = typeof(Chunk);
        MemberInfo[] members =
        [
            .. chunkType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            .. chunkType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
        ];

        foreach (MemberInfo member in members)
        {
            Type? type = member switch
            {
                FieldInfo field => field.FieldType,
                PropertyInfo property => property.PropertyType,
                _ => null,
            };

            if (type is null)
            {
                continue;
            }

            Assert.False(type == typeof(uint[]) && member.Name.Contains("Color", StringComparison.OrdinalIgnoreCase),
                $"{member.Name} 不应把渲染颜色存入 sim chunk。");
            Assert.False(type == typeof(uint[]) && member.Name.Contains("Render", StringComparison.OrdinalIgnoreCase),
                $"{member.Name} 不应把 render buffer 存入 sim chunk。");
        }
    }

    /// <summary>
    /// 验证 Simulation 程序集没有引入 AoS `Cell` struct 作为热路径状态容器。
    /// </summary>
    [Fact]
    public void SimulationDoesNotExposeAosCellStruct()
    {
        Type? cellStruct = typeof(Chunk).Assembly.GetTypes()
            .FirstOrDefault(type => type is { Name: "Cell", IsValueType: true });

        Assert.Null(cellStruct);
    }

    /// <summary>
    /// 验证温度场保持 1/4 分辨率，每个 64x64 chunk 只对应 16x16 温度子块。
    /// </summary>
    [Fact]
    public void TemperatureFieldUsesQuarterResolutionBlocks()
    {
        Assert.Equal(4, EngineConstants.TempFieldDownscale);
        Assert.Equal(16, TemperatureField.BlockSize);
        Assert.Equal(TemperatureField.BlockSize * TemperatureField.BlockSize, TemperatureField.BlockArea);
        Assert.Equal(EngineConstants.ChunkArea / (EngineConstants.TempFieldDownscale * EngineConstants.TempFieldDownscale), TemperatureField.BlockArea);
    }

    /// <summary>
    /// 验证 BGRA8 render buffer 是渲染相位独立存储，不混入权威 cell。
    /// </summary>
    [Fact]
    public void RenderBufferIsSeparateBgraStorage()
    {
        RenderBuffer buffer = new(1920, 1080);

        Assert.Equal(1920 * 1080, buffer.Pixels.Length);
        Assert.Equal(1920 * 1080 * sizeof(uint), buffer.ByteLength);
    }

    /// <summary>
    /// 验证单常驻 chunk 预算落在架构 §12.2 要求的 18-20KB 区间。
    /// </summary>
    [Fact]
    public void ResidentChunkBudgetStaysWithinPlanEnvelope()
    {
        Assert.InRange(ChunkMemoryBudget.EstimatedResidentChunkBytes, 18 * 1024, 20 * 1024);
    }
}
