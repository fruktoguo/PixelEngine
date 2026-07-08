using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 场景在编辑器中的 authoring 行为声明；运行版 Demo 使用根目录下的完整实现。
/// </summary>
public sealed class LevelDirector : Behaviour
{
    public int LevelWidth { get; set; } = 640;
    public int LevelHeight { get; set; } = 360;
    public float PlayerSpawnX { get; set; } = 48f;
    public float PlayerSpawnY { get; set; } = 244f;
    public float GoalX { get; set; } = 570f;
    public float GoalY { get; set; } = 208f;
    public bool BuildScriptEntities { get; set; } = true;
    public bool BuildAmbientSparkEmitters { get; set; }
    public bool BuildAmbientMaterialEmitters { get; set; }
    public bool BuildGoalTrigger { get; set; } = true;
    public bool BuildSpawnHazardProbe { get; set; }
    public bool BuildLoadCounterProbe { get; set; }
    public float CameraZoom { get; set; } = 2f;
}

/// <summary>
/// Demo 任务状态。
/// </summary>
public enum MissionState
{
    Playing,
    Won,
    Lost,
}

/// <summary>
/// Demo 任务导演的编辑器 authoring 声明。
/// </summary>
public sealed class MissionDirector : Behaviour
{
    public int RequiredCrystals { get; set; } = 3;
    public float TimeLimitSeconds { get; set; } = 240f;
    public float InitialLavaSurfaceY { get; set; } = 336f;
    public float LavaRiseCellsPerSecond { get; set; }
}

/// <summary>
/// Demo 上升熔岩危险导演的编辑器 authoring 声明。
/// </summary>
public sealed class RisingHazardDirector : Behaviour
{
    public string MaterialName { get; set; } = "lava";
    public float MinX { get; set; } = 96f;
    public float Width { get; set; } = 448f;
    public float StartSurfaceY { get; set; } = 336f;
    public float TargetSurfaceY { get; set; } = 196f;
    public float RiseSeconds { get; set; } = 180f;
    public int EmitterCount { get; set; } = 12;
    public int EmitterRadius { get; set; } = 4;
    public float EmitterIntervalSeconds { get; set; } = 0.08f;
    public int FillStepCells { get; set; } = 12;
    public int FillVerticalStepCells { get; set; } = 8;
    public float FillIntervalSeconds { get; set; } = 0.12f;
    public float LossSurfaceY { get; set; } = 210f;
}

/// <summary>
/// Demo 撤离触发器的编辑器 authoring 声明。
/// </summary>
public sealed class ExtractionTrigger : Behaviour
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 34f;
    public float Height { get; set; } = 54f;
    public string ExtractionAudioCue { get; set; } = "goal_reached.wav";
    public string CelebrationMaterialName { get; set; } = "sand";
    public int CelebrationParticleCount { get; set; } = 42;
    public uint LightColorBgra { get; set; } = 0xFF_80_F0_FF;
}

/// <summary>
/// Demo 目标水晶的编辑器 authoring 声明。
/// </summary>
public sealed class ObjectiveCrystal : Behaviour
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; } = 3;
    public string MaterialName { get; set; } = "crystal";
    public bool PlaceOnStart { get; set; } = true;
}
