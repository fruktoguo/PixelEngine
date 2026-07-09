namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 可执行程序入口，将命令行参数转发给 <see cref="EditorShellApp"/>。
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        return EditorShellApp.Execute(args);
    }
}
