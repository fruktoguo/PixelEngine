using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class UiContractTests
{
    [Theory]
    [InlineData(typeof(UiValue))]
    [InlineData(typeof(UiEvent))]
    [InlineData(typeof(UiDocumentHandle))]
    [InlineData(typeof(UiScreenHandle))]
    [InlineData(typeof(UiScreenId))]
    [InlineData(typeof(UiElementId))]
    [InlineData(typeof(UiActionId))]
    [InlineData(typeof(UiPathId))]
    [InlineData(typeof(UiStringHandle))]
    public void HotPathContractsContainNoManagedReferences(Type type)
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiValue>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiEvent>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiDocumentHandle>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiScreenHandle>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiScreenId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiElementId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiActionId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiPathId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiStringHandle>());
        Assert.True(type.IsValueType);
    }

    [Fact]
    public void UiAssemblyDoesNotReferenceEditorOrScripting()
    {
        Assembly assembly = typeof(GameUiHost).Assembly;
        string[] references = [.. assembly.GetReferencedAssemblies().Select(static name => name.Name ?? string.Empty)];

        Assert.DoesNotContain("PixelEngine.Editor", references);
        Assert.DoesNotContain("PixelEngine.Scripting", references);
    }

    [Fact]
    public void UiPublicApiMembersHaveChineseXmlDocumentation()
    {
        string root = FindRepositoryRoot();
        string projectDirectory = Path.Combine(root, "src", "PixelEngine.UI");
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly))
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

    [Fact]
    public void UiValuePreservesTypedPayload()
    {
        UiValue number = new(42L);
        UiValue scalar = new(12.5);
        UiValue flag = UiValue.FromBoolean(true);
        UiValue text = UiValue.FromStringHandle(new UiStringHandle(7));

        Assert.Equal(42L, number.AsInt64());
        Assert.Equal(12.5, scalar.AsDouble());
        Assert.True(flag.AsBoolean());
        Assert.Equal(new UiStringHandle(7), text.AsStringHandle());
        _ = Assert.Throws<InvalidOperationException>(() => scalar.AsInt64());
    }

    private static bool IsPublicApiDeclaration(string line)
    {
        string trimmed = line.TrimStart();
        return Regex.IsMatch(
            trimmed,
            @"^public\s+(sealed\s+|static\s+|readonly\s+|record\s+|ref\s+|enum\s+|interface\s+|class\s+|struct\s+|delegate\s+|[A-Z_a-z])");
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

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
