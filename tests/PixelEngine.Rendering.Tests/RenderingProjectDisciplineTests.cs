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

    [Fact]
    public void RenderingSourcesDoNotReadBackGpuDataIntoSimulation()
    {
        string source = ReadRenderingSources();

        Assert.DoesNotContain("ReadPixels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTexImage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTextureImage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBufferRange(BufferTargetARB.PixelPackBuffer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelPackBuffer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingHotPathSourcesAvoidLinqAndIteratorAllocations()
    {
        string source = string.Join(
            Environment.NewLine,
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderBufferBuilder.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "ParticleCompositor.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "OverlayRenderer.cs")),
            File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "BloomPass.cs")));

        Assert.DoesNotContain("System.Linq", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("yield return", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Action<", source.Replace("event Action<GL>?", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
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

    private static string ReadRenderingSources()
    {
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(ProjectPath("src", "PixelEngine.Rendering"), "*.cs")
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));
    }
}
