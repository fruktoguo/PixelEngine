using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class AudioProjectDisciplineTests
{
    [Fact]
    public void AudioProjectReferencesOpenAlAndAllowsUnsafe()
    {
        XDocument project = XDocument.Load(ProjectPath("src", "PixelEngine.Audio", "PixelEngine.Audio.csproj"));
        string xml = project.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("Silk.NET.OpenAL", xml, StringComparison.Ordinal);
        Assert.Contains("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioSourcesDoNotDeclareDllImport()
    {
        string source = string.Join('\n', Directory.EnumerateFiles(ProjectPath("src", "PixelEngine.Audio"), "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("DllImport", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LibraryImport", source, StringComparison.Ordinal);
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
