using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor authoring 侧的内建 Canvas (Web) 组件；不冒充脚本 Behaviour。
/// </summary>
internal sealed class EditorWebCanvasComponent
{
    public string? ManifestAssetId { get; set; }

    public string? ManifestPath { get; set; }

    public string? InitialScreenId { get; set; }

    public bool Enabled { get; set; } = true;

    public int SortingOrder { get; set; }

    public bool Primary { get; set; }

    public EditorWebCanvasComponent Clone(bool clearPrimary = false)
    {
        return new EditorWebCanvasComponent
        {
            ManifestAssetId = ManifestAssetId,
            ManifestPath = ManifestPath,
            InitialScreenId = InitialScreenId,
            Enabled = Enabled,
            SortingOrder = SortingOrder,
            Primary = !clearPrimary && Primary,
        };
    }

    public static EditorWebCanvasComponent FromDocument(EngineSceneWebCanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new EditorWebCanvasComponent
        {
            ManifestAssetId = document.ManifestAssetId,
            ManifestPath = document.ManifestPath,
            InitialScreenId = document.InitialScreenId,
            Enabled = document.Enabled,
            SortingOrder = document.SortingOrder,
            Primary = document.Primary,
        };
    }

    public EngineSceneWebCanvasDocument ToDocument(bool clearPrimary = false)
    {
        return new EngineSceneWebCanvasDocument
        {
            ManifestAssetId = ManifestAssetId,
            ManifestPath = ManifestPath,
            InitialScreenId = InitialScreenId,
            Enabled = Enabled,
            SortingOrder = SortingOrder,
            Primary = !clearPrimary && Primary,
        };
    }

    public static bool ContentEquals(EditorWebCanvasComponent? left, EditorWebCanvasComponent? right)
    {
        return ReferenceEquals(left, right) ||
            (left is not null &&
             right is not null &&
             string.Equals(left.ManifestAssetId, right.ManifestAssetId, StringComparison.Ordinal) &&
             string.Equals(left.ManifestPath, right.ManifestPath, StringComparison.Ordinal) &&
             string.Equals(left.InitialScreenId, right.InitialScreenId, StringComparison.Ordinal) &&
             left.Enabled == right.Enabled &&
             left.SortingOrder == right.SortingOrder &&
             left.Primary == right.Primary);
    }
}

/// <summary>
/// Editor authoring 侧的内建 Canvas Scaler 组件。
/// </summary>
internal sealed class EditorCanvasScalerComponent
{
    public UiCanvasScalerSettings Settings { get; set; } = UiCanvasScalerSettings.Default;

    public EditorCanvasScalerComponent Clone()
    {
        return new EditorCanvasScalerComponent { Settings = Settings };
    }

    public static EditorCanvasScalerComponent FromDocument(EngineSceneCanvasScalerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new EditorCanvasScalerComponent { Settings = document.ToSettings() };
    }

    public EngineSceneCanvasScalerDocument ToDocument()
    {
        UiCanvasScalerSettings settings = Settings;
        return EngineSceneCanvasScalerDocument.FromSettings(in settings);
    }

    public static bool ContentEquals(EditorCanvasScalerComponent? left, EditorCanvasScalerComponent? right)
    {
        return ReferenceEquals(left, right) ||
            (left is not null &&
             right is not null &&
             left.Settings == right.Settings);
    }
}
