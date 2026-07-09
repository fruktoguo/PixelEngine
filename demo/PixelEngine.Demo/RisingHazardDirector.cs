using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 数据驱动的上升熔岩危险导演，通过一排 MaterialEmitter 持续注入熔岩。
/// </summary>
public sealed class RisingHazardDirector : Behaviour
{
    private MissionDirector? _mission;
    private MaterialEmitter[] _emitters = [];
    private MaterialId _material = MaterialId.Invalid;
    private bool _emitterBuildSystemRegistered;
    private float _elapsedSeconds;
    private float _fillTimer;

    /// <summary>
    /// 熔岩材质名。
    /// </summary>
    public string MaterialName { get; set; } = "lava";

    /// <summary>
    /// 熔岩覆盖区域左侧 X 坐标。
    /// </summary>
    public float MinX { get; set; } = 96f;

    /// <summary>
    /// 熔岩覆盖区域宽度。
    /// </summary>
    public float Width { get; set; } = 448f;

    /// <summary>
    /// 初始熔岩表面 Y 坐标。
    /// </summary>
    public float StartSurfaceY { get; set; } = 336f;

    /// <summary>
    /// 目标熔岩表面 Y 坐标。
    /// </summary>
    public float TargetSurfaceY { get; set; } = 196f;

    /// <summary>
    /// 从初始熔岩线升到目标熔岩线所需秒数。
    /// </summary>
    public float RiseSeconds { get; set; } = 180f;

    /// <summary>
    /// 表面采样喷口数量。
    /// </summary>
    public int EmitterCount { get; set; } = 12;

    /// <summary>
    /// 每个喷口写入半径。
    /// </summary>
    public int EmitterRadius { get; set; } = 4;

    /// <summary>
    /// 每个喷口喷发间隔。
    /// </summary>
    public float EmitterIntervalSeconds { get; set; } = 0.08f;

    /// <summary>
    /// 熔岩线以下补充熔岩的水平采样间距，单位 cell；越小越密，默认保持低开销。
    /// </summary>
    public int FillStepCells { get; set; } = 12;

    /// <summary>
    /// 熔岩线以下补充熔岩的垂直采样间距，单位 cell。
    /// </summary>
    public int FillVerticalStepCells { get; set; } = 8;

    /// <summary>
    /// 补充熔岩的最小时间间隔，单位秒。
    /// </summary>
    public float FillIntervalSeconds { get; set; } = 0.12f;

    /// <summary>
    /// 若熔岩表面升到该 Y 坐标及以上，则判定通路被淹没。
    /// </summary>
    public float LossSurfaceY { get; set; } = 210f;

    /// <summary>
    /// 当前熔岩表面 Y 坐标。
    /// </summary>
    public float CurrentSurfaceY { get; private set; }

    /// <summary>
    /// 当前上涨熔岩覆盖的活跃面积估算，单位 cell。
    /// </summary>
    public int ActiveAreaCells { get; private set; }

    /// <summary>
    /// 已创建的 MaterialEmitter 数量。
    /// </summary>
    public int ActiveEmitterCount => _emitters.Length;

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        CurrentSurfaceY = StartSurfaceY;
        ActiveAreaCells = EstimateActiveAreaCells();
        ResolveMaterial();
        ResolveMission();
        RegisterEmitterBuildSystem();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        ResolveMission();
        ResolveMaterial();
        RegisterEmitterBuildSystem();
        // 熔岩线插值上升 → 同步 MissionDirector → 驱动喷口与体积填充 → 淹没判负
        _elapsedSeconds += dt;
        float duration = Math.Max(0.001f, RiseSeconds);
        float t = Math.Clamp(_elapsedSeconds / duration, 0f, 1f);
        CurrentSurfaceY = StartSurfaceY + ((TargetSurfaceY - StartSurfaceY) * t);
        ActiveAreaCells = EstimateActiveAreaCells();
        _mission?.SetLavaSurface(CurrentSurfaceY);
        UpdateEmitters();
        FillLavaVolume(dt);

