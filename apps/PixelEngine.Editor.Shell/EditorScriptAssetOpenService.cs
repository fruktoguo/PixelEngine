using System.ComponentModel;
using System.Diagnostics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 在外部 IDE 中打开 .cs 脚本资产。
/// </summary>
internal sealed class EditorScriptAssetOpenService
{
    private const string FilePlaceholder = "{file}";
    private const string LinePlaceholder = "{line}";
    private const string ColumnPlaceholder = "{column}";
    private const string ProjectPlaceholder = "{project}";
    private readonly EditorAssetManifestStore? _assets;
    private readonly EditorProject? _project;
    private readonly string _projectRoot;
    private readonly string _contentRoot;
    private readonly Func<string> _editorCommandProvider;
    private readonly IExternalScriptEditorProcessLauncher _processLauncher;
    private readonly IExternalCodeEditorLocator _editorLocator;

    /// <summary>
    /// 兼容基于 content manifest 的旧 Project Window opener。
    /// </summary>
    public EditorScriptAssetOpenService(
        EditorAssetManifestStore assets,
        Func<string>? editorCommandProvider = null,
        IExternalScriptEditorProcessLauncher? processLauncher = null,
        IExternalCodeEditorLocator? editorLocator = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _projectRoot = assets.ProjectRoot;
        _contentRoot = assets.ContentRoot;
        _editorCommandProvider = editorCommandProvider ?? (static () => string.Empty);
        _processLauncher = processLauncher ?? new ExternalScriptEditorProcessLauncher();
        _editorLocator = editorLocator ?? new ExternalCodeEditorLocator();
    }

    /// <summary>
    /// 生产 Project Window 双根 opener；直接验证 Content/ 或 ScriptSource/ 下的真实 .cs 文件。
    /// </summary>
    public EditorScriptAssetOpenService(
        EditorProject project,
        Func<string>? editorCommandProvider = null,
        IExternalScriptEditorProcessLauncher? processLauncher = null,
        IExternalCodeEditorLocator? editorLocator = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _projectRoot = project.ProjectRoot;
        _contentRoot = project.ContentRootPath;
        _editorCommandProvider = editorCommandProvider ?? (static () => string.Empty);
        _processLauncher = processLauncher ?? new ExternalScriptEditorProcessLauncher();
        _editorLocator = editorLocator ?? new ExternalCodeEditorLocator();
    }

    public bool TryOpenScriptAsset(string logicalPath, out string diagnostic)
    {
        EditorScriptAssetOpenResult result = OpenScriptAsset(logicalPath);
        diagnostic = result.Diagnostic;
        return result.Success;
    }

    public EditorScriptAssetOpenResult OpenScriptAsset(string logicalPath)
    {
        return OpenScriptAsset(logicalPath, line: 1, column: 1);
    }

    /// <summary>
    /// 打开脚本并定位到一基行列；Project Window 使用 1:1，Console 使用编译诊断的真实位置。
    /// </summary>
    public EditorScriptAssetOpenResult OpenScriptAsset(string logicalPath, int line, int column = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        line = Math.Max(1, line);
        column = Math.Max(1, column);
        try
        {
            return _project is null
                ? OpenManifestScriptAsset(logicalPath, line, column)
                : OpenRootedScriptAsset(logicalPath, line, column);
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            return Failed(logicalPath, $"脚本资产打开失败：{ex.Message}");
        }
    }

    private EditorScriptAssetOpenResult OpenManifestScriptAsset(string logicalPath, int line, int column)
    {
        EditorAssetManifestStore assets = _assets ??
            throw new InvalidOperationException("legacy script opener 缺少 asset manifest。");
        if (!assets.TryResolveLogicalPath(logicalPath, out EditorAssetRecord asset))
        {
            return Failed(logicalPath, $"脚本资产不存在或未登记：{logicalPath}");
        }

        if (asset.AssetType != EditorAssetType.Script)
        {
            return Failed(asset, $"资产不是 script 类型，拒绝外部打开：{asset.LogicalPath} ({asset.AssetType})。");
        }

        string fullPath = ResolveLegacyFullPath(asset);
        return !File.Exists(fullPath)
            ? Failed(asset, $"脚本文件不存在：{fullPath}", fullPath)
            : OpenResolvedScript(asset.Id, asset.LogicalPath, fullPath, line, column);
    }

