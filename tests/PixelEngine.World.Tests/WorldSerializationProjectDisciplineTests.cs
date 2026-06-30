using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// World / Serialization 项目的工程纪律测试。
/// </summary>
public sealed class WorldSerializationProjectDisciplineTests
{
    /// <summary>
    /// 验证 World / Serialization 不开启 unsafe，并且源码中没有 unsafe 块。
    /// </summary>
    [Fact]
    public void WorldAndSerializationStaySafeCodeOnly()
    {
        string root = FindRepositoryRoot();
        AssertProjectDoesNotEnableUnsafe(root, "PixelEngine.World");
        AssertProjectDoesNotEnableUnsafe(root, "PixelEngine.Serialization");
        AssertSourceDoesNotUseUnsafe(Path.Combine(root, "src", "PixelEngine.World"));
        AssertSourceDoesNotUseUnsafe(Path.Combine(root, "src", "PixelEngine.Serialization"));
    }

    /// <summary>
    /// 验证 plan/07 允许的 World / Serialization 项目引用方向。
    /// </summary>
    [Fact]
    public void WorldAndSerializationKeepExpectedProjectReferences()
    {
        string root = FindRepositoryRoot();

        Assert.Equal(
            ["PixelEngine.Core", "PixelEngine.Simulation", "PixelEngine.Serialization"],
            ReadProjectReferenceNames(root, "PixelEngine.World"));
        Assert.Equal(
            ["PixelEngine.Core", "PixelEngine.Simulation"],
            ReadProjectReferenceNames(root, "PixelEngine.Serialization"));
    }

    /// <summary>
    /// 验证 World / Serialization 公开 API 保持中文 XML 文档注释入口。
    /// </summary>
    [Fact]
    public void WorldAndSerializationPublicApiMembersHaveXmlDocumentation()
    {
        string root = FindRepositoryRoot();
        AssertPublicApiHasXmlDocumentation(Path.Combine(root, "src", "PixelEngine.World"));
        AssertPublicApiHasXmlDocumentation(Path.Combine(root, "src", "PixelEngine.Serialization"));
    }

    private static void AssertProjectDoesNotEnableUnsafe(string root, string projectName)
    {
        string projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
        XDocument project = XDocument.Load(projectPath);
        IEnumerable<string> unsafeValues = project
            .Descendants("AllowUnsafeBlocks")
            .Select(element => element.Value.Trim());
        Assert.DoesNotContain(unsafeValues, value => bool.TryParse(value, out bool enabled) && enabled);
    }

    private static void AssertSourceDoesNotUseUnsafe(string projectDirectory)
    {
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(source, @"\bunsafe\b"), $"{file} 不应包含 unsafe 关键字。");
        }
    }

    private static void AssertPublicApiHasXmlDocumentation(string projectDirectory)
    {
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsPublicApiDeclaration(lines[i]))
                {
                    continue;
                }

                int previous = PreviousNonAttributeLine(lines, i);
                Assert.True(
                    previous >= 0 && IsChineseXmlDocumentationBlock(lines, previous),
                    $"{file}:{i + 1} 公开 API 缺少中文 XML 文档注释。");
            }
        }
    }

    private static bool IsPublicApiDeclaration(string line)
    {
        string trimmed = line.TrimStart();
        return Regex.IsMatch(
            trimmed,
            @"^public\s+(sealed\s+|static\s+|readonly\s+|record\s+|ref\s+|enum\s+|interface\s+|class\s+|struct\s+|[A-Z_a-z])");
    }

    private static bool IsChineseXmlDocumentationBlock(string[] lines, int lastDocumentationLine)
    {
        bool hasDocumentation = false;
        bool hasChinese = false;
        bool hasInheritdoc = false;
        for (int i = lastDocumentationLine; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            hasDocumentation = true;
            hasChinese |= Regex.IsMatch(trimmed, @"\p{IsCJKUnifiedIdeographs}");
            hasInheritdoc |= trimmed.Contains("<inheritdoc", StringComparison.Ordinal);
        }

        return hasDocumentation && hasChinese && !hasInheritdoc;
    }

    private static int PreviousNonAttributeLine(string[] lines, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return i;
            }
        }

        return -1;
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
