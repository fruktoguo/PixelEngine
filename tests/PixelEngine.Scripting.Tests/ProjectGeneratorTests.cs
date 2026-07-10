using System.Xml.Linq;
using System.Diagnostics;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本项目生成器测试。
/// 不变式：生成的 csproj/场景文件满足 Scripting 装配契约。
/// </summary>
public sealed class ProjectGeneratorTests
{
    /// <summary>
    /// 验证仓库内 local 模式生成可由 IDE 加载的项目文件。
    /// </summary>
    [Fact]
    public void GenerateOrRefreshCreatesLocalProjectFromTemplate()
    {
        // Arrange：准备输入与初始状态
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

            // Assert：验证预期结果
            Assert.True(result.ProjectChanged);
            Assert.True(result.SolutionChanged);
            Assert.True(File.Exists(result.ProjectPath));
            Assert.True(File.Exists(result.SolutionPath));

            XDocument project = XDocument.Load(result.ProjectPath);
            Assert.Equal("net10.0", ReadProperty(project, "TargetFramework"));
            Assert.Equal("true", ReadProperty(project, "GenerateDocumentationFile"));
            Assert.Equal("false", ReadProperty(project, "ServerGarbageCollection"));
            Assert.Equal("true", ReadProperty(project, "ConcurrentGarbageCollection"));
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
        // Arrange：准备输入与初始状态
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
            // Assert：验证预期结果
            Assert.Equal("true", ReadProperty(project, "GenerateDocumentationFile"));
            Assert.Equal("false", ReadProperty(project, "ServerGarbageCollection"));
            Assert.Equal("true", ReadProperty(project, "ConcurrentGarbageCollection"));
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
        // Arrange：准备输入与初始状态
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

            // Assert：验证预期结果
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

    /// <summary>
    /// 验证生成的本地游戏项目只通过公开入口引用即可编译并运行。
    /// </summary>
    [Fact]
    public void GeneratedLocalProjectCompilesAndRunsAgainstPublicApi()
    {
        // Arrange：准备输入与初始状态
        string directory = CreateTempDirectory();
        try
        {
            string root = FindRepositoryRoot();
            ProjectGenerator generator = new();
            ProjectGenerationResult result = generator.GenerateOrRefresh(new GameProjectGenerationOptions(
                directory,
                "RunnableGame",
                GameProjectReferenceMode.Local)
            {
                EngineRoot = root,
            });

            string scriptsDirectory = Path.Combine(directory, "Scripts");
            _ = Directory.CreateDirectory(scriptsDirectory);
            File.WriteAllText(Path.Combine(directory, "Program.cs"), """
                using RunnableGame;
                using PixelEngine.Scripting;

                Scene scene = new();
                Entity entity = scene.CreateEntity();
                _ = entity.AddComponent<PlayerBehaviour>();
                return 0;
                """);
            File.WriteAllText(Path.Combine(scriptsDirectory, "PlayerBehaviour.cs"), """
                using PixelEngine.Scripting;

                namespace RunnableGame;

                public sealed class PlayerBehaviour : Behaviour
                {
                    [Persist]
                    public int Score;

                    protected override void OnUpdate(float dt)
                    {
                        if (Context.Time.FrameCount >= 0)
                        {
                            Score++;
                        }
                    }
                }
                """);

            DotnetResult run = RunDotnet(
                "run",
                "--project",
                result.ProjectPath,
                "-c",
                "Release");

            // Assert：验证预期结果
            Assert.True(run.ExitCode == 0, run.Output);
            string documentationPath = Path.Combine(
                directory,
                "bin",
                "Release",
                "net10.0",
                "PixelEngine.Scripting.xml");
            Assert.True(File.Exists(documentationPath), $"缺少脚本 IntelliSense XML 文档：{documentationPath}");
            string documentation = File.ReadAllText(documentationPath);
            Assert.Contains("脚本访问引擎能力的统一入口", documentation, StringComparison.Ordinal);
            Assert.Contains("延迟移动角色控制器", documentation, StringComparison.Ordinal);
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

    private static DotnetResult RunDotnet(params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 dotnet。");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet run 超时。");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        return new DotnetResult(process.ExitCode, output + error);
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

    private readonly record struct DotnetResult(int ExitCode, string Output);
}
