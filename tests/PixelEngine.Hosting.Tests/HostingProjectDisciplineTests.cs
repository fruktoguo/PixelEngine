using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 项目的工程纪律测试。
/// </summary>
public sealed class HostingProjectDisciplineTests
{
    /// <summary>
    /// 验证 Demo 只经 Hosting 与 Scripting 公开入口引用引擎。
    /// </summary>
    [Fact]
    public void DemoProjectReferencesOnlyPublicEntryProjects()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "demo", "PixelEngine.Demo", "PixelEngine.Demo.csproj"));

        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Scripting"],
            [
                .. ReadIncludes(project, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include)!),
            ]);
        Assert.Empty(ReadIncludes(project, "PackageReference"));
    }

    /// <summary>
    /// 验证独立编辑器壳位于 apps 层，只引用 Hosting、Editor 与 Gui 三个公开装配入口。
    /// </summary>
    [Fact]
    public void EditorShellProjectReferencesOnlyShellEntryProjects()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "PixelEngine.Editor.Shell.csproj"));

        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Editor", "PixelEngine.Gui"],
            [
                .. ReadIncludes(project, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include)!),
            ]);
    }

    /// <summary>
    /// 验证编辑器壳只通过中性 bootstrap 创建唯一窗口，不直接散落创建 RenderWindow。
    /// </summary>
    [Fact]
    public void EditorShellCreatesWindowOnlyThroughNeutralBootstrap()
    {
        string root = FindRepositoryRoot();
        string shellSource = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "apps", "PixelEngine.Editor.Shell"), "*.cs").Select(File.ReadAllText));

        Assert.Contains("EditorShellWindow.Create()", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorHostBootstrap.Create", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderWindow.Create", shellSource.Replace("EditorHostBootstrap.Create", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo 源码不绕过 Hosting/Scripting 公开入口访问内容或模拟实现。
    /// </summary>
    [Fact]
    public void DemoSourcesDoNotBypassHostingFacade()
    {
        string root = FindRepositoryRoot();
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "demo", "PixelEngine.Demo"), "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("using PixelEngine.Content", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using PixelEngine.Simulation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EngineContentLoader", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Materials.Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Reactions.Count", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo lava-mine 验收场景以 .scene 文件落盘，并通过公开场景文档格式引用 LevelDirector。
    /// </summary>
    [Fact]
    public void DemoLavaMineSceneFileUsesLevelDirectorBehaviour()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine.scene");

        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);

        Assert.Equal("lava-mine", document.Name);
        EngineSceneBehaviourDocument behaviour = Assert.Single(Assert.Single(document.Entities!).Behaviours!);
        Assert.Equal("PixelEngine.Demo.LevelDirector", behaviour.TypeName);
        Assert.Equal("640", behaviour.SerializedFields!["LevelWidth"]);
        Assert.Equal("360", behaviour.SerializedFields["LevelHeight"]);
        Assert.Equal("true", behaviour.SerializedFields["BuildScriptEntities"]);
    }

    /// <summary>
    /// 验证音频窗口探针不是黑屏空场景，截图门禁能观察到真实可见内容。
    /// </summary>
    [Fact]
    public void DemoAudioProbeSceneMaterializesVisibleLevelDirector()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine-audio-probe.scene");

        AssertProbeSceneUsesVisibleLevelDirector(scenePath, "lava-mine-audio-probe");
    }

    /// <summary>
    /// 验证粒子 / 光照窗口探针不是黑屏空场景，截图门禁能观察到真实可见内容。
    /// </summary>
    [Fact]
    public void DemoParticleLightProbeSceneMaterializesVisibleLevelDirector()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine-particle-light-probe.scene");

        AssertProbeSceneUsesVisibleLevelDirector(scenePath, "lava-mine-particle-light-probe");
    }

    /// <summary>
    /// 验证 Demo 可见内容包 API 不泄漏 Content / Simulation 实现类型。
    /// </summary>
    [Fact]
    public void EngineContentPackagePublicApiDoesNotExposeImplementationAssemblies()
    {
        foreach (MemberInfo member in typeof(EngineContentPackage).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (member is MethodInfo method)
            {
                AssertAllowedPublicType(method.ReturnType, member.Name);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertAllowedPublicType(parameter.ParameterType, member.Name);
                }
            }
            else if (member is PropertyInfo property)
            {
                AssertAllowedPublicType(property.PropertyType, member.Name);
            }
            else if (member is ConstructorInfo constructor)
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    AssertAllowedPublicType(parameter.ParameterType, member.Name);
                }
            }
        }
    }

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

    private static void AssertAllowedPublicType(Type type, string memberName)
    {
        Type publicType = UnwrapType(type);
        Assert.False(
            publicType.Namespace?.StartsWith("PixelEngine.Simulation", StringComparison.Ordinal) == true ||
            publicType.Namespace?.StartsWith("PixelEngine.Content", StringComparison.Ordinal) == true,
            $"{memberName} 泄漏了实现类型 {publicType.FullName}。");
        foreach (Type argument in publicType.GenericTypeArguments)
        {
            AssertAllowedPublicType(argument, memberName);
        }
    }

    private static void AssertProbeSceneUsesVisibleLevelDirector(string scenePath, string expectedName)
    {
        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);

        Assert.Equal(expectedName, document.Name);
        EngineSceneBehaviourDocument behaviour = Assert.Single(Assert.Single(document.Entities!).Behaviours!);
        Assert.Equal("PixelEngine.Demo.LevelDirector", behaviour.TypeName);
        Assert.Equal("true", behaviour.SerializedFields!["BuildScriptEntities"]);
        Assert.Equal("640", behaviour.SerializedFields["LevelWidth"]);
        Assert.Equal("360", behaviour.SerializedFields["LevelHeight"]);
    }

    private static Type UnwrapType(Type type)
    {
        Type current = type;
        while (current.IsByRef || current.IsPointer || current.IsArray)
        {
            current = current.GetElementType()!;
        }

        return current;
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
