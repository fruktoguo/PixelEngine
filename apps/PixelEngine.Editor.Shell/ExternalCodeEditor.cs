using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 外部代码编辑器的稳定偏好值；preset 与自定义命令共享同一个持久化字段。
/// </summary>
internal static class ExternalCodeEditorPreference
{
    public const string VsCode = "vscode";
    public const string VisualStudio = "visual-studio";
    public const string Rider = "rider";
    public const string SystemDefault = "system-default";

    public static string Normalize(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(normalized) ? VsCode : normalized;
    }

    public static ExternalCodeEditorKind Classify(string? value)
    {
        string normalized = Normalize(value);
        if (string.Equals(normalized, VsCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "code", StringComparison.OrdinalIgnoreCase))
        {
            return ExternalCodeEditorKind.VsCode;
        }

        bool isVisualStudio = string.Equals(normalized, VisualStudio, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "visualstudio", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "devenv", StringComparison.OrdinalIgnoreCase);
        return isVisualStudio
            ? ExternalCodeEditorKind.VisualStudio
            : string.Equals(normalized, Rider, StringComparison.OrdinalIgnoreCase)
            ? ExternalCodeEditorKind.Rider
            : string.Equals(normalized, SystemDefault, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase)
                ? ExternalCodeEditorKind.SystemDefault
                : ExternalCodeEditorKind.Custom;
    }
}

internal enum ExternalCodeEditorKind
{
    VsCode,
    VisualStudio,
    Rider,
    SystemDefault,
    Custom,
}

internal readonly record struct ExternalCodeEditorInstallation(
    ExternalCodeEditorKind Kind,
    string DisplayName,
    string ExecutablePath);

internal interface IExternalCodeEditorLocator
{
    bool TryLocate(ExternalCodeEditorKind kind, out ExternalCodeEditorInstallation installation, out string diagnostic);

    bool TryResolveCustomExecutable(string command, out string executablePath, out string diagnostic);
}

/// <summary>
/// Windows-first IDE 探测器；按 PATH、标准安装目录、installer 工具和注册表定位真实 executable。
/// </summary>
internal sealed class ExternalCodeEditorLocator(IExternalCodeEditorDiscoveryEnvironment? environment = null) : IExternalCodeEditorLocator
{
    private readonly IExternalCodeEditorDiscoveryEnvironment _environment = environment ?? new ExternalCodeEditorDiscoveryEnvironment();

    public bool TryLocate(
        ExternalCodeEditorKind kind,
        out ExternalCodeEditorInstallation installation,
        out string diagnostic)
    {
        IEnumerable<string?> candidates = kind switch
        {
            ExternalCodeEditorKind.VsCode => EnumerateVsCodeCandidates(),
            ExternalCodeEditorKind.VisualStudio => EnumerateVisualStudioCandidates(),
            ExternalCodeEditorKind.Rider => EnumerateRiderCandidates(),
            ExternalCodeEditorKind.SystemDefault or ExternalCodeEditorKind.Custom => [],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知外部代码编辑器类型。"),
        };
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? rawCandidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(rawCandidate))
            {
                continue;
            }

