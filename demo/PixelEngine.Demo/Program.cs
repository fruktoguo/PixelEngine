/// <summary>
/// Demo 可执行程序入口；将命令行参数转发给 Demo 程序。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 进程主入口，由 SDK 隐式生成调用。
    /// </summary>
    public static int Main(string[] args)
    {
        return PixelEngine.Demo.DemoProgram.Execute(args);
    }
}