        if (_mission is not null && _mission.State == MissionState.Playing && CurrentSurfaceY <= LossSurfaceY)
        {
            _mission.MarkLost("lava_flooded_route");
        }
    }

    private void ResolveMaterial()
    {
        if (_material.IsValid)
        {
            return;
        }

        _material = Context.Materials.Resolve(MaterialName);
        if (!_material.IsValid)
        {
            BlockedReason = $"材质未解析：{MaterialName}";
        }
    }

    private void RegisterEmitterBuildSystem()
    {
        if (_emitterBuildSystemRegistered)
        {
            return;
        }

        Context.Scene.RegisterSystem(new EmitterBuildSystem(this));
        _emitterBuildSystemRegistered = true;
    }

    private void EnsureEmitters()
    {
        if (_emitters.Length > 0)
        {
            return;
        }

        int count = Math.Clamp(EmitterCount, 1, 64);
        _emitters = new MaterialEmitter[count];
        for (int i = 0; i < count; i++)
        {
            Entity entity = Context.Scene.CreateEntity();
            MaterialEmitter emitter = entity.AddComponent<MaterialEmitter>();
            emitter.MaterialName = MaterialName;
            emitter.Radius = Math.Max(1, EmitterRadius);
            emitter.IntervalSeconds = Math.Max(0.001f, EmitterIntervalSeconds);
            emitter.ParticleCount = 1;
            emitter.ParticleSpeed = 14f;
            emitter.ParticleLifetime = 34;
            emitter.DirectionX = 0f;
            emitter.DirectionY = 1f;
            emitter.AddLight = true;
            emitter.LightRadius = 22f;
            emitter.LightColorBgra = 0xFF_30_70_FF;
            emitter.LightIntensity = 0.28f;
            _emitters[i] = emitter;
        }

        UpdateEmitters();
    }

    private void FillLavaVolume(float dt)
    {
        if (!_material.IsValid)
        {
            return;
        }

        _fillTimer -= dt;
        if (_fillTimer > 0f)
        {
            return;
        }

        _fillTimer += Math.Max(0.01f, FillIntervalSeconds);
        int stepX = Math.Clamp(FillStepCells, 4, 64);
        int stepY = Math.Clamp(FillVerticalStepCells, 4, 64);
        int minX = (int)MathF.Round(MinX);
        int maxX = (int)MathF.Round(MinX + Width);
        int startY = (int)MathF.Round(CurrentSurfaceY);
        int maxY = (int)MathF.Ceiling(Math.Max(StartSurfaceY, CurrentSurfaceY) + 34f);
        for (int y = startY; y <= maxY; y += stepY)
        {
            for (int x = minX; x <= maxX; x += stepX)
            {
                if (Context.Cells.IsSolid(x, y))
                {
                    continue;
                }

                Context.Cells.SetCell(x, y, _material);
            }
        }
    }

    private int EstimateActiveAreaCells()
    {
        float maxY = MathF.Max(StartSurfaceY, CurrentSurfaceY) + 34f;
        float height = MathF.Max(0f, maxY - CurrentSurfaceY);
        return (int)MathF.Ceiling(MathF.Max(0f, Width) * height);
    }

    private void UpdateEmitters()
    {
        if (_emitters.Length == 0)
        {
            return;
        }

        float step = _emitters.Length == 1 ? 0f : Width / (_emitters.Length - 1);
        for (int i = 0; i < _emitters.Length; i++)
        {
            MaterialEmitter emitter = _emitters[i];
            emitter.MaterialName = MaterialName;
            emitter.X = MinX + (step * i);
            emitter.Y = CurrentSurfaceY;
            emitter.Radius = Math.Max(1, EmitterRadius);
            emitter.IntervalSeconds = Math.Max(0.001f, EmitterIntervalSeconds);
        }
    }

    private void ResolveMission()
    {
        if (_mission is not null)
        {
            return;
        }

        if (Entity.TryGetComponent(out MissionDirector localMission))
        {
            _mission = localMission;
            BlockedReason = string.Empty;
            return;
        }

        ScriptEntityInspection[] entities = Context.Scene.CaptureInspectionSnapshot();
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptComponentInspection[] components = entities[i].Components;
            for (int j = 0; j < components.Length; j++)
            {
                if (components[j].Behaviour is MissionDirector mission)
                {
                    _mission = mission;
                    BlockedReason = string.Empty;
                    return;
                }
            }
        }

        BlockedReason = "场景中未找到 MissionDirector；熔岩仍会上升，但无法同步任务熔岩线。";
    }

    private sealed class EmitterBuildSystem(RisingHazardDirector director) : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
            _ = context;
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            _ = context;
            _ = dt;
            director.EnsureEmitters();
        }
    }
}
