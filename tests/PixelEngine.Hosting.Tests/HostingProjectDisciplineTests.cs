using System.Text.RegularExpressions;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 项目的工程纪律测试。
/// </summary>
public sealed class HostingProjectDisciplineTests
{
    /// <summary>
    /// 验证 Hosting 公开 API 都带中文 XML 文档注释。
    /// </summary>
    [Fact]
    public void HostingPublicApiMembersHaveChineseXmlDocumentation()
    {
        string root = FindRepositoryRoot();
        string projectDirectory = Path.Combine(root, "src", "PixelEngine.Hosting");
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string[] lines = File.ReadAllLines(file);
            int braceDepth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (braceDepth <= 1 && IsPublicApiDeclaration(lines[i]))
                {
                    int previous = PreviousNonAttributeLine(lines, i);
                    Assert.True(
                        previous >= 0 && IsChineseXmlDocumentationBlock(lines, previous),
                        $"{file}:{i + 1} 公开 API 缺少中文 XML 文档注释。");
                }

                braceDepth += GetBraceDepthDelta(lines[i]);
            }
        }
    }

    private static bool IsPublicApiDeclaration(string line)
    {
        string trimmed = line.TrimStart();
        return Regex.IsMatch(
            trimmed,
            @"^public\s+(sealed\s+|static\s+|readonly\s+|record\s+|enum\s+|interface\s+|class\s+|struct\s+|delegate\s+|[A-Z_a-z])");
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

    private static int GetBraceDepthDelta(string line)
    {
        int delta = 0;
        foreach (char character in line)
        {
            if (character == '{')
            {
                delta++;
            }
            else if (character == '}')
            {
                delta--;
            }
        }

        return delta;
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
