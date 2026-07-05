using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class UiManifestLoaderTests
{
    [Fact]
    public void LoadFromDirectoryResolvesScreensToAssetSources()
    {
        string root = CreateUiRoot();
        string main = WriteAsset(root, "main.xhtml", "<ui />");
        _ = WriteAsset(root, "settings/settings.xhtml", "<ui />");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml", "preload": true },
                { "id": "settings", "path": "settings/settings.xhtml" }
              ]
            }
            """);

        UiManifest manifest = UiManifestLoader.LoadFromDirectory(root);

        Assert.Equal(NormalizedRoot(root), manifest.RootDirectory);
        Assert.Equal(2, manifest.ScreenCount);
        Assert.True(manifest.TryGetScreen("main", out UiManifestScreen screen));
        Assert.True(screen.Preload);
        Assert.Equal("main.xhtml", screen.RelativePath);
        Assert.Equal(Path.GetFullPath(main), screen.FullPath);
        Assert.Equal(new UiScreenId(UiStableId.Hash("main")), screen.ScreenId);

        UiDocumentSource source = manifest.ResolveDocumentSource("main");
        Assert.Equal(UiDocumentSourceKind.Asset, source.Kind);
        Assert.Equal(Path.GetFullPath(main), source.Path);
        Assert.Equal(screen.ScreenId.Value, source.StableId);
    }

    [Fact]
    public void LoadRejectsDuplicateScreenIds()
    {
        string root = CreateUiRoot();
        _ = WriteAsset(root, "main.xhtml", "<ui />");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml" },
                { "id": "main", "path": "main.xhtml" }
              ]
            }
            """);

        _ = Assert.Throws<InvalidDataException>(() => UiManifestLoader.LoadFromDirectory(root));
    }

    [Fact]
    public void LoadRejectsPathEscapingUiRoot()
    {
        string root = CreateUiRoot();
        string outside = Path.Combine(Path.GetDirectoryName(root)!, "outside.xhtml");
        File.WriteAllText(outside, "<ui />");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "outside", "path": "../outside.xhtml" }
              ]
            }
            """);

        _ = Assert.Throws<InvalidDataException>(() => UiManifestLoader.LoadFromDirectory(root));
    }

    [Fact]
    public void LoadRejectsMissingScreenDocument()
    {
        string root = CreateUiRoot();
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "missing.xhtml" }
              ]
            }
            """);

        _ = Assert.Throws<FileNotFoundException>(() => UiManifestLoader.LoadFromDirectory(root));
    }

    private static string CreateUiRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-ui", Guid.NewGuid().ToString("N"), "content", "ui");
        _ = Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteAsset(string root, string relativePath, string contents)
    {
        string path = Path.Combine(root, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void WriteManifest(string root, string contents)
    {
        File.WriteAllText(Path.Combine(root, UiManifestLoader.ManifestFileName), contents);
    }

    private static string NormalizedRoot(string root)
    {
        return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
    }
}
