using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderingProjectDisciplineTests
{
    [Fact]
    public void RenderingProjectReferencesRequiredProjectsAndAllowsUnsafe()
    {
        XDocument project = XDocument.Load(ProjectPath("src", "PixelEngine.Rendering", "PixelEngine.Rendering.csproj"));
        string xml = project.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.Core\\PixelEngine.Core.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.Simulation\\PixelEngine.Simulation.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.Content\\PixelEngine.Content.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("..\\PixelEngine.World\\PixelEngine.World.csproj", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderWindowDoesNotUseSilkRunLoop()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderWindow.cs"));

        Assert.Contains(".DoEvents()", source, StringComparison.Ordinal);
        Assert.Contains(".SwapBuffers()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Run(", source, StringComparison.Ordinal);
    }

    private static string ProjectPath(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            path = Directory.GetParent(path)!.FullName;
        }

        return Path.Combine([path, .. parts]);
    }
}
