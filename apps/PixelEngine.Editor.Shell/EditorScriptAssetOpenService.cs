using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 在外部 IDE 中打开 .cs 脚本资产。
/// </summary>
internal sealed class EditorScriptAssetOpenService
{
    private const string FilePlaceholder = "{file}";
    private readonly EditorAssetManifestStore? _assets;
    private readonly EditorProject? _project;
    private readonly string _projectRoot;
    private readonly string _contentRoot;
    private readonly Func<string> _editorCommandProvider;
    private readonly IExternalScriptEditorProcessLauncher _processLauncher;

    /// <summary>
    /// 兼容基于 content manifest 的旧 Project Window opener。
    /// </summary>
    public EditorScriptAssetOpenService(
        EditorAssetManifestStore assets,
        Func<string>? editorCommandProvider = null,
        IExternalScriptEditorProcessLauncher? processLauncher = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _projectRoot = assets.ProjectRoot;
        _contentRoot = assets.ContentRoot;
        _editorCommandProvider = editorCommandProvider ?? (static () => string.Empty);
        _processLauncher = processLauncher ?? new ExternalScriptEditorProcessLauncher();
    }

    /// <summary>
    /// 生产 Project Window 双根 opener；直接验证 Content/ 或 ScriptSource/ 下的真实 .cs 文件。
    /// </summary>
    public EditorScriptAssetOpenService(
        EditorProject project,
        Func<string>? editorCommandProvider = null,
        IExternalScriptEditorProcessLauncher? processLauncher = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _projectRoot = project.ProjectRoot;
        _contentRoot = project.ContentRootPath;
        _editorCommandProvider = editorCommandProvider ?? (static () => string.Empty);
        _processLauncher = processLauncher ?? new ExternalScriptEditorProcessLauncher();
    }

    public bool TryOpenScriptAsset(string logicalPath, out string diagnostic)
    {
        EditorScriptAssetOpenResult result = OpenScriptAsset(logicalPath);
        diagnostic = result.Diagnostic;
        return result.Success;
    }

    public EditorScriptAssetOpenResult OpenScriptAsset(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        try
        {
            return _project is null
                ? OpenManifestScriptAsset(logicalPath)
                : OpenRootedScriptAsset(logicalPath);
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            return Failed(logicalPath, $"脚本资产打开失败：{ex.Message}");
        }
    }

    private EditorScriptAssetOpenResult OpenManifestScriptAsset(string logicalPath)
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
            : OpenResolvedScript(asset.Id, asset.LogicalPath, fullPath);
    }

    private EditorScriptAssetOpenResult OpenRootedScriptAsset(string rootedPath)
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
            : OpenResolvedScript(string.Empty, canonicalPath, fullPath);
    }

    private EditorScriptAssetOpenResult OpenResolvedScript(
        string assetId,
        string logicalPath,
        string fullPath)
    {
        string editorCommand = _editorCommandProvider()?.Trim() ?? string.Empty;
        bool useSystemDefault = IsSystemDefault(editorCommand);
        if (!TryCreateStartInfo(editorCommand, fullPath, useSystemDefault, out ProcessStartInfo? startInfo, out string diagnostic) ||
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
            : $"已用外部脚本编辑器打开脚本：{logicalPath}";
        return new EditorScriptAssetOpenResult(
            Success: true,
            AssetId: assetId,
            LogicalPath: logicalPath,
            ResolvedPath: fullPath,
            EditorCommand: useSystemDefault ? "system-default" : editorCommand,
            UsedSystemDefault: useSystemDefault,
            Diagnostic: success);
    }

    private bool TryCreateStartInfo(
        string editorCommand,
        string fullPath,
        bool useSystemDefault,
        out ProcessStartInfo? startInfo,
        out string diagnostic)
    {
        if (useSystemDefault)
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

        string[] tokens = SplitCommandLine(editorCommand, out diagnostic);
        if (tokens.Length == 0)
        {
            startInfo = null;
            diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? "外部脚本编辑器配置为空或无效。"
                : diagnostic;
            return false;
        }

        bool hasPlaceholder = editorCommand.Contains(FilePlaceholder, StringComparison.Ordinal);
        startInfo = new ProcessStartInfo
        {
            FileName = ReplaceFilePlaceholder(tokens[0], fullPath),
            UseShellExecute = false,
            WorkingDirectory = _projectRoot,
        };
        for (int i = 1; i < tokens.Length; i++)
        {
            startInfo.ArgumentList.Add(ReplaceFilePlaceholder(tokens[i], fullPath));
        }

        if (!hasPlaceholder)
        {
            startInfo.ArgumentList.Add(fullPath);
        }

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

    private static bool IsSystemDefault(string editorCommand)
    {
        return string.IsNullOrWhiteSpace(editorCommand) ||
            string.Equals(editorCommand.Trim(), "system-default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(editorCommand.Trim(), "default", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReplaceFilePlaceholder(string value, string fullPath)
    {
        return value.Replace(FilePlaceholder, fullPath, StringComparison.Ordinal);
    }

    private static string[] SplitCommandLine(string command, out string diagnostic)
    {
        diagnostic = string.Empty;
        List<string> tokens = [];
        StringBuilder current = new();
        char quote = '\0';
        for (int i = 0; i < command.Length; i++)
        {
            char ch = command[i];
            if (ch is '"' or '\'')
            {
                if (quote == '\0')
                {
                    quote = ch;
                    continue;
                }

                if (quote == ch)
                {
                    quote = '\0';
                    continue;
                }
            }

            if (char.IsWhiteSpace(ch) && quote == '\0')
            {
                AddToken(tokens, current);
                continue;
            }

            _ = current.Append(ch);
        }

        if (quote != '\0')
        {
            diagnostic = "外部脚本编辑器配置包含未闭合引号。";
            return [];
        }

        AddToken(tokens, current);
        return [.. tokens];
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        _ = current.Clear();
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
