using System.Text.RegularExpressions;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>发布的 pixelengine-editor Skill 必须完整、确定且只调用公共 CLI。</summary>
public sealed class EditorAutomationSkillTests
{
    /// <summary>Skill 文件集、frontmatter、UI metadata 与 CLI wrapper 必须形成闭合发布面。</summary>
    [Fact]
    public void RepositorySkillIsCompleteAndInvokesOnlyThePublicCli()
    {
        string skillRoot = Path.Combine(RepositoryRoot(), "skills", "pixelengine-editor");
        string[] actualFiles =
        [
            .. Directory.EnumerateFiles(skillRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(skillRoot, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
        string[] expectedFiles =
        [
            "SKILL.md",
            "agents/openai.yaml",
            "references/workflows.md",
            "scripts/invoke.ps1",
        ];
        Assert.Equal(expectedFiles, actualFiles);

        string skill = File.ReadAllText(Path.Combine(skillRoot, "SKILL.md"));
        string metadata = File.ReadAllText(Path.Combine(skillRoot, "agents", "openai.yaml"));
        string wrapper = File.ReadAllText(Path.Combine(skillRoot, "scripts", "invoke.ps1"));
        Assert.StartsWith("---\nname: pixelengine-editor\n", NormalizeLines(skill), StringComparison.Ordinal);
        Assert.Contains("capabilities --matrix", skill, StringComparison.Ordinal);
        Assert.Contains("transaction execute --plan-file", skill, StringComparison.Ordinal);
        Assert.Contains(
            "Use the versioned local automation API only through `pixelengine-editor`",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("$pixelengine-editor", metadata, StringComparison.Ordinal);
        Assert.Contains("$env:PIXELENGINE_EDITOR_CLI", wrapper, StringComparison.Ordinal);
        Assert.Contains("Get-Command pixelengine-editor", wrapper, StringComparison.Ordinal);
        Assert.Contains("& $cliPath @args", wrapper, StringComparison.Ordinal);
        Assert.Contains("exit $LASTEXITCODE", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedPipe", wrapper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", wrapper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scripted", wrapper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mcp", wrapper, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>PowerShell wrapper 的多 scope CSV 必须始终作为一个带引号参数示例发布。</summary>
    [Fact]
    public void PowerShellSkillExamplesQuoteEveryMultiScopeCsv()
    {
        string skillRoot = Path.Combine(RepositoryRoot(), "skills", "pixelengine-editor");
        string[] markdownFiles = Directory.GetFiles(skillRoot, "*.md", SearchOption.AllDirectories);
        Regex unquotedMultiScope = new(
            @"--scopes\s+[A-Za-z0-9.]+,",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        string[] violations =
        [
            .. markdownFiles
                .SelectMany(path => File.ReadLines(path).Select((line, index) => new
                {
                    Path = path,
                    Line = line,
                    Number = index + 1,
                }))
                .Where(entry => unquotedMultiScope.IsMatch(entry.Line))
                .Select(entry =>
                    $"{Path.GetRelativePath(skillRoot, entry.Path).Replace('\\', '/')}:{entry.Number}")
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            violations.Length == 0,
            "存在会被 PowerShell 拆分的未加引号 --scopes CSV：" + string.Join(", ", violations));
    }

    /// <summary>最终验收 runner 必须只启动公开 CLI，并覆盖完整 author→run→build→player 生命周期。</summary>
    [Fact]
    public void FinalE2ERunnerUsesOnlyExternalCliProcessesAndHasNoScriptedBypass()
    {
        string root = RepositoryRoot();
        string runner = File.ReadAllText(Path.Combine(root, "tools", "run-editor-automation-e2e.ps1"));

        Assert.Contains("Invoke-CapturedProcess", runner, StringComparison.Ordinal);
        Assert.Contains("-FilePath $cliPath", runner, StringComparison.Ordinal);
        Assert.Contains("externalCliProcessCount", runner, StringComparison.Ordinal);
        Assert.Contains("@('transaction', 'execute'", runner, StringComparison.Ordinal);
        Assert.Contains("transaction-execute-rollback", runner, StringComparison.Ordinal);
        Assert.Contains("hierarchy-after-rollback", runner, StringComparison.Ordinal);
        Assert.Contains("history-undo", runner, StringComparison.Ordinal);
        Assert.Contains("history-redo", runner, StringComparison.Ordinal);
        Assert.Contains("play-enter-first", runner, StringComparison.Ordinal);
        Assert.Contains("play-enter-second", runner, StringComparison.Ordinal);
        Assert.Contains("marker-transform-set", runner, StringComparison.Ordinal);
        Assert.Contains("build-start-wait", runner, StringComparison.Ordinal);
        Assert.Contains("player-get-running", runner, StringComparison.Ordinal);
        Assert.Contains("player-terminate", runner, StringComparison.Ordinal);
        Assert.Contains("discover-after-exit", runner, StringComparison.Ordinal);
        Assert.Contains("pixelengine.editor-automation-e2e/v1", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedPipeClientStream", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorAutomationClient", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("credential.json", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--scripted-", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("Computer Use", runner, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               (!File.Exists(Path.Combine(current.FullName, "PixelEngine.sln")) ||
                !File.Exists(Path.Combine(current.FullName, "AGENTS.md"))))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }
}
