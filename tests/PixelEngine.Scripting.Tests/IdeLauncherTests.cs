using System.Diagnostics;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// IDE 启动器测试。
/// 不变式：IDE 启动器解析工作区与脚本路径正确。
/// </summary>
public sealed class IdeLauncherTests
{
    /// <summary>
    /// 验证 IDE 探测按 Rider、Visual Studio、VS Code 优先级返回。
    /// </summary>
    [Fact]
    public void DiscoverInstalledIdesReturnsPriorityOrder()
    {
        FakeIdeEnvironment environment = new();
        _ = environment.Files.Add(@"C:\Tools\code.cmd");
        _ = environment.Files.Add(@"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe");
        _ = environment.Files.Add(@"C:\VS\Community\Common7\IDE\devenv.exe");
        _ = environment.Files.Add(@"C:\Users\dev\AppData\Local\Programs\Rider 2026.1\bin\rider64.exe");
        environment.EnvironmentVariables["PATH"] = @"C:\Tools";
        environment.EnvironmentVariables["LOCALAPPDATA"] = @"C:\Users\dev\AppData\Local";
        environment.EnvironmentVariables["ProgramFiles(x86)"] = @"C:\Program Files (x86)";
        _ = environment.Directories.Add(@"C:\Users\dev\AppData\Local\Programs\Rider 2026.1");
        environment.CapturedOutputs[@"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"] = @"C:\VS\Community";

        IdeLauncher launcher = new(environment);
        IReadOnlyList<IdeCandidate> candidates = launcher.DiscoverInstalledIdes();

        Assert.Equal([IdeKind.Rider, IdeKind.VisualStudio, IdeKind.VsCode], candidates.Select(candidate => candidate.Kind).ToArray());
    }

    /// <summary>
    /// 验证启动器选择最高优先级 IDE 并传入解决方案路径。
    /// </summary>
    [Fact]
    public void OpenSolutionStartsHighestPriorityIde()
    {
        FakeIdeEnvironment environment = new();
        _ = environment.Files.Add(@"C:\Tools\code.cmd");
        _ = environment.Files.Add(@"C:\Game\PixelGame.sln");
        environment.EnvironmentVariables["PATH"] = @"C:\Tools";
        IdeLauncher launcher = new(environment);

        IdeLaunchResult result = launcher.OpenSolution(@"C:\Game\PixelGame.sln");

        Assert.True(result.Success, result.Error);
        Assert.Equal(IdeKind.VsCode, result.Candidate?.Kind);
        Assert.NotNull(environment.StartInfo);
        Assert.Equal(@"C:\Tools\code.cmd", environment.StartInfo.FileName);
        Assert.Equal("\"C:\\Game\\PixelGame.sln\"", environment.StartInfo.Arguments);
        Assert.Equal(@"C:\Game", environment.StartInfo.WorkingDirectory);
    }

    private sealed class FakeIdeEnvironment : IIdeLauncherEnvironment
    {
        public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> CapturedOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ProcessStartInfo? StartInfo { get; private set; }

        public string? GetEnvironmentVariable(string name)
        {
            return EnvironmentVariables.GetValueOrDefault(name);
        }

        public string? FindOnPath(string command)
        {
            string? path = GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (string candidate in CommandCandidates(directory, command))
                {
                    if (Files.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        public bool FileExists(string path)
        {
            return Files.Contains(path);
        }

        public IEnumerable<string> EnumerateDirectories(string root, string pattern, SearchOption searchOption)
        {
            return Directories.Where(directory =>
                directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase));
        }

        public string Capture(string fileName, string arguments)
        {
            return CapturedOutputs.GetValueOrDefault(fileName) ?? string.Empty;
        }

        public bool Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return true;
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
}
