namespace PixelEngine.Editor.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await CliApplication.RunAsync(args).ConfigureAwait(false);
    }
}
