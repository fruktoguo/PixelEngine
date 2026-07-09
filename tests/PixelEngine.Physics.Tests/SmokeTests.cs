using System.Reflection;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// Physics 项目的最小程序集加载冒烟测试。
/// 不变式：测试程序集可加载、最小冒烟路径不抛异常。
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// 验证项目程序集可以加载，并且运行时可以枚举其中的类型。
    /// </summary>
    [Fact]
    public void ProjectAssemblyCanBeLoadedAndTypesEnumerated()
    {
        Assembly assembly = Assembly.Load("PixelEngine.Physics");
        Type[] types = assembly.GetTypes();

        Assert.Equal("PixelEngine.Physics", assembly.GetName().Name);
        Assert.NotNull(types);
    }
}
