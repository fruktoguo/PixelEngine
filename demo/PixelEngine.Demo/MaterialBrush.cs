using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 材质笔刷脚本，使用公开世界写入、材质查询、输入与相机 API。
/// </summary>
public sealed class MaterialBrush : Behaviour
{
    private static readonly string[] DefaultMaterialNames =
    [
        "sand",
        "water",
        "oil",
        "lava",
        "fire",
        "stone",
        "wood",
        "acid",
        "ice",
        "metal",
    ];

    private readonly MaterialId[] _materials = new MaterialId[DefaultMaterialNames.Length];
    private MaterialId _empty;
    private bool _resolved;

    /// <summary>
    /// 当前选中材质槽位。
    /// </summary>
    public int SelectedIndex { get; private set; }

    /// <summary>
    /// 当前笔刷半径，单位 cell。
    /// </summary>
    public int Radius { get; private set; } = 4;

    /// <summary>
    /// 最小笔刷半径。
    /// </summary>
    public int MinRadius { get; set; } = 1;

    /// <summary>
    /// 最大笔刷半径。
    /// </summary>
    public int MaxRadius { get; set; } = 24;

    /// <summary>
    /// 当前选中材质名。
    /// </summary>
    public string SelectedMaterialName => DefaultMaterialNames[SelectedIndex];

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveMaterials();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        ResolveMaterials();
        HandleMaterialSelection();
        HandleRadius();
        HandlePaint();
    }

    private void ResolveMaterials()
    {
        if (_resolved)
        {
            return;
        }

        _empty = Context.Materials.Resolve("empty");
        bool allResolved = _empty.IsValid;
        for (int i = 0; i < DefaultMaterialNames.Length; i++)
        {
            _materials[i] = Context.Materials.Resolve(DefaultMaterialNames[i]);
            allResolved &= _materials[i].IsValid;
        }

        _resolved = allResolved;
    }

    private void HandleMaterialSelection()
    {
        for (int i = 0; i < DefaultMaterialNames.Length; i++)
        {
            if (Context.Input.WasPressed(DigitKey(i)))
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    private void HandleRadius()
    {
        float wheel = Context.Input.MouseWheelY;
        if (wheel == 0f)
        {
            return;
        }

        int delta = wheel > 0f ? 1 : -1;
        Radius = Math.Clamp(Radius + delta, Math.Max(1, MinRadius), Math.Max(MinRadius, MaxRadius));
    }

    private void HandlePaint()
    {
        bool place = Context.Input.IsMouseDown(MouseButton.Left);
        bool erase = Context.Input.IsMouseDown(MouseButton.Right);
        if (!place && !erase)
        {
            return;
        }

        (float mouseX, float mouseY) = Context.Input.MousePixel;
        Point2F world = Context.Camera.ScreenToWorld(mouseX, mouseY);
        int x = (int)MathF.Floor(world.X);
        int y = (int)MathF.Floor(world.Y);
        MaterialId material = erase ? _empty : _materials[SelectedIndex];
        if (material.IsValid)
        {
            Context.Cells.Paint(x, y, Radius, material);
        }
    }

    private static Key DigitKey(int index)
    {
        return index switch
        {
            0 => Key.Digit1,
            1 => Key.Digit2,
            2 => Key.Digit3,
            3 => Key.Digit4,
            4 => Key.Digit5,
            5 => Key.Digit6,
            6 => Key.Digit7,
            7 => Key.Digit8,
            8 => Key.Digit9,
            9 => Key.Digit0,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "材质槽位超出数字键范围。"),
        };
    }
}
