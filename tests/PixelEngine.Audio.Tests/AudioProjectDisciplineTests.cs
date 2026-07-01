using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Audio.Tests;

/// <summary>
/// Audio 项目的工程纪律测试。
/// </summary>
public sealed class AudioProjectDisciplineTests
{
    /// <summary>
    /// 验证 Audio 使用 OpenAL 绑定并允许封装层 unsafe。
    /// </summary>
    [Fact]
    public void AudioProjectReferencesOpenAlAndAllowsUnsafe()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "src", "PixelEngine.Audio", "PixelEngine.Audio.csproj"));
        string xml = project.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("Silk.NET.OpenAL", xml, StringComparison.Ordinal);
        Assert.Contains("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Audio 只引用允许的基础项目，不反向依赖 World / Physics / Rendering / Hosting。
    /// </summary>
    [Fact]
    public void AudioKeepsExpectedProjectReferences()
    {
        string root = FindRepositoryRoot();
        string[] references = ReadProjectReferenceNames(root, "PixelEngine.Audio");

        Assert.Equal(["PixelEngine.Core", "PixelEngine.Content", "PixelEngine.Simulation"], references);
    }

    /// <summary>
    /// 验证 Audio 源码不声明新的平台互操作入口。
    /// </summary>
    [Fact]
    public void AudioSourcesDoNotDeclareDllImport()
    {
        string root = FindRepositoryRoot();
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "src", "PixelEngine.Audio"), "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("DllImport", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LibraryImport", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证音频主派发路径不读取 sim 网格，也不调用加载 / 流式 worker。
    /// </summary>
    [Fact]
    public void AudioDispatchHotPathDoesNotAccessGridOrAssetLoading()
    {
        string root = FindRepositoryRoot();
        AssertSourceDoesNotContain(
            Path.Combine(root, "src", "PixelEngine.Audio", "AudioDispatcher.cs"),
            "CellGrid",
            "Chunk",
            "IChunkSource",
            "LoadAsync",
            "AudioClipCache",
            "AudioStreamPlayer");
        AssertSourceDoesNotContain(
            Path.Combine(root, "src", "PixelEngine.Hosting", "AudioPhaseDriver.cs"),
            "CellGrid",
            "Chunk",
            "IChunkSource",
            "LoadAsync",
            "AudioClipCache",
            "AudioStreamPlayer");
    }

    private static void AssertSourceDoesNotContain(string path, params string[] forbiddenTokens)
    {
        string source = File.ReadAllText(path);
        foreach (string token in forbiddenTokens)
        {
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }
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
