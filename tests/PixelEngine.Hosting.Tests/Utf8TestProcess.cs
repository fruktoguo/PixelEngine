using System.Diagnostics;
using System.Text;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 为脚本纪律测试创建确定性的 UTF-8 子进程，避免 hosted Windows 的 console code page
/// 把中文诊断替换成问号，也避免裸 <c>bash</c> 命中 WindowsApps/WSL launcher。
/// </summary>
internal static class Utf8TestProcess
{
    private const string PowerShellBootstrap = """
        [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $global:OutputEncoding = [Console]::OutputEncoding
        $global:ErrorActionPreference = 'Stop'
        if ($null -ne (Get-Variable -Name PSStyle -ErrorAction SilentlyContinue)) {
            $PSStyle.OutputRendering = 'PlainText'
        }
        if ($args.Count -lt 1) { throw 'missing script path' }
        $scriptPath = [string]$args[0]
        $scriptArguments = if ($args.Count -gt 1) { @($args[1..($args.Count - 1)]) } else { @() }
        $global:LASTEXITCODE = 0
        try {
            & $scriptPath @scriptArguments
            $scriptSucceeded = $?
            $scriptExitCode = $global:LASTEXITCODE
        }
        catch {
            [Console]::Error.WriteLine([string]$_.Exception.Message)
            exit 1
        }
        if ($scriptExitCode -ne 0) { exit $scriptExitCode }
        if (-not $scriptSucceeded) { exit 1 }
        """;

    private static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static ProcessStartInfo CreatePowerShell(
        string workingDirectory,
        string scriptPath,
        IEnumerable<string> arguments,
        string? executable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = CreateRedirected(executable ?? "pwsh", workingDirectory);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-CommandWithArgs");
        startInfo.ArgumentList.Add(PowerShellBootstrap);
        startInfo.ArgumentList.Add(Path.GetFullPath(scriptPath, workingDirectory));
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static ProcessStartInfo CreateBash(
        string workingDirectory,
        string scriptPath,
        IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = CreateRedirected(ResolveBashExecutable(), workingDirectory);
        startInfo.Environment["LANG"] = "C.UTF-8";
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static void ConfigureRedirectedUtf8(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.StandardOutputEncoding = Utf8;
        startInfo.StandardErrorEncoding = Utf8;
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";
    }

    internal static string ResolveBashExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("PIXELENGINE_TEST_BASH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string fullPath = Path.GetFullPath(configured);
            return File.Exists(fullPath)
                ? fullPath
                : throw new FileNotFoundException("PIXELENGINE_TEST_BASH 指向的 Bash 不存在。", fullPath);
        }

        if (!OperatingSystem.IsWindows())
        {
            return File.Exists("/bin/bash") ? "/bin/bash" : "bash";
        }

        string[] candidates =
        [
            Combine(Environment.GetEnvironmentVariable("ProgramW6432"), "Git", "bin", "bash.exe"),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
        ];
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[i]) && File.Exists(candidates[i]))
            {
                return Path.GetFullPath(candidates[i]);
            }
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(directory, "bash.exe");
                if (File.Exists(candidate) &&
                    !candidate.Contains(
                        Path.Combine("Microsoft", "WindowsApps"),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new FileNotFoundException(
            "Windows 脚本纪律测试需要 Git for Windows Bash；拒绝使用 WindowsApps/WSL launcher。可用 PIXELENGINE_TEST_BASH 显式指定。");
    }

    private static ProcessStartInfo CreateRedirected(string executable, string workingDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        ConfigureRedirectedUtf8(startInfo);
        return startInfo;
    }

    private static string Combine(string? root, params string[] segments)
    {
        return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine([root, .. segments]);
    }
}
