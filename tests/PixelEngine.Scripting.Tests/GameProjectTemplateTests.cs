using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 游戏项目模板测试。
/// 不变式：模板生成工程可编译、默认场景与脚本入口存在。
/// </summary>
public sealed class GameProjectTemplateTests
{
    /// <summary>
    /// 验证仓库内游戏模板只经 Hosting 与 Scripting 公开项目引用接入引擎。
    /// </summary>
    [Fact]
    public void LocalGameTemplateReferencesOnlyPublicEntryProjects()
    {
        XDocument project = LoadTemplate("PixelEngine.Game.local.csproj.template");

        Assert.Equal("net10.0", ReadProperty(project, "TargetFramework"));
        Assert.Equal("true", ReadProperty(project, "GenerateDocumentationFile"));
        Assert.Equal(
            [
                "{{EngineRoot}}/src/PixelEngine.Hosting/PixelEngine.Hosting.csproj",
                "{{EngineRoot}}/src/PixelEngine.Scripting/PixelEngine.Scripting.csproj",
            ],
            ReadIncludes(project, "ProjectReference"));
        Assert.Empty(ReadIncludes(project, "PackageReference"));
    }

    /// <summary>
    /// 验证外部游戏模板通过 NuGet 包引用公开入口，并显式生成 XML 文档。
    /// </summary>
    [Fact]
    public void PackageGameTemplateReferencesOnlyPublicEntryPackages()
    {
        XDocument project = LoadTemplate("PixelEngine.Game.package.csproj.template");

        Assert.Equal("net10.0", ReadProperty(project, "TargetFramework"));
        Assert.Equal("true", ReadProperty(project, "GenerateDocumentationFile"));
        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Scripting"],
            ReadIncludes(project, "PackageReference"));
        Assert.Empty(ReadIncludes(project, "ProjectReference"));
        foreach (XElement package in project.Descendants("PackageReference"))
        {
            Assert.Equal("{{PixelEngineVersion}}", package.Attribute("Version")?.Value);
        }
    }

    private static XDocument LoadTemplate(string fileName)
    {
        string root = FindRepositoryRoot();
        string templatePath = Path.Combine(root, "src", "PixelEngine.Scripting", "Templates", fileName);
        return XDocument.Load(templatePath);
    }

    private static string? ReadProperty(XDocument project, string name)
    {
        return project.Descendants(name).SingleOrDefault()?.Value.Trim();
    }

    private static string[] ReadIncludes(XDocument project, string elementName)
    {
        return
        [
            .. project
                .Descendants(elementName)
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!),
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
