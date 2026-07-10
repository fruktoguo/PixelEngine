using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 目标水晶矿簇；负责铺设 crystal cell，并在矿簇被破坏后发布采集收益事件。
/// </summary>
public sealed class ObjectiveCrystal : Behaviour
{
    private MaterialId _crystal = MaterialId.Invalid;
    private bool[] _remaining = [];
    private int _diameter;
    private int _settleFrames;
    private bool _initialized;

    /// <summary>
    /// 水晶矿簇中心 X 坐标。
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// 水晶矿簇中心 Y 坐标。
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// 矿簇半径，单位 cell。
    /// </summary>
    public int Radius { get; set; } = 3;

    /// <summary>
    /// 水晶材质名。
    /// </summary>
    public string MaterialName { get; set; } = "crystal";

    /// <summary>
    /// 是否在启动时铺设水晶矿簇。
    /// </summary>
    public bool PlaceOnStart { get; set; } = true;

    /// <summary>
    /// 已发布的采集 cell 数。
    /// </summary>
    public int CollectedCells { get; private set; }

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveMaterial();
        InitializeMask();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        ResolveMaterial();
        if (!_crystal.IsValid)
        {
            return;
        }

        if (!_initialized)
        {
            InitializeMask();
        }

        if (_settleFrames > 0)
        {
            _settleFrames--;
            return;
        }

        DetectCollectedCells();
    }

    private void ResolveMaterial()
    {
        if (_crystal.IsValid)
        {
            return;
        }

        _crystal = Context.Materials.Resolve(MaterialName);
        BlockedReason = _crystal.IsValid ? string.Empty : $"材质未解析：{MaterialName}";
    }

    private void InitializeMask()
    {
        if (_initialized || !_crystal.IsValid)
        {
            return;
        }

        int radius = Math.Max(1, Radius);
        _diameter = (radius * 2) + 1;
        _remaining = new bool[_diameter * _diameter];
        int radiusSquared = radius * radius;
        for (int localY = 0; localY < _diameter; localY++)
        {
            int dy = localY - radius;
            for (int localX = 0; localX < _diameter; localX++)
            {
                int dx = localX - radius;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    _remaining[(localY * _diameter) + localX] = true;
                }
            }
        }

        if (PlaceOnStart)
        {
            Context.Cells.Paint(X, Y, radius, _crystal);
            _settleFrames = 1;
        }

        _initialized = true;
    }

    private void DetectCollectedCells()
    {
        int radius = Math.Max(1, Radius);
        MaterialInfo info = Context.Materials.GetInfo(_crystal);
        ushort amountPerCell = info.MineYield == 0 ? (ushort)1 : info.MineYield;
        for (int localY = 0; localY < _diameter; localY++)
        {
            int y = Y + localY - radius;
            for (int localX = 0; localX < _diameter; localX++)
            {
                int index = (localY * _diameter) + localX;
                if (!_remaining[index])
                {
                    continue;
                }

                int x = X + localX - radius;
                if (Context.Cells.GetMaterial(x, y) == _crystal)
                {
                    continue;
                }

                _remaining[index] = false;
                CollectedCells++;
                MineYieldEvent item = new(x, y, _crystal.Value, amountPerCell);
                _ = Context.Events.TryPublish(in item);
            }
        }
    }
}
