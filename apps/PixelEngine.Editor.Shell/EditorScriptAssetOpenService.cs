using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 在外部 IDE 中打开 .cs 脚本资产。
/// </summary>
internal sealed class EditorScriptAssetOpenService(
    EditorAssetManifestStore assets,
    Func<ProjectSettingsDto>? settingsProvider = null,
    IExternalScriptEditorProcessLauncher? processLauncher = null)
{
    private const string FilePlaceholder = "{file}";
    private readonly EditorAssetManifestStore _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly Func<ProjectSettingsDto> _settingsProvider = settingsProvider ?? (() => EngineProjectSettingsStore.LoadProjectSettings(assets.ProjectRoot));
    private readonly IExternalScriptEditorProcessLauncher _processLauncher = processLauncher ?? new ExternalScriptEditorProcessLauncher();

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
            if (!_assets.TryResolveLogicalPath(logicalPath, out EditorAssetRecord asset))
            {
                return Failed(logicalPath, $"脚本资产不存在或未登记：{logicalPath}");
            }

            if (asset.AssetType != EditorAssetType.Script)
            {
                return Failed(asset, $"资产不是 script 类型，拒绝外部打开：{asset.LogicalPath} ({asset.AssetType})。");
            }

            string fullPath = ResolveFullPath(asset);
            if (!File.Exists(fullPath))
            {
                return Failed(asset, $"脚本文件不存在：{fullPath}", fullPath);
            }

            ProjectSettingsDto settings = _settingsProvider().Normalize();
            string editorCommand = settings.EditorPreferences.ExternalScriptEditor?.Trim() ?? string.Empty;
            bool useSystemDefault = IsSystemDefault(editorCommand);
            if (!TryCreateStartInfo(editorCommand, fullPath, useSystemDefault, out ProcessStartInfo? startInfo, out string diagnostic) ||
                startInfo is null)
            {
                return Failed(asset, diagnostic, fullPath, editorCommand, useSystemDefault);
            }

            if (!_processLauncher.Start(startInfo, out string launcherDiagnostic))
            {
                string message = string.IsNullOrWhiteSpace(launcherDiagnostic)
                    ? $"启动外部脚本编辑器失败：{asset.LogicalPath}。"
                    : $"启动外部脚本编辑器失败：{asset.LogicalPath}。{launcherDiagnostic}";
                return Failed(asset, message, fullPath, editorCommand, useSystemDefault);
            }

            string success = useSystemDefault
                ? $"已用系统默认 opener 打开脚本：{asset.LogicalPath}"
                : $"已用外部脚本编辑器打开脚本：{asset.LogicalPath}";
            return new EditorScriptAssetOpenResult(
                Success: true,
                AssetId: asset.Id,
                LogicalPath: asset.LogicalPath,
                ResolvedPath: fullPath,
                EditorCommand: useSystemDefault ? "system-default" : editorCommand,
                UsedSystemDefault: useSystemDefault,
                Diagnostic: success);
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            return Failed(logicalPath, $"脚本资产打开失败：{ex.Message}");
        }
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
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? _assets.ContentRoot,
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
            WorkingDirectory = _assets.ProjectRoot,
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

    private string ResolveFullPath(EditorAssetRecord asset)
    {
        string contentRoot = Path.GetFullPath(_assets.ContentRoot);
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
        return new EditorScriptAssetOpenResult(
            false,
            asset.Id,
            asset.LogicalPath,
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
