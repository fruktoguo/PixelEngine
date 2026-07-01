using System.Text.RegularExpressions;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/16 热路径源码纪律测试。
/// </summary>
public sealed class PerformanceHardeningHotPathDisciplineTests
{
    private static readonly string[] HotPathFiles =
    [
        Path.Combine("src", "PixelEngine.Simulation", "ChunkUpdater.cs"),
        Path.Combine("src", "PixelEngine.Simulation", "CheckerboardScheduler.cs"),
        Path.Combine("src", "PixelEngine.Simulation", "ReactionEngine.cs"),
        Path.Combine("src", "PixelEngine.Simulation", "TemperatureField.cs"),
        Path.Combine("src", "PixelEngine.Simulation", "Particles", "ParticleSystem.cs"),
        Path.Combine("src", "PixelEngine.Rendering", "RenderBufferBuilder.cs"),
        Path.Combine("src", "PixelEngine.Rendering", "ParticleCompositor.cs"),
        Path.Combine("src", "PixelEngine.Serialization", "ChunkCodec.cs"),
    ];

    private static readonly Regex[] ForbiddenPatterns =
    [
        new(@"\b(Select|Where|Aggregate|ToList|ToArray)\s*\(", RegexOptions.Compiled),
        new(@"yield\s+return", RegexOptions.Compiled),
        new(@"params\s+", RegexOptions.Compiled),
        new(@"FormattableString", RegexOptions.Compiled),
        new(@"\$""", RegexOptions.Compiled),
        new(@"string\.Concat", RegexOptions.Compiled),
    ];

    /// <summary>
    /// 验证 plan/16 明确列出的热路径文件不含 LINQ、迭代器、params 或字符串拼接。
    /// </summary>
    [Fact]
    public void HotPathSourcesAvoidAllocationProneConstructs()
    {
        string root = FindRepositoryRoot();
        foreach (string relativePath in HotPathFiles)
        {
            string source = RemoveExceptionalLines(File.ReadAllLines(Path.Combine(root, relativePath)));
            foreach (Regex pattern in ForbiddenPatterns)
            {
                Match match = pattern.Match(source);
                Assert.False(match.Success, $"{relativePath} 命中热路径禁用模式 `{pattern}`：{match.Value}");
            }
        }
    }

    private static string RemoveExceptionalLines(string[] lines)
    {
        return string.Join(
            Environment.NewLine,
            lines.Where(static line =>
                !line.Contains("throw ", StringComparison.Ordinal) &&
                !line.Contains("Debug.Assert", StringComparison.Ordinal)));
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
