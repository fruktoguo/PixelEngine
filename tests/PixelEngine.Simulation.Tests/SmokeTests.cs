using System.Reflection;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 项目的最小程序集加载冒烟测试。
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// 验证项目程序集可以加载，并且运行时可以枚举其中的类型。
    /// </summary>
    [Fact]
    public void ProjectAssemblyCanBeLoadedAndTypesEnumerated()
    {
        Assembly assembly = Assembly.Load("PixelEngine.Simulation");
        Type[] types = assembly.GetTypes();

        Assert.Equal("PixelEngine.Simulation", assembly.GetName().Name);
        Assert.NotNull(types);
    }
}
