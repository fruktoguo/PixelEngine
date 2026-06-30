using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PixelEngine.Scripting;

internal sealed class IdeLauncher(IIdeLauncherEnvironment? environment = null)
{
    private readonly IIdeLauncherEnvironment _environment = environment ?? new IdeLauncherEnvironment();

    public IReadOnlyList<IdeCandidate> DiscoverInstalledIdes()
    {
        List<IdeCandidate> candidates = [];
        AddRiderCandidates(candidates);
        AddVisualStudioCandidates(candidates);
        AddVsCodeCandidates(candidates);
        return Deduplicate(candidates);
    }

    public IdeLaunchResult OpenSolution(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!_environment.FileExists(solutionPath))
        {
            return IdeLaunchResult.Failed($"解决方案文件不存在：{solutionPath}");
        }

        IReadOnlyList<IdeCandidate> candidates = DiscoverInstalledIdes();
        if (candidates.Count == 0)
        {
            return IdeLaunchResult.Failed("未找到 Rider、Visual Studio 或 VS Code。");
        }

        IdeCandidate candidate = candidates[0];
        ProcessStartInfo startInfo = new()
        {
            FileName = candidate.ExecutablePath,
            Arguments = Quote(solutionPath),
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory,
        };
        return _environment.Start(startInfo)
            ? IdeLaunchResult.Launched(candidate)
            : IdeLaunchResult.Failed($"启动 IDE 失败：{candidate.Name}");
    }

    private void AddRiderCandidates(List<IdeCandidate> candidates)
    {
        AddPathCandidate(candidates, IdeKind.Rider, "Rider", "rider");
        string? localAppData = _environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return;
        }

        AddRiderExecutables(candidates, Path.Combine(localAppData, "Programs"));
        AddRiderExecutables(candidates, Path.Combine(localAppData, "JetBrains", "Toolbox", "apps", "Rider"));
    }

    private void AddRiderExecutables(List<IdeCandidate> candidates, string root)
    {
        foreach (string directory in _environment.EnumerateDirectories(root, "Rider*", SearchOption.AllDirectories))
        {
            AddFileCandidate(candidates, IdeKind.Rider, "Rider", Path.Combine(directory, "bin", "rider64.exe"));
            AddFileCandidate(candidates, IdeKind.Rider, "Rider", Path.Combine(directory, "bin", "rider.exe"));
            AddFileCandidate(candidates, IdeKind.Rider, "Rider", Path.Combine(directory, "bin", "rider.bat"));
        }
    }

    private void AddVisualStudioCandidates(List<IdeCandidate> candidates)
    {
        string? vswhere = _environment.FindOnPath("vswhere")
            ?? TryCombineExisting(_environment.GetEnvironmentVariable("ProgramFiles(x86)"), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (string.IsNullOrWhiteSpace(vswhere))
        {
            return;
        }

        string output = _environment.Capture(vswhere, "-latest -products * -property installationPath");
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddFileCandidate(candidates, IdeKind.VisualStudio, "Visual Studio", Path.Combine(line, "Common7", "IDE", "devenv.exe"));
        }
    }

    private void AddVsCodeCandidates(List<IdeCandidate> candidates)
    {
        AddPathCandidate(candidates, IdeKind.VsCode, "VS Code", "code");
        AddFileCandidate(candidates, IdeKind.VsCode, "VS Code", TryCombineExisting(_environment.GetEnvironmentVariable("LOCALAPPDATA"), "Programs", "Microsoft VS Code", "Code.exe"));
        AddFileCandidate(candidates, IdeKind.VsCode, "VS Code", TryCombineExisting(_environment.GetEnvironmentVariable("ProgramFiles"), "Microsoft VS Code", "Code.exe"));
        AddFileCandidate(candidates, IdeKind.VsCode, "VS Code", TryReadVsCodeRegistryCommand());
    }

    private void AddPathCandidate(List<IdeCandidate> candidates, IdeKind kind, string name, string command)
    {
        string? path = _environment.FindOnPath(command);
        AddFileCandidate(candidates, kind, name, path);
    }

    private void AddFileCandidate(List<IdeCandidate> candidates, IdeKind kind, string name, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && _environment.FileExists(path))
        {
            candidates.Add(new IdeCandidate(kind, name, path));
        }
    }

    private string? TryReadVsCodeRegistryCommand()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\Code.exe\shell\open\command");
            return ExtractExecutableFromCommand(key?.GetValue(null) as string);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? TryCombineExisting(string? root, params string[] segments)
    {
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine([root, .. segments]);
    }

    private static string? ExtractExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string trimmed = command.Trim();
        if (trimmed[0] == '"')
        {
            int endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed[1..endQuote] : null;
        }

        int firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    private static IReadOnlyList<IdeCandidate> Deduplicate(List<IdeCandidate> candidates)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<IdeCandidate> unique = [];
        foreach (IdeCandidate candidate in candidates)
        {
            if (seen.Add(candidate.ExecutablePath))
            {
                unique.Add(candidate);
            }
        }

        return unique;
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

internal interface IIdeLauncherEnvironment
{
    string? GetEnvironmentVariable(string name);

    string? FindOnPath(string command);

    bool FileExists(string path);

    IEnumerable<string> EnumerateDirectories(string root, string pattern, SearchOption searchOption);

    string Capture(string fileName, string arguments);

    bool Start(ProcessStartInfo startInfo);
}

internal sealed class IdeLauncherEnvironment : IIdeLauncherEnvironment
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
            foreach (string candidate in CommandCandidates(directory, command))
            {
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
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, pattern, searchOption)
            : [];
    }

    public string Capture(string fileName, string arguments)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
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

            string output = process.StandardOutput.ReadToEnd();
            _ = process.WaitForExit(2_000);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    public bool Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IEnumerable<string> CommandCandidates(string directory, string command)
    {
        yield return Path.Combine(directory, command);
        if (Path.HasExtension(command))
        {
            yield break;
        }

        yield return Path.Combine(directory, $"{command}.exe");
        yield return Path.Combine(directory, $"{command}.cmd");
        yield return Path.Combine(directory, $"{command}.bat");
    }
}

internal sealed record IdeCandidate(IdeKind Kind, string Name, string ExecutablePath);

internal sealed record IdeLaunchResult(bool Success, IdeCandidate? Candidate, string? Error)
{
    public static IdeLaunchResult Launched(IdeCandidate candidate)
    {
        return new IdeLaunchResult(true, candidate, null);
    }

    public static IdeLaunchResult Failed(string error)
    {
        return new IdeLaunchResult(false, null, error);
    }
}

internal enum IdeKind
{
    Rider,
    VisualStudio,
    VsCode,
}
