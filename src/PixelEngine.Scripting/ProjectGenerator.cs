using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PixelEngine.Scripting;

/// <summary>
/// 根据模板生成或刷新用户脚本 .csproj 与 .sln 文件。
/// </summary>
internal sealed class ProjectGenerator
{
    private const string LocalTemplateName = "PixelEngine.Game.local.csproj.template";
    private const string PackageTemplateName = "PixelEngine.Game.package.csproj.template";
    private const string CSharpProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

    private readonly string _templateDirectory;

    public ProjectGenerator()
        : this(FindDefaultTemplateDirectory())
    {
    }

    public ProjectGenerator(string templateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);
        _templateDirectory = Path.GetFullPath(templateDirectory);
    }

    /// <summary>
    /// 生成或刷新脚本项目与解决方案；内容未变时跳过写入。
    /// </summary>
    public ProjectGenerationResult GenerateOrRefresh(GameProjectGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        string projectDirectory = Path.GetFullPath(options.ProjectDirectory);
        _ = Directory.CreateDirectory(projectDirectory);

        string projectPath = Path.Combine(projectDirectory, options.ProjectName + ".csproj");
        string solutionPath = Path.Combine(projectDirectory, options.ProjectName + ".sln");
        string projectText = BuildProjectText(options);
        string solutionText = BuildSolutionText(options.ProjectName, projectPath, solutionPath);

        bool projectChanged = WriteIfChanged(projectPath, projectText);
        bool solutionChanged = WriteIfChanged(solutionPath, solutionText);
        return new ProjectGenerationResult(projectPath, solutionPath, projectChanged, solutionChanged);
    }

    /// <summary>
    /// 新建脚本文件后刷新项目；当前与 <see cref="GenerateOrRefresh"/> 行为相同。
    /// </summary>
    public ProjectGenerationResult RefreshAfterScriptCreated(GameProjectGenerationOptions options)
    {
        return GenerateOrRefresh(options);
    }

    /// <summary>
    /// 从模板构建 .csproj 文本，替换占位符并确保生成 XML 文档。
    /// </summary>
    private string BuildProjectText(GameProjectGenerationOptions options)
    {
        string templateName = options.ReferenceMode == GameProjectReferenceMode.Local
            ? LocalTemplateName
            : PackageTemplateName;
        string templatePath = Path.Combine(_templateDirectory, templateName);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"脚本项目模板不存在：{templatePath}", templatePath);
        }

        string text = File.ReadAllText(templatePath, Encoding.UTF8);
        if (options.EngineRoot is not null)
        {
            text = text.Replace("{{EngineRoot}}", NormalizeMsBuildPath(Path.GetFullPath(options.EngineRoot)), StringComparison.Ordinal);
        }

        if (options.PixelEngineVersion is not null)
        {
            text = text.Replace("{{PixelEngineVersion}}", options.PixelEngineVersion, StringComparison.Ordinal);
        }

        if (text.Contains("{{", StringComparison.Ordinal) || text.Contains("}}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"脚本项目模板仍存在未解析占位符：{templatePath}");
        }

        XDocument project = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        EnsureGenerateDocumentationFile(project);
        return project.Declaration is null
            ? project.ToString(SaveOptions.DisableFormatting) + Environment.NewLine
            : project.Declaration + Environment.NewLine + project.ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
    }

    /// <summary>
    /// 生成 Visual Studio 12 格式解决方案文本；项目 GUID 由名称与相对路径稳定派生。
    /// </summary>
    private static string BuildSolutionText(string projectName, string projectPath, string solutionPath)
    {
        string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        string relativeProjectPath = Path.GetRelativePath(solutionDirectory, projectPath).Replace('/', '\\');
        string projectGuid = CreateStableGuid(projectName + "|" + relativeProjectPath).ToString("D").ToUpperInvariant();
        StringBuilder builder = new();
        _ = builder
            .AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00")
            .AppendLine("# Visual Studio Version 17")
            .AppendLine("VisualStudioVersion = 17.0.31903.59")
            .AppendLine("MinimumVisualStudioVersion = 10.0.40219.1")
            .Append("Project(\"{").Append(CSharpProjectTypeGuid).Append("}\") = \"")
            .Append(projectName).Append("\", \"").Append(relativeProjectPath).Append("\", \"{").Append(projectGuid).AppendLine("}\"")
            .AppendLine("EndProject")
            .AppendLine("Global")
            .AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution")
            .AppendLine("\t\tDebug|Any CPU = Debug|Any CPU")
            .AppendLine("\t\tRelease|Any CPU = Release|Any CPU")
            .AppendLine("\tEndGlobalSection")
            .AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution")
            .Append("\t\t{").Append(projectGuid).AppendLine("}.Debug|Any CPU.ActiveCfg = Debug|Any CPU")
            .Append("\t\t{").Append(projectGuid).AppendLine("}.Debug|Any CPU.Build.0 = Debug|Any CPU")
            .Append("\t\t{").Append(projectGuid).AppendLine("}.Release|Any CPU.ActiveCfg = Release|Any CPU")
            .Append("\t\t{").Append(projectGuid).AppendLine("}.Release|Any CPU.Build.0 = Release|Any CPU")
            .AppendLine("\tEndGlobalSection")
            .AppendLine("\tGlobalSection(SolutionProperties) = preSolution")
            .AppendLine("\t\tHideSolutionNode = FALSE")
            .AppendLine("\tEndGlobalSection")
            .AppendLine("EndGlobal");
        return builder.ToString();
    }

    /// <summary>
    /// 确保项目启用 GenerateDocumentationFile，便于 IDE 显示脚本 API 文档。
    /// </summary>
    private static void EnsureGenerateDocumentationFile(XDocument project)
    {
        XElement root = project.Root ?? throw new InvalidOperationException("项目模板不是有效 XML。");
        XElement? propertyGroup = root.Elements("PropertyGroup").FirstOrDefault();
        if (propertyGroup is null)
        {
            propertyGroup = new XElement("PropertyGroup");
            root.AddFirst(propertyGroup);
        }

        XElement? generateDocumentationFile = propertyGroup.Elements("GenerateDocumentationFile").SingleOrDefault();
        if (generateDocumentationFile is null)
        {
            propertyGroup.Add(new XElement("GenerateDocumentationFile", "true"));
            return;
        }

        generateDocumentationFile.Value = "true";
    }

    /// <summary>
    /// 仅当目标文件不存在或内容不同时写入，减少 IDE 无关刷新。
    /// </summary>
    private static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path, Encoding.UTF8);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return false;
            }
        }

        File.WriteAllText(path, content, Encoding.UTF8);
        return true;
    }

    private static void ValidateOptions(GameProjectGenerationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectName);
        if (options.ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("项目名不能包含文件名非法字符。", nameof(options));
        }

        if (options.ReferenceMode == GameProjectReferenceMode.Local)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.EngineRoot);
            return;
        }

        if (options.ReferenceMode == GameProjectReferenceMode.Package)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.PixelEngineVersion);
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(options), options.ReferenceMode, "未知脚本项目引用模式。");
    }

    /// <summary>
    /// 将路径规范为 MSBuild 友好的正斜杠形式。
    /// </summary>
    private static string NormalizeMsBuildPath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }

    /// <summary>
    /// 从当前程序集目录向上搜索引擎源码树中的 Templates 目录。
    /// </summary>
    private static string FindDefaultTemplateDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string templateDirectory = Path.Combine(directory.FullName, "src", "PixelEngine.Scripting", "Templates");
            if (File.Exists(Path.Combine(templateDirectory, LocalTemplateName)) &&
                File.Exists(Path.Combine(templateDirectory, PackageTemplateName)))
            {
                return templateDirectory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine.Scripting/Templates 模板目录。");
    }

    /// <summary>
    /// 由输入字符串派生稳定的 RFC 4122 风格 GUID，用于解决方案项目标识。
    /// </summary>
    private static Guid CreateStableGuid(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

/// <summary>
/// 脚本项目生成选项。
/// </summary>
internal sealed record GameProjectGenerationOptions(string ProjectDirectory, string ProjectName, GameProjectReferenceMode ReferenceMode)
{
    /// <summary>
    /// 本地引用模式下的引擎根目录。
    /// </summary>
    public string? EngineRoot { get; init; }

    /// <summary>
    /// NuGet 包引用模式下的 PixelEngine 版本号。
    /// </summary>
    public string? PixelEngineVersion { get; init; }
}

/// <summary>
/// 脚本项目对引擎的引用方式。
/// </summary>
internal enum GameProjectReferenceMode
{
    /// <summary>通过本地工程路径引用引擎。</summary>
    Local,
    /// <summary>通过 NuGet 包引用引擎。</summary>
    Package,
}

/// <summary>
/// 项目生成操作的结果摘要。
/// </summary>
/// <param name="ProjectPath">生成的 .csproj 绝对路径。</param>
/// <param name="SolutionPath">生成的 .sln 绝对路径。</param>
/// <param name="ProjectChanged">.csproj 是否被实际写入。</param>
/// <param name="SolutionChanged">.sln 是否被实际写入。</param>
internal readonly record struct ProjectGenerationResult(string ProjectPath, string SolutionPath, bool ProjectChanged, bool SolutionChanged);
