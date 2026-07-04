namespace PixelEngine.Editor.Shell.Build;

internal sealed class BuildToolLocator
{
    public BuildToolLocatorResult Locate()
    {
        string repositoryRoot = FindRepositoryRoot();
        bool windows = OperatingSystem.IsWindows();
        string buildPlayerName = windows ? "build-player.ps1" : "build-player.sh";
        string buildPlayerPath = Path.Combine(repositoryRoot, "tools", buildPlayerName);
        return new BuildToolLocatorResult
        {
            RepositoryRoot = repositoryRoot,
            BuildPlayerPath = buildPlayerPath,
            BuildPlayerExists = File.Exists(buildPlayerPath),
            DotnetPath = "dotnet",
            ShellPath = windows ? "pwsh" : "sh",
            UsesPowerShell = windows,
        };
    }

    private static string FindRepositoryRoot()
    {
        string? root = FindRepositoryRootFrom(AppContext.BaseDirectory) ??
            FindRepositoryRootFrom(Environment.CurrentDirectory);
        return root ?? throw new DirectoryNotFoundException("无法定位 PixelEngine.sln 所在仓库根目录。");
    }

    private static string? FindRepositoryRootFrom(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
