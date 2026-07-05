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
        string logo = WriteAsset(root, "images/logo.png", "png");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml", "preload": true },
                { "id": "settings", "path": "settings/settings.xhtml" }
              ],
              "images": [
                { "id": "logo", "path": "images/logo.png", "preload": true }
              ]
            }
            """);

        UiManifest manifest = UiManifestLoader.LoadFromDirectory(root);

        Assert.Equal(NormalizedRoot(root), manifest.RootDirectory);
        Assert.Equal(NormalizedRoot(Path.Combine(root, "fonts")), manifest.FontsDirectory);
        Assert.Equal(NormalizedRoot(Path.Combine(root, "images")), manifest.ImagesDirectory);
        Assert.Equal(manifest.FontsDirectory, manifest.AssetDirectories.FontsDirectory);
        Assert.Equal(manifest.ImagesDirectory, manifest.AssetDirectories.ImagesDirectory);
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

        Assert.Equal(1, manifest.ImageCount);
        Assert.True(manifest.TryGetImage("logo", out UiManifestImage image));
        Assert.True(image.Preload);
        Assert.Equal("images/logo.png", image.RelativePath);
        Assert.Equal(Path.GetFullPath(logo), image.FullPath);
        Assert.Equal(UiStableId.Hash("logo"), image.StableId);
        Assert.Equal(image, manifest.GetRequiredImage("logo"));
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

    [Fact]
    public void LoadRejectsImageOutsideImagesDirectory()
    {
        string root = CreateUiRoot();
        _ = WriteAsset(root, "main.xhtml", "<ui />");
        _ = WriteAsset(root, "logo.png", "png");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml" }
              ],
              "images": [
                { "id": "logo", "path": "logo.png" }
              ]
            }
            """);

        _ = Assert.Throws<InvalidDataException>(() => UiManifestLoader.LoadFromDirectory(root));
    }

    [Fact]
    public void LoadRejectsDuplicateImageIds()
    {
        string root = CreateUiRoot();
        _ = WriteAsset(root, "main.xhtml", "<ui />");
        _ = WriteAsset(root, "images/logo.png", "png");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml" }
              ],
              "images": [
                { "id": "logo", "path": "images/logo.png" },
                { "id": "logo", "path": "images/logo.png" }
              ]
            }
            """);

        _ = Assert.Throws<InvalidDataException>(() => UiManifestLoader.LoadFromDirectory(root));
    }

    [Fact]
    public void LoadRejectsMissingImageAsset()
    {
        string root = CreateUiRoot();
        _ = WriteAsset(root, "main.xhtml", "<ui />");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml" }
              ],
              "images": [
                { "id": "missing", "path": "images/missing.png" }
              ]
            }
            """);

        _ = Assert.Throws<FileNotFoundException>(() => UiManifestLoader.LoadFromDirectory(root));
    }

    [Fact]
    public void AssetDirectoriesReportOptionalFolderPresence()
    {
        string root = CreateUiRoot();
        _ = Directory.CreateDirectory(Path.Combine(root, "fonts"));
        _ = WriteAsset(root, "main.xhtml", "<ui />");
        WriteManifest(root, """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml" }
              ]
            }
            """);

        UiManifest manifest = UiManifestLoader.LoadFromDirectory(root);

        Assert.True(manifest.AssetDirectories.HasFontsDirectory);
        Assert.False(manifest.AssetDirectories.HasImagesDirectory);
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
