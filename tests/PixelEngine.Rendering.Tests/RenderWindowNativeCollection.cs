using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// GLFW/Win32 进程级窗口类注册不能被多个 xUnit test class 并行初始化。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RenderWindowNativeCollection
{
    public const string Name = "RenderWindowNative";
}