            string? candidate;
            try
            {
                candidate = NormalizeAutomaticCandidate(kind, rawCandidate);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // PATH 或注册表中的单个损坏候选不能阻断其它安装来源。
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate) &&
                visited.Add(candidate) &&
                _environment.FileExists(candidate))
            {
                installation = new ExternalCodeEditorInstallation(kind, DisplayName(kind), candidate);
                diagnostic = string.Empty;
                return true;
            }
        }

        installation = default;
        diagnostic = $"未找到 {DisplayName(kind)}。请安装后重试，或在 Preferences > External Tools 中选择其他编辑器。";
        return false;
    }

    public bool TryResolveCustomExecutable(
        string command,
        out string executablePath,
        out string diagnostic)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            diagnostic = "自定义外部编辑器 executable 不能为空。";
            return false;
        }

        string candidate;
        if (Path.IsPathRooted(command))
        {
            candidate = command;
        }
        else
        {
            if (command.Contains(Path.DirectorySeparatorChar) ||
                command.Contains(Path.AltDirectorySeparatorChar))
            {
                diagnostic = "自定义外部编辑器使用相对路径时不能包含目录；请填写绝对路径或 PATH 中的命令名。";
                return false;
            }

            candidate = _environment.FindOnPath(command) ?? string.Empty;
        }

        try
        {
            candidate = string.IsNullOrWhiteSpace(candidate) ? string.Empty : Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostic = $"自定义外部编辑器路径无效：{command}";
            return false;
        }

        if (candidate.Length == 0 || !_environment.FileExists(candidate))
        {
            diagnostic = $"未找到自定义外部编辑器 executable：{command}";
            return false;
        }

        executablePath = candidate;
        diagnostic = string.Empty;
        return true;
    }

    private IEnumerable<string?> EnumerateVsCodeCandidates()
    {
        yield return _environment.FindOnPath("code");
        string? localAppData = _environment.GetEnvironmentVariable("LOCALAPPDATA");
        yield return Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
        yield return Combine(localAppData, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe");
        yield return Combine(_environment.GetEnvironmentVariable("ProgramFiles"), "Microsoft VS Code", "Code.exe");
        yield return Combine(_environment.GetEnvironmentVariable("ProgramFiles"), "Microsoft VS Code Insiders", "Code - Insiders.exe");
        yield return Combine(_environment.GetEnvironmentVariable("ProgramFiles(x86)"), "Microsoft VS Code", "Code.exe");
        foreach (string command in _environment.ReadRegistryCommands(ExternalCodeEditorKind.VsCode))
        {
            yield return ExtractExecutable(command);
        }
    }

    private IEnumerable<string?> EnumerateVisualStudioCandidates()
    {
        yield return _environment.FindOnPath("devenv");
        string? vswhere = _environment.FindOnPath("vswhere") ??
            Combine(_environment.GetEnvironmentVariable("ProgramFiles(x86)"), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!string.IsNullOrWhiteSpace(vswhere) && _environment.FileExists(vswhere))
        {
            string output = _environment.Capture(vswhere, "-latest -products * -requires Microsoft.Component.MSBuild -property installationPath");
            foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return Path.Combine(line, "Common7", "IDE", "devenv.exe");
            }
        }

        foreach (string command in _environment.ReadRegistryCommands(ExternalCodeEditorKind.VisualStudio))
        {
            yield return ExtractExecutable(command);
        }
    }

    private IEnumerable<string?> EnumerateRiderCandidates()
    {
        yield return _environment.FindOnPath("rider");
        string? localAppData = _environment.GetEnvironmentVariable("LOCALAPPDATA");
        foreach (string root in new[]
        {
            Combine(localAppData, "Programs") ?? string.Empty,
            Combine(localAppData, "JetBrains", "Toolbox", "apps", "Rider") ?? string.Empty,
        })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (string directory in _environment.EnumerateDirectories(root, "Rider*", SearchOption.AllDirectories))
            {
                yield return Path.Combine(directory, "bin", "rider64.exe");
                yield return Path.Combine(directory, "bin", "rider.exe");
                yield return Path.Combine(directory, "bin", "rider.bat");
            }
        }
    }

    private string? NormalizeAutomaticCandidate(ExternalCodeEditorKind kind, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        bool isCommandScript = string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
        if (!isCommandScript)
        {
            return fullPath;
        }

        if (kind != ExternalCodeEditorKind.VsCode)
        {
            // 自动 IDE 路径必须收敛到真实 executable；脚本 shim 仅允许用户显式自定义。
            return null;
        }

        string? bin = Path.GetDirectoryName(fullPath);
        string? install = bin is null ? null : Path.GetDirectoryName(bin);
        string adjacentExecutable = install is null ? string.Empty : Path.Combine(install, "Code.exe");
        return !string.IsNullOrWhiteSpace(adjacentExecutable) && _environment.FileExists(adjacentExecutable)
            ? adjacentExecutable
            : null;
    }

    private static string DisplayName(ExternalCodeEditorKind kind)
    {
        return kind switch
        {
            ExternalCodeEditorKind.VsCode => "Visual Studio Code",
            ExternalCodeEditorKind.VisualStudio => "Visual Studio",
            ExternalCodeEditorKind.Rider => "JetBrains Rider",
            ExternalCodeEditorKind.SystemDefault => "系统默认程序",
            ExternalCodeEditorKind.Custom => "自定义代码编辑器",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知外部代码编辑器类型。"),
        };
    }

    private static string? Combine(string? root, params string[] segments)
    {
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine([root, .. segments]);
    }

    internal static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string value = command.Trim();
        if (value[0] == '"')
        {
            int closingQuote = value.IndexOf('"', 1);
            return closingQuote > 1 ? value[1..closingQuote] : null;
        }

        int extension = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return extension >= 0 ? value[..(extension + 4)].Trim() : value.Split(' ', 2)[0];
    }
}

