using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 解析或生成当前 PixelEngine 工程的 IDE 模型，并按用户偏好打开完整 workspace。
/// </summary>
internal sealed class EditorCodeWorkspaceOpenService(
    EditorProject project,
    Func<string> editorCommandProvider,
    IExternalScriptEditorProcessLauncher? processLauncher = null,
    IExternalCodeEditorLocator? editorLocator = null,
    string? referenceAssemblyDirectory = null)
{
    private const long MaximumAutomationWorkspaceFileBytes = 4L * 1024L * 1024L;
    private const string ProjectPlaceholder = "{project}";
    private const string SolutionPlaceholder = "{solution}";
    private const string WorkspacePlaceholder = "{workspace}";
    private const string FilePlaceholder = "{file}";
    private const string LinePlaceholder = "{line}";
    private const string ColumnPlaceholder = "{column}";
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));
    private readonly Func<string> _editorCommandProvider = editorCommandProvider ?? throw new ArgumentNullException(nameof(editorCommandProvider));
    private readonly IExternalScriptEditorProcessLauncher _processLauncher = processLauncher ?? new ExternalScriptEditorProcessLauncher();
    private readonly IExternalCodeEditorLocator _editorLocator = editorLocator ?? new ExternalCodeEditorLocator();
    private readonly string _referenceAssemblyDirectory = Path.GetFullPath(
        referenceAssemblyDirectory ?? EditorCodeWorkspaceFiles.ResolveReferenceAssemblyDirectory());

    public EditorCodeWorkspaceOpenResult OpenCodeProject()
    {
        using EditorCodeWorkspacePreparedOpen prepared = PrepareCodeProject(
            CapturePreparationDescriptor(),
            CancellationToken.None);
        try
        {
            return CommitPrepared(prepared);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            return EditorCodeWorkspaceOpenResult.Failed($"准备 C# 工程文件失败：{exception.Message}");
        }
    }

    internal EditorCodeWorkspacePreparationDescriptor CapturePreparationDescriptor()
    {
        return new EditorCodeWorkspacePreparationDescriptor(
            Path.GetFullPath(_project.ProjectRoot),
            _project.Name,
            Path.GetFullPath(_project.ScriptSourcePath),
            _referenceAssemblyDirectory,
            ExternalCodeEditorPreference.Normalize(_editorCommandProvider()));
    }

    internal bool IsPreparationCurrent(in EditorCodeWorkspacePreparationDescriptor descriptor)
    {
        return descriptor == CapturePreparationDescriptor();
    }

    internal EditorCodeWorkspacePreparedOpen PrepareCodeProject(
        in EditorCodeWorkspacePreparationDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EditorCSharpProjectPlan plan;
        try
        {
            plan = EditorCodeWorkspaceFiles.CreatePlan(
                descriptor.ProjectRoot,
                descriptor.ProjectName,
                descriptor.ScriptSourcePath,
                descriptor.ReferenceAssemblyDirectory);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            return EditorCodeWorkspacePreparedOpen.Failed(
                descriptor,
                $"准备 C# 工程文件失败：{exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        EditorAssetAutomationFileJournal? journal = null;
        try
        {
            journal = StageGeneratedFiles(descriptor.ProjectRoot, plan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ExternalCodeEditorKind kind = ExternalCodeEditorPreference.Classify(descriptor.EditorCommand);
            bool launchable = TryCreateStartInfo(
                descriptor.EditorCommand,
                kind,
                plan.Resolution,
                descriptor.ProjectRoot,
                out ProcessStartInfo? startInfo,
                out string diagnostic);
            cancellationToken.ThrowIfCancellationRequested();
            return new EditorCodeWorkspacePreparedOpen(
                descriptor,
                plan.Resolution,
                kind,
                launchable ? startInfo : null,
                launchable ? string.Empty : diagnostic,
                journal);
        }
        catch
        {
            journal?.Dispose();
            throw;
        }
    }

    internal EditorCodeWorkspaceOpenResult CommitPrepared(EditorCodeWorkspacePreparedOpen prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!IsPreparationCurrent(prepared.Descriptor))
        {
            throw new InvalidOperationException("C# workspace preparation 已因工程或 IDE 设置变化而失效。");
        }

        prepared.ApplyGeneratedFiles();
        EditorCSharpProjectResolution resolution = prepared.Resolution;
        if (prepared.StartInfo is null)
        {
            return EditorCodeWorkspaceOpenResult.Failed(prepared.Diagnostic, resolution);
        }

        if (!_processLauncher.Start(prepared.StartInfo, out string launchDiagnostic))
        {
            string message = string.IsNullOrWhiteSpace(launchDiagnostic)
                ? "启动 C# 工程编辑器失败。"
                : $"启动 C# 工程编辑器失败：{launchDiagnostic}";
            return EditorCodeWorkspaceOpenResult.Failed(message, resolution);
        }

        string openedTarget = prepared.EditorKind is ExternalCodeEditorKind.VsCode or ExternalCodeEditorKind.Custom
            ? resolution.WorkspaceTarget
            : resolution.SolutionPath;
        return new EditorCodeWorkspaceOpenResult(
            true,
            prepared.EditorKind,
            resolution.ProjectPath,
            resolution.SolutionPath,
            openedTarget,
            resolution.ProjectGenerated,
            resolution.SolutionGenerated,
            $"已在 {DisplayName(prepared.EditorKind)} 中打开 C# 工程：{openedTarget}");
    }

    private bool TryCreateStartInfo(
        string editorCommand,
        ExternalCodeEditorKind kind,
        in EditorCSharpProjectResolution resolution,
        string projectRoot,
        out ProcessStartInfo? startInfo,
        out string diagnostic)
    {
        if (kind == ExternalCodeEditorKind.SystemDefault)
        {
            startInfo = new ProcessStartInfo
            {
                FileName = resolution.SolutionPath,
                UseShellExecute = true,
                WorkingDirectory = projectRoot,
            };
            diagnostic = string.Empty;
            return true;
        }

        if (kind != ExternalCodeEditorKind.Custom)
        {
            if (!_editorLocator.TryLocate(kind, out ExternalCodeEditorInstallation installation, out diagnostic))
            {
                startInfo = null;
                return false;
            }

            IReadOnlyList<string> presetArguments = kind switch
            {
                ExternalCodeEditorKind.VsCode => ["--reuse-window", resolution.WorkspaceTarget],
                ExternalCodeEditorKind.VisualStudio or ExternalCodeEditorKind.Rider => [resolution.SolutionPath],
                ExternalCodeEditorKind.SystemDefault or ExternalCodeEditorKind.Custom =>
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "该编辑器类型不使用 preset 启动路径。"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知外部代码编辑器类型。"),
            };
            startInfo = ExternalCodeEditorProcess.CreateStartInfo(
                installation.ExecutablePath,
                presetArguments,
                projectRoot);
            diagnostic = string.Empty;
            return true;
        }

        string[] tokens = ExternalCodeEditorCommandLine.Split(editorCommand, out diagnostic);
        if (tokens.Length == 0)
        {
            startInfo = null;
            diagnostic = string.IsNullOrWhiteSpace(diagnostic) ? "外部代码编辑器命令为空或无效。" : diagnostic;
            return false;
        }

        bool hasTargetPlaceholder = editorCommand.Contains(ProjectPlaceholder, StringComparison.Ordinal) ||
            editorCommand.Contains(SolutionPlaceholder, StringComparison.Ordinal) ||
            editorCommand.Contains(WorkspacePlaceholder, StringComparison.Ordinal);
        if (ContainsScriptLocationPlaceholder(tokens[0]))
        {
            startInfo = null;
            diagnostic = "自定义外部编辑器 executable 不能使用 {file}/{line}/{column} 占位符。";
            return false;
        }

        string executableCommand = ReplacePlaceholders(tokens[0], resolution, projectRoot);
        if (!_editorLocator.TryResolveCustomExecutable(
            executableCommand,
            out string executable,
            out diagnostic))
        {
            startInfo = null;
            return false;
        }

        List<string> arguments = [];
        for (int i = 1; i < tokens.Length; i++)
        {
            if (ContainsScriptLocationPlaceholder(tokens[i]))
            {
                if (arguments.Count > 0 && IsScriptLocationOption(arguments[^1]))
                {
                    arguments.RemoveAt(arguments.Count - 1);
                }

                continue;
            }

            arguments.Add(ReplacePlaceholders(tokens[i], resolution, projectRoot));
        }

        if (!hasTargetPlaceholder)
        {
            arguments.Add(projectRoot);
        }

        startInfo = ExternalCodeEditorProcess.CreateStartInfo(executable, arguments, projectRoot);
        diagnostic = string.Empty;
        return true;
    }

    private static string ReplacePlaceholders(
        string value,
        in EditorCSharpProjectResolution resolution,
        string projectRoot)
    {
        return value
            .Replace(ProjectPlaceholder, projectRoot, StringComparison.Ordinal)
            .Replace(SolutionPlaceholder, resolution.SolutionPath, StringComparison.Ordinal)
            .Replace(WorkspacePlaceholder, resolution.WorkspaceTarget, StringComparison.Ordinal);
    }

    private static EditorAssetAutomationFileJournal? StageGeneratedFiles(
        string projectRoot,
        EditorCSharpProjectPlan plan,
        CancellationToken cancellationToken)
    {
        List<EditorAssetAutomationFileState> before = [];
        List<EditorAssetAutomationFileState> after = [];
        AddGeneratedFile(
            plan.Resolution.ProjectPath,
            plan.ProjectContents,
            before,
            after,
            cancellationToken);
        AddGeneratedFile(
            plan.Resolution.SolutionPath,
            plan.SolutionContents,
            before,
            after,
            cancellationToken);
        return before.Count == 0
            ? null
            : EditorAssetAutomationFileJournal.Stage(
                projectRoot,
                new EditorAssetAutomationFileSnapshot([.. before]),
                new EditorAssetAutomationFileSnapshot([.. after]));
    }

    private static void AddGeneratedFile(
        string path,
        string? contents,
        List<EditorAssetAutomationFileState> before,
        List<EditorAssetAutomationFileState> after,
        CancellationToken cancellationToken)
    {
        if (contents is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] targetContents = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
        byte[]? currentContents = null;
        DateTime? lastWriteTimeUtc = null;
        if (File.Exists(path))
        {
            FileInfo info = new(path);
            if (info.Length > MaximumAutomationWorkspaceFileBytes)
            {
                throw new InvalidOperationException(
                    $"Editor-owned C# workspace 文件超过 {MaximumAutomationWorkspaceFileBytes} 字节上限：{path}");
            }

            currentContents = File.ReadAllBytes(path);
            lastWriteTimeUtc = info.LastWriteTimeUtc;
        }

        if (currentContents is not null && currentContents.AsSpan().SequenceEqual(targetContents))
        {
            return;
        }

        before.Add(new EditorAssetAutomationFileState(path, currentContents, lastWriteTimeUtc));
        after.Add(new EditorAssetAutomationFileState(path, targetContents, null));
    }

    private static bool ContainsScriptLocationPlaceholder(string value)
    {
        return value.Contains(FilePlaceholder, StringComparison.Ordinal) ||
            value.Contains(LinePlaceholder, StringComparison.Ordinal) ||
            value.Contains(ColumnPlaceholder, StringComparison.Ordinal);
    }

    private static bool IsScriptLocationOption(string value)
    {
        return value.Equals("--goto", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("/Edit", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("/Command", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("--line", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("--column", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayName(ExternalCodeEditorKind kind)
    {
        return kind switch
        {
            ExternalCodeEditorKind.VsCode => "Visual Studio Code",
            ExternalCodeEditorKind.VisualStudio => "Visual Studio",
            ExternalCodeEditorKind.Rider => "JetBrains Rider",
            ExternalCodeEditorKind.SystemDefault => "系统默认程序",
            ExternalCodeEditorKind.Custom => "自定义编辑器",
            _ => kind.ToString(),
        };
    }
}

internal readonly record struct EditorCodeWorkspacePreparationDescriptor(
    string ProjectRoot,
    string ProjectName,
    string ScriptSourcePath,
    string ReferenceAssemblyDirectory,
    string EditorCommand);

internal sealed class EditorCodeWorkspacePreparedOpen : IDisposable
{
    private EditorAssetAutomationFileJournal? _journal;
    private int _filesApplied;
    private int _disposed;

    internal EditorCodeWorkspacePreparedOpen(
        EditorCodeWorkspacePreparationDescriptor descriptor,
        EditorCSharpProjectResolution resolution,
        ExternalCodeEditorKind editorKind,
        ProcessStartInfo? startInfo,
        string diagnostic,
        EditorAssetAutomationFileJournal? journal)
    {
        Descriptor = descriptor;
        Resolution = resolution;
        EditorKind = editorKind;
        StartInfo = startInfo;
        Diagnostic = diagnostic;
        _journal = journal;
    }

    internal EditorCodeWorkspacePreparationDescriptor Descriptor { get; }

    internal EditorCSharpProjectResolution Resolution { get; }

    internal ExternalCodeEditorKind EditorKind { get; }

    internal ProcessStartInfo? StartInfo { get; }

    internal string Diagnostic { get; }

    internal bool FilesChanged => _journal is not null;

    internal static EditorCodeWorkspacePreparedOpen Failed(
        EditorCodeWorkspacePreparationDescriptor descriptor,
        string diagnostic)
    {
        return new EditorCodeWorkspacePreparedOpen(
            descriptor,
            default,
            default,
            null,
            diagnostic,
            null);
    }

    internal void ApplyGeneratedFiles()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _filesApplied, 1) != 0)
        {
            throw new InvalidOperationException("C# workspace prepared files 只能提交一次。");
        }

        _journal?.ApplyAfter();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        EditorAssetAutomationFileJournal? journal = Interlocked.Exchange(ref _journal, null);
        journal?.Dispose();
    }
}

internal sealed class EditorCodeWorkspaceAutomationWorkspace
{
    private readonly EditorCodeWorkspaceOpenService _service;
    private readonly EditorCodeWorkspacePreparationDescriptor _descriptor;

    internal EditorCodeWorkspaceAutomationWorkspace(
        EditorProjectSession session,
        EditorCodeWorkspaceOpenService service)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _descriptor = service.CapturePreparationDescriptor();
    }

    internal EditorProjectSession Session { get; }

    internal EditorCodeWorkspacePreparedOpen Prepare(CancellationToken cancellationToken)
    {
        return _service.PrepareCodeProject(_descriptor, cancellationToken);
    }

    internal bool IsCurrent(EditorProjectSession? currentSession)
    {
        return ReferenceEquals(currentSession, Session) &&
            _service.IsPreparationCurrent(_descriptor);
    }

    internal EditorCodeWorkspaceOpenResult Commit(EditorCodeWorkspacePreparedOpen prepared)
    {
        return _service.CommitPrepared(prepared);
    }
}

internal sealed class EditorCodeWorkspaceAutomationPrepared(
    EditorCodeWorkspaceAutomationWorkspace workspace,
    EditorCodeWorkspacePreparedOpen open) : IDisposable
{
    internal EditorCodeWorkspaceAutomationWorkspace Workspace { get; } =
        workspace ?? throw new ArgumentNullException(nameof(workspace));

    internal EditorCodeWorkspacePreparedOpen Open { get; } =
        open ?? throw new ArgumentNullException(nameof(open));

    public void Dispose()
    {
        Open.Dispose();
    }
}

internal readonly record struct EditorCodeWorkspaceOpenResult(
    bool Success,
    ExternalCodeEditorKind EditorKind,
    string? ProjectPath,
    string? SolutionPath,
    string? OpenedTarget,
    bool ProjectGenerated,
    bool SolutionGenerated,
    string Diagnostic)
{
    public static EditorCodeWorkspaceOpenResult Failed(
        string diagnostic,
        EditorCSharpProjectResolution resolution = default)
    {
        return new EditorCodeWorkspaceOpenResult(
            false,
            default,
            resolution.ProjectPath,
            resolution.SolutionPath,
            null,
            resolution.ProjectGenerated,
            resolution.SolutionGenerated,
            diagnostic);
    }
}

internal readonly record struct EditorCSharpProjectResolution(
    string ProjectPath,
    string SolutionPath,
    string WorkspaceTarget,
    bool ProjectGenerated,
    bool SolutionGenerated);

internal sealed record EditorCSharpProjectPlan(
    EditorCSharpProjectResolution Resolution,
    string? ProjectContents,
    string? SolutionContents);

/// <summary>
/// C# 工程文件解析器。用户文件只读；仅带 ownership marker 的 Editor 生成文件允许刷新。
/// </summary>
internal static class EditorCodeWorkspaceFiles
{
    internal const string OwnershipMarker = "PixelEngine Editor generated C# workspace";
    private static readonly string[] ScriptReferenceAssemblyNames =
    [
        "PixelEngine.Audio",
        "PixelEngine.Content",
        "PixelEngine.Core",
        "PixelEngine.Gui",
        "PixelEngine.Hosting",
        "PixelEngine.Interop",
        "PixelEngine.Physics",
        "PixelEngine.Rendering",
        "PixelEngine.Scripting",
        "PixelEngine.Serialization",
        "PixelEngine.Simulation",
        "PixelEngine.UI",
        "PixelEngine.World",
    ];

    public static string ResolveReferenceAssemblyDirectory()
    {
        string packagedReferences = Path.Combine(AppContext.BaseDirectory, "ScriptReferenceAssemblies");
        return Directory.Exists(packagedReferences) ? packagedReferences : AppContext.BaseDirectory;
    }

    public static EditorCSharpProjectResolution ResolveOrGenerate(
        EditorProject project,
        string referenceAssemblyDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        EditorCSharpProjectPlan plan = CreatePlan(
            project.ProjectRoot,
            project.Name,
            project.ScriptSourcePath,
            referenceAssemblyDirectory);
        ApplyPlan(plan);
        return plan.Resolution;
    }

    internal static EditorCSharpProjectPlan CreatePlan(
        string projectRoot,
        string projectName,
        string scriptSourcePath,
        string referenceAssemblyDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceAssemblyDirectory);
        string root = Path.GetFullPath(projectRoot);
        string safeName = SanitizeFileName(projectName);
        string? existingProject = FindExistingProject(root, projectName, safeName);
        bool projectGenerated = existingProject is null;
        string projectPath = existingProject ?? SelectOwnedPath(
            Path.Combine(root, safeName + ".Scripts.csproj"),
            Path.Combine(root, safeName + ".PixelEngine.Scripts.csproj"));
        string? projectContents = projectGenerated
            ? BuildGeneratedProject(root, scriptSourcePath, projectPath, referenceAssemblyDirectory)
            : null;

        string? existingSolution = FindContainingSolution(root, projectPath);
        bool solutionGenerated = existingSolution is null;
        string solutionPath = existingSolution ?? SelectOwnedPath(
            Path.Combine(root, Path.GetFileNameWithoutExtension(projectPath) + ".sln"),
            Path.Combine(root, Path.GetFileNameWithoutExtension(projectPath) + ".PixelEngine.sln"));
        string? solutionContents = solutionGenerated
            ? BuildGeneratedSolution(projectPath, solutionPath)
            : null;

        return new EditorCSharpProjectPlan(
            new EditorCSharpProjectResolution(
                Path.GetFullPath(projectPath),
                Path.GetFullPath(solutionPath),
                ResolveVsCodeWorkspaceTarget(root, safeName),
                projectGenerated,
                solutionGenerated),
            projectContents,
            solutionContents);
    }

    internal static void ApplyPlan(EditorCSharpProjectPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ProjectContents is { } projectContents)
        {
            WriteOwnedFile(plan.Resolution.ProjectPath, projectContents);
        }

        if (plan.SolutionContents is { } solutionContents)
        {
            WriteOwnedFile(plan.Resolution.SolutionPath, solutionContents);
        }
    }

    public static string ResolveVsCodeWorkspaceTarget(string projectRoot, string? preferredName = null)
    {
        string root = Path.GetFullPath(projectRoot);
        string[] workspaces = Directory.Exists(root)
            ? Directory.GetFiles(root, "*.code-workspace", SearchOption.TopDirectoryOnly)
            : [];
        if (workspaces.Length == 0)
        {
            return root;
        }

        Array.Sort(workspaces, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            string comparableName = ComparableName(preferredName);
            string? preferred = workspaces.FirstOrDefault(path =>
                string.Equals(ComparableName(Path.GetFileNameWithoutExtension(path)), comparableName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return Path.GetFullPath(preferred);
            }
        }

        return Path.GetFullPath(workspaces[0]);
    }

    private static string? FindExistingProject(
        string projectRoot,
        string projectName,
        string safeName)
    {
        string[] candidates =
        [
            .. Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                .Where(static path => !IsOwnedFile(path))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
        ];
        if (candidates.Length == 0)
        {
            return null;
        }

        string wanted = ComparableName(projectName);
        string? named = candidates.FirstOrDefault(path =>
            string.Equals(ComparableName(Path.GetFileNameWithoutExtension(path)), wanted, StringComparison.OrdinalIgnoreCase));
        named ??= candidates.FirstOrDefault(path =>
            string.Equals(ComparableName(Path.GetFileNameWithoutExtension(path)), ComparableName(safeName), StringComparison.OrdinalIgnoreCase));
        return named ?? (candidates.Length == 1 ? candidates[0] : null);
    }

    private static string? FindContainingSolution(string projectRoot, string projectPath)
    {
        DirectoryInfo? directory = new(projectRoot);
        while (directory is not null)
        {
            string[] solutions;
            try
            {
                solutions =
                [
                    .. Directory.GetFiles(directory.FullName, "*.sln", SearchOption.TopDirectoryOnly),
                    .. Directory.GetFiles(directory.FullName, "*.slnx", SearchOption.TopDirectoryOnly),
                ];
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
            {
                solutions = [];
            }

            Array.Sort(solutions, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < solutions.Length; i++)
            {
                if (SolutionContainsProject(solutions[i], projectPath))
                {
                    return Path.GetFullPath(solutions[i]);
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool SolutionContainsProject(string solutionPath, string projectPath)
    {
        if (string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return SlnxContainsProject(solutionPath, projectPath);
        }

        string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        try
        {
            foreach (string line in File.ReadLines(solutionPath))
            {
                if (!line.StartsWith("Project(", StringComparison.Ordinal) ||
                    !line.Contains(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] quoted = line.Split('"');
                if (quoted.Length < 6)
                {
                    continue;
                }

                string referenced = quoted[5].Replace('\\', Path.DirectorySeparatorChar);
                string resolved = Path.GetFullPath(Path.Combine(solutionDirectory, referenced));
                if (string.Equals(resolved, Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }

        return false;
    }

    private static bool SlnxContainsProject(string solutionPath, string projectPath)
    {
        string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        try
        {
            XDocument document = XDocument.Load(solutionPath, LoadOptions.None);
            return document.Descendants("Project")
                .Select(static project => project.Attribute("Path")?.Value)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Any(path => string.Equals(
                    Path.GetFullPath(Path.Combine(solutionDirectory, path!.Replace('\\', Path.DirectorySeparatorChar))),
                    Path.GetFullPath(projectPath),
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string BuildGeneratedProject(
        string projectRoot,
        string scriptSourcePath,
        string projectPath,
        string referenceAssemblyDirectory)
    {
        List<string> references = new(ScriptReferenceAssemblyNames.Length);
        for (int i = 0; i < ScriptReferenceAssemblyNames.Length; i++)
        {
            string assemblyPath = Path.Combine(referenceAssemblyDirectory, ScriptReferenceAssemblyNames[i] + ".dll");
            string documentationPath = Path.Combine(referenceAssemblyDirectory, ScriptReferenceAssemblyNames[i] + ".xml");
            if (!File.Exists(assemblyPath) || !File.Exists(documentationPath))
            {
                throw new InvalidOperationException(
                    $"Editor 发行目录缺少脚本 reference assembly 或 XML 文档：{ScriptReferenceAssemblyNames[i]} ({referenceAssemblyDirectory})");
            }

            references.Add(assemblyPath);
        }

        string projectDirectory = Path.GetDirectoryName(projectPath) ?? projectRoot;
        string scriptGlob = Path.GetRelativePath(projectDirectory, scriptSourcePath)
            .Replace('\\', '/')
            .TrimEnd('/') + "/**/*.cs";
        XDocument document = new(
            new XComment(" " + OwnershipMarker + " "),
            new XElement("Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net10.0"),
                    new XElement("OutputType", "Library"),
                    new XElement("LangVersion", "14"),
                    new XElement("Nullable", "enable"),
                    new XElement("ImplicitUsings", "enable"),
                    new XElement("EnableDefaultCompileItems", "false"),
                    new XElement("GenerateDocumentationFile", "true")),
                new XElement("ItemGroup",
                    new XElement("Compile", new XAttribute("Include", scriptGlob))),
                new XElement("ItemGroup",
                    references.Select(path =>
                        new XElement("Reference",
                            new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                            new XElement("HintPath", Path.GetFullPath(path)),
                            new XElement("Private", "false"))))));
        return document.Declaration + document.ToString(SaveOptions.None) + Environment.NewLine;
    }

    private static string BuildGeneratedSolution(string projectPath, string solutionPath)
    {
        string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        string relativeProject = Path.GetRelativePath(solutionDirectory, projectPath).Replace('/', '\\');
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        string guid = CreateStableGuid(Path.GetFullPath(projectPath)).ToString("D").ToUpperInvariant();
        return string.Join(
            Environment.NewLine,
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "# Visual Studio Version 17",
            $"# {OwnershipMarker}",
            "VisualStudioVersion = 17.0.31903.59",
            "MinimumVisualStudioVersion = 10.0.40219.1",
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{relativeProject}\", \"{{{guid}}}\"",
            "EndProject",
            "Global",
            "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution",
            "\t\tDebug|Any CPU = Debug|Any CPU",
            "\t\tRelease|Any CPU = Release|Any CPU",
            "\tEndGlobalSection",
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
            $"\t\t{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            $"\t\t{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            $"\t\t{{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
            $"\t\t{{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU",
            "\tEndGlobalSection",
            "EndGlobal",
            string.Empty);
    }

    private static string SelectOwnedPath(string preferred, string fallback)
    {
        if (!File.Exists(preferred) || IsOwnedFile(preferred))
        {
            return preferred;
        }

        if (!File.Exists(fallback) || IsOwnedFile(fallback))
        {
            return fallback;
        }

        string directory = Path.GetDirectoryName(fallback) ?? Directory.GetCurrentDirectory();
        string stem = Path.GetFileNameWithoutExtension(fallback);
        string extension = Path.GetExtension(fallback);
        for (int suffix = 2; suffix < 10_000; suffix++)
        {
            string candidate = Path.Combine(directory, $"{stem}.{suffix}{extension}");
            if (!File.Exists(candidate) || IsOwnedFile(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"无法为 Editor C# 工程分配安全文件名：{fallback}");
    }

    private static void WriteOwnedFile(string path, string content)
    {
        if (File.Exists(path))
        {
            if (!IsOwnedFile(path))
            {
                throw new InvalidOperationException($"拒绝覆盖用户维护的 IDE 文件：{path}");
            }

            if (string.Equals(File.ReadAllText(path, Encoding.UTF8), content, StringComparison.Ordinal))
            {
                return;
            }
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool IsOwnedFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            char[] buffer = new char[4096];
            int count = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, count).Contains(OwnershipMarker, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        StringBuilder value = new();
        foreach (char character in name.Trim())
        {
            _ = value.Append(Path.GetInvalidFileNameChars().Contains(character) || char.IsWhiteSpace(character) ? '_' : character);
        }

        string result = value.ToString().TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(result))
        {
            return "PixelEngineGame";
        }

        string deviceName = result.Split('.', 2)[0];
        bool reserved = deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (deviceName.Length == 4 &&
                (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                deviceName[3] is >= '1' and <= '9');
        return reserved ? "_" + result : result;
    }

    private static string ComparableName(string value)
    {
        return new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
    }

    private static Guid CreateStableGuid(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
