using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PixelEngine.Scripting;

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

    public ProjectGenerationResult RefreshAfterScriptCreated(GameProjectGenerationOptions options)
    {
        return GenerateOrRefresh(options);
    }

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

    private static string NormalizeMsBuildPath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }

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

internal sealed record GameProjectGenerationOptions(string ProjectDirectory, string ProjectName, GameProjectReferenceMode ReferenceMode)
{
    public string? EngineRoot { get; init; }

    public string? PixelEngineVersion { get; init; }
}

internal enum GameProjectReferenceMode
{
    Local,
    Package,
}

internal readonly record struct ProjectGenerationResult(string ProjectPath, string SolutionPath, bool ProjectChanged, bool SolutionChanged);