internal interface IExternalCodeEditorDiscoveryEnvironment
{
    string? GetEnvironmentVariable(string name);

    string? FindOnPath(string command);

    bool FileExists(string path);

    IEnumerable<string> EnumerateDirectories(string root, string pattern, SearchOption searchOption);

    string Capture(string executable, string arguments);

    IEnumerable<string> ReadRegistryCommands(ExternalCodeEditorKind kind);
}

internal sealed class ExternalCodeEditorDiscoveryEnvironment : IExternalCodeEditorDiscoveryEnvironment
{
    public string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    public string? FindOnPath(string command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string extension in new[] { ".exe", ".cmd", ".bat", string.Empty })
            {
                string candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public IEnumerable<string> EnumerateDirectories(string root, string pattern, SearchOption searchOption)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateDirectories(root, pattern, searchOption)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string Capture(string executable, string arguments)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            if (!process.Start())
            {
                return string.Empty;
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(2_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // 进程恰好在超时边界退出时无需处理。
                }

                return string.Empty;
            }

            _ = stderr.GetAwaiter().GetResult();
            string output = stdout.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    public IEnumerable<string> ReadRegistryCommands(ExternalCodeEditorKind kind)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [];
        }

        string[] subKeys = kind switch
        {
            ExternalCodeEditorKind.VsCode =>
            [
                @"Software\Classes\Applications\Code.exe\shell\open\command",
                @"Software\Classes\vscode\shell\open\command",
                @"Software\Microsoft\Windows\CurrentVersion\App Paths\Code.exe",
            ],
            ExternalCodeEditorKind.VisualStudio =>
            [
                @"Software\Classes\Applications\devenv.exe\shell\open\command",
            ],
            ExternalCodeEditorKind.Rider or ExternalCodeEditorKind.SystemDefault or ExternalCodeEditorKind.Custom => [],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知外部代码编辑器类型。"),
        };
        List<string> values = [];
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (string subKey in subKeys)
                    {
                        using RegistryKey? key = baseKey.OpenSubKey(subKey);
                        if (key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command))
                        {
                            values.Add(command);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 单个 registry hive/view 不可读时继续使用其它安装来源。
                }
            }
        }

        return values;
    }
}

internal static class ExternalCodeEditorProcess
{
    public static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        if (string.Equals(Path.GetExtension(executable), ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(executable), ".bat", StringComparison.OrdinalIgnoreCase))
        {
            ProcessStartInfo commandScript = new()
            {
                FileName = executable,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            for (int i = 0; i < arguments.Count; i++)
            {
                commandScript.ArgumentList.Add(arguments[i]);
            }

            return commandScript;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        for (int i = 0; i < arguments.Count; i++)
        {
            startInfo.ArgumentList.Add(arguments[i]);
        }

        return startInfo;
    }
}

internal static class ExternalCodeEditorCommandLine
{
    public static string[] Split(string command, out string diagnostic)
    {
        diagnostic = string.Empty;
        List<string> tokens = [];
        StringBuilder current = new();
        char quote = '\0';
        bool tokenStarted = false;
        for (int i = 0; i < command.Length; i++)
        {
            char ch = command[i];
            if (ch is '"' or '\'')
            {
                if (quote == '\0')
                {
                    quote = ch;
                    tokenStarted = true;
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
                AddToken(tokens, current, ref tokenStarted);
                continue;
            }

            tokenStarted = true;
            _ = current.Append(ch);
        }

        if (quote != '\0')
        {
            diagnostic = "外部脚本编辑器配置包含未闭合引号。";
            return [];
        }

        AddToken(tokens, current, ref tokenStarted);
        return [.. tokens];
    }

    private static void AddToken(List<string> tokens, StringBuilder current, ref bool tokenStarted)
    {
        if (!tokenStarted)
        {
            return;
        }

        tokens.Add(current.ToString());
        _ = current.Clear();
        tokenStarted = false;
    }
}
