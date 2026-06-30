using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本项目生成器测试。
/// </summary>
public sealed class ProjectGeneratorTests
{
    /// <summary>
    /// 验证仓库内 local 模式生成可由 IDE 加载的项目文件。
    /// </summary>
    [Fact]
    public void GenerateOrRefreshCreatesLocalProjectFromTemplate()
    {
        string directory = CreateTempDirectory();
        try
        {
            string root = FindRepositoryRoot();
            ProjectGenerator generator = new();

            ProjectGenerationResult result = generator.GenerateOrRefresh(new GameProjectGenerationOptions(
                directory,
                "GeneratedGame",
                GameProjectReferenceMode.Local)
            {
                EngineRoot = root,
            });

            Assert.True(result.ProjectChanged);
            Assert.True(result.SolutionChanged);
            Assert.True(File.Exists(result.ProjectPath));
            Assert.True(File.Exists(result.SolutionPath));

            XDocument project = XDocument.Load(result.ProjectPath);
            Assert.Equal("net10.0", ReadProperty(project, "TargetFramework"));
            Assert.Equal("true", ReadProperty(project, "GenerateDocumentationFile"));
            Assert.Equal(
                [
                    NormalizeMsBuildPath(Path.Combine(root, "src", "PixelEngine.Hosting", "PixelEngine.Hosting.csproj")),
                    NormalizeMsBuildPath(Path.Combine(root, "src", "PixelEngine.Scripting", "PixelEngine.Scripting.csproj")),
                ],
                ReadIncludes(project, "ProjectReference"));
            Assert.Empty(ReadIncludes(project, "PackageReference"));

            string solution = File.ReadAllText(result.SolutionPath);
            Assert.Contains("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"GeneratedGame\", \"GeneratedGame.csproj\"", solution);
            Assert.Contains("Debug|Any CPU.Build.0 = Debug|Any CPU", solution);
            Assert.Contains("Release|Any CPU.Build.0 = Release|Any CPU", solution);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 验证 package 模式生成 NuGet 引用并替换版本占位符。
    /// </summary>
    [Fact]
    public void GenerateOrRefreshCreatesPackageProjectFromTemplate()
    {
        string directory = CreateTempDirectory();
        try
        {
            ProjectGenerator generator = new();

            ProjectGenerationResult result = generator.GenerateOrRefresh(new GameProjectGenerationOptions(
                directory,
                "ExternalGame",
                GameProjectReferenceMode.Package)
            {
                PixelEngineVersion = "1.2.3",
            });

            XDocument project = XDocument.Load(result.ProjectPath);
            Assert.Equal("true", ReadProperty(project, "GenerateDocumentationFile"));
            Assert.Equal(["PixelEngine.Hosting", "PixelEngine.Scripting"], ReadIncludes(project, "PackageReference"));
            Assert.Empty(ReadIncludes(project, "ProjectReference"));
            foreach (XElement package in project.Descendants("PackageReference"))
            {
                Assert.Equal("1.2.3", package.Attribute("Version")?.Value);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 验证新建脚本后调用刷新不会扰动已最新的 csproj 与 sln。
    /// </summary>
    [Fact]
    public void RefreshAfterScriptCreatedIsIdempotentWhenFilesAreCurrent()
    {
        string directory = CreateTempDirectory();
        try
        {
            ProjectGenerator generator = new();
            GameProjectGenerationOptions options = new(directory, "ScriptRefreshGame", GameProjectReferenceMode.Package)
            {
                PixelEngineVersion = "2.0.0",
            };

            ProjectGenerationResult first = generator.GenerateOrRefresh(options);
            string firstProject = File.ReadAllText(first.ProjectPath);
            string firstSolution = File.ReadAllText(first.SolutionPath);

            _ = Directory.CreateDirectory(Path.Combine(directory, "Scripts"));
            File.WriteAllText(Path.Combine(directory, "Scripts", "PlayerBehaviour.cs"), "namespace Game;\n");

            ProjectGenerationResult second = generator.RefreshAfterScriptCreated(options);

            Assert.False(second.ProjectChanged);
            Assert.False(second.SolutionChanged);
            Assert.Equal(firstProject, File.ReadAllText(second.ProjectPath));
            Assert.Equal(firstSolution, File.ReadAllText(second.SolutionPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    private static string NormalizeMsBuildPath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "PixelEngine.ProjectGeneratorTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(path);
        return path;
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