    private EditorScriptAssetOpenResult OpenRootedScriptAsset(string rootedPath, int line, int column)
    {
        EditorProject project = _project ??
            throw new InvalidOperationException("rooted script opener 缺少 EditorProject。");
        if (!EditorRootedBrowserPath.TryParse(rootedPath, out EditorAssetPath path, out string diagnostic))
        {
            return Failed(rootedPath, diagnostic);
        }

        string canonicalPath = EditorRootedBrowserPath.Format(path);
        if (!string.Equals(Path.GetExtension(path.RelativePath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(canonicalPath, $"外部脚本编辑器仅支持 .cs 文件：{canonicalPath}");
        }

        string fullPath = EditorRootedBrowserPath.ResolveFullPath(
            path,
            project.ContentRootPath,
            project.ScriptSourcePath);
        return !File.Exists(fullPath)
            ? Failed(string.Empty, canonicalPath, $"脚本文件不存在：{fullPath}", fullPath)
            : OpenResolvedScript(string.Empty, canonicalPath, fullPath, line, column);
    }

    private EditorScriptAssetOpenResult OpenResolvedScript(
        string assetId,
        string logicalPath,
        string fullPath,
        int line,
        int column)
    {
        string editorCommand = ExternalCodeEditorPreference.Normalize(_editorCommandProvider());
        ExternalCodeEditorKind editorKind = ExternalCodeEditorPreference.Classify(editorCommand);
        bool useSystemDefault = editorKind == ExternalCodeEditorKind.SystemDefault;
        if (!TryCreateStartInfo(editorCommand, editorKind, fullPath, line, column, out ProcessStartInfo? startInfo, out string diagnostic) ||
            startInfo is null)
        {
            return Failed(assetId, logicalPath, diagnostic, fullPath, editorCommand, useSystemDefault);
        }

        if (!_processLauncher.Start(startInfo, out string launcherDiagnostic))
        {
            string message = string.IsNullOrWhiteSpace(launcherDiagnostic)
                ? $"启动外部脚本编辑器失败：{logicalPath}。"
                : $"启动外部脚本编辑器失败：{logicalPath}。{launcherDiagnostic}";
            return Failed(assetId, logicalPath, message, fullPath, editorCommand, useSystemDefault);
        }

        string success = useSystemDefault
            ? $"已用系统默认 opener 打开脚本：{logicalPath}"
            : $"已用外部脚本编辑器打开脚本：{logicalPath}:{line}:{column}";
        return new EditorScriptAssetOpenResult(
            Success: true,
            AssetId: assetId,
            LogicalPath: logicalPath,
            ResolvedPath: fullPath,
            EditorCommand: useSystemDefault ? ExternalCodeEditorPreference.SystemDefault : editorCommand,
            UsedSystemDefault: useSystemDefault,
            Diagnostic: success);
    }

    private bool TryCreateStartInfo(
        string editorCommand,
        ExternalCodeEditorKind editorKind,
        string fullPath,
        int line,
        int column,
        out ProcessStartInfo? startInfo,
        out string diagnostic)
    {
        if (editorKind == ExternalCodeEditorKind.SystemDefault)
        {
            startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                WorkingDirectory = _project is null
                    ? _contentRoot
                    : Path.GetDirectoryName(fullPath) ?? _contentRoot,
            };
            diagnostic = string.Empty;
            return true;
        }

        if (editorKind != ExternalCodeEditorKind.Custom)
        {
            return TryCreatePresetStartInfo(editorKind, fullPath, line, column, out startInfo, out diagnostic);
        }

        string[] tokens = ExternalCodeEditorCommandLine.Split(editorCommand, out diagnostic);
        if (tokens.Length == 0)
        {
            startInfo = null;
            diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? "外部脚本编辑器配置为空或无效。"
                : diagnostic;
            return false;
        }

        bool hasPlaceholder = editorCommand.Contains(FilePlaceholder, StringComparison.Ordinal);
        string executableCommand = ReplacePlaceholders(tokens[0], fullPath, line, column);
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
            arguments.Add(ReplacePlaceholders(tokens[i], fullPath, line, column));
        }

        if (!hasPlaceholder)
        {
            arguments.Add(fullPath);
        }

        startInfo = ExternalCodeEditorProcess.CreateStartInfo(executable, arguments, _projectRoot);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryCreatePresetStartInfo(
        ExternalCodeEditorKind kind,
        string fullPath,
        int line,
        int column,
        out ProcessStartInfo? startInfo,
        out string diagnostic)
    {
        if (!_editorLocator.TryLocate(kind, out ExternalCodeEditorInstallation installation, out diagnostic))
        {
            startInfo = null;
            return false;
        }

        EditorCSharpProjectResolution resolution;
        try
        {
            EditorProject? project = _project;
            resolution = project is null
                ? default
                : EditorCodeWorkspaceFiles.ResolveOrGenerate(
                    project,
                    EditorCodeWorkspaceFiles.ResolveReferenceAssemblyDirectory());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            startInfo = null;
            diagnostic = $"准备脚本 IDE 工程上下文失败：{exception.Message}";
            return false;
        }

        string workspaceTarget = resolution.WorkspaceTarget ?? EditorCodeWorkspaceFiles.ResolveVsCodeWorkspaceTarget(_projectRoot);
        string solutionTarget = resolution.SolutionPath ?? _projectRoot;
        List<string> arguments = kind switch
        {
            ExternalCodeEditorKind.VsCode =>
            [
                "--reuse-window",
                workspaceTarget,
                "--goto",
                $"{fullPath}:{line}:{column}",
            ],
            ExternalCodeEditorKind.VisualStudio => [solutionTarget, "/Edit", fullPath, "/Command", $"Edit.Goto {line}"],
            ExternalCodeEditorKind.Rider => [solutionTarget, "--line", line.ToString(System.Globalization.CultureInfo.InvariantCulture), "--column", column.ToString(System.Globalization.CultureInfo.InvariantCulture), fullPath],
            ExternalCodeEditorKind.SystemDefault or ExternalCodeEditorKind.Custom =>
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "该编辑器类型不使用 preset 脚本启动路径。"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知外部代码编辑器类型。"),
        };
        startInfo = ExternalCodeEditorProcess.CreateStartInfo(
            installation.ExecutablePath,
            arguments,
            _projectRoot);
        diagnostic = string.Empty;
        return true;
    }

    private string ResolveLegacyFullPath(EditorAssetRecord asset)
    {
        string contentRoot = Path.GetFullPath(_contentRoot);
        string fullPath = Path.GetFullPath(Path.Combine(contentRoot, asset.LogicalPath));
        string rootWithSeparator = contentRoot.EndsWith(Path.DirectorySeparatorChar)
            ? contentRoot
            : contentRoot + Path.DirectorySeparatorChar;
        bool insideContentRoot = string.Equals(fullPath, contentRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        return insideContentRoot
            ? fullPath
            : throw new InvalidOperationException($"资产路径越过 content 根目录：{asset.LogicalPath}");
    }

    private string ReplacePlaceholders(string value, string fullPath, int line, int column)
    {
        return value
            .Replace(FilePlaceholder, fullPath, StringComparison.Ordinal)
            .Replace(LinePlaceholder, line.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(ColumnPlaceholder, column.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(ProjectPlaceholder, _projectRoot, StringComparison.Ordinal);
    }

    private static EditorScriptAssetOpenResult Failed(string logicalPath, string diagnostic)
    {
        return new EditorScriptAssetOpenResult(false, string.Empty, logicalPath, null, null, false, diagnostic);
    }

    private static EditorScriptAssetOpenResult Failed(
        EditorAssetRecord asset,
        string diagnostic,
        string? resolvedPath = null,
        string? editorCommand = null,
        bool usedSystemDefault = false)
    {
        return Failed(
            asset.Id,
            asset.LogicalPath,
            diagnostic,
            resolvedPath,
            editorCommand,
            usedSystemDefault);
    }

    private static EditorScriptAssetOpenResult Failed(
        string assetId,
        string logicalPath,
        string diagnostic,
        string? resolvedPath = null,
        string? editorCommand = null,
        bool usedSystemDefault = false)
    {
        return new EditorScriptAssetOpenResult(
            false,
            assetId,
            logicalPath,
            resolvedPath,
            editorCommand,
            usedSystemDefault,
            diagnostic);
    }
}

/// <summary>
/// 外部脚本编辑器进程启动器接口。
/// </summary>
internal interface IExternalScriptEditorProcessLauncher
{
    bool Start(ProcessStartInfo startInfo, out string diagnostic);
}

/// <summary>
/// 默认的外部脚本编辑器进程启动实现。
/// </summary>
internal sealed class ExternalScriptEditorProcessLauncher : IExternalScriptEditorProcessLauncher
{
    public bool Start(ProcessStartInfo startInfo, out string diagnostic)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            diagnostic = process is null ? "Process.Start 返回 null。" : string.Empty;
            return process is not null;
        }
        catch (Win32Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            diagnostic = ex.Message;
            return false;
        }
        catch (FileNotFoundException ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }
}

/// <summary>
/// EditorScriptAssetOpenResult 数据结构。
/// </summary>
internal sealed record EditorScriptAssetOpenResult(
    bool Success,
    string AssetId,
    string LogicalPath,
    string? ResolvedPath,
    string? EditorCommand,
    bool UsedSystemDefault,
    string Diagnostic);
