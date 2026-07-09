using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 项目的工程纪律测试。
/// 不变式：Simulation 项目引用与 plan 文档约定一致。
/// </summary>
public sealed class SimulationProjectDisciplineTests
{
    /// <summary>
    /// 验证 Simulation 只引用 Core，不反向依赖 Rendering / Physics / Content / World。
    /// </summary>
    [Fact]
    public void SimulationReferencesOnlyCore()
    {
        string root = FindRepositoryRoot();
        string[] references = ReadProjectReferenceNames(root, "PixelEngine.Simulation");

        Assert.Equal(["PixelEngine.Core"], references);
    }

    private static string[] ReadProjectReferenceNames(string root, string projectName)
    {
        string projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
        XDocument project = XDocument.Load(projectPath);
        return
        [
            .. project
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFileNameWithoutExtension(include!)),
        ];
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
