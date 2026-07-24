using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

internal static partial class NoitaPixelSceneVisualCatalog
{
    internal static int LayerCount => Layers.Length;

    internal static WorldVisualLayerDescriptor GetLayer(int index)
    {
        ReadOnlySpan<NoitaPixelSceneVisualDefinition> layers = Layers;
        if ((uint)index >= (uint)layers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        ref readonly NoitaPixelSceneVisualDefinition layer = ref layers[index];
        return new WorldVisualLayerDescriptor(
            layer.Asset,
            layer.WorldX,
            layer.WorldY,
            layer.Width,
            layer.Height,
            layer.Layer);
    }
}

internal readonly record struct NoitaPixelSceneVisualDefinition(
    ScriptAssetReference Asset,
    int WorldX,
    int WorldY,
    int Width,
    int Height,
    WorldVisualLayerKind Layer,
    string SourcePath,
    string SourceSha256);
