namespace PixelEngine.Editor.Shell;

internal sealed class EditorSceneTransform
{
    public float X { get; set; }

    public float Y { get; set; }

    public float RotationRadians { get; set; }

    public float ScaleX { get; set; } = 1f;

    public float ScaleY { get; set; } = 1f;

    public EditorSceneTransform Clone()
    {
        return new EditorSceneTransform
        {
            X = X,
            Y = Y,
            RotationRadians = RotationRadians,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
        };
    }
}
