using PixelEngine.Hosting;
using PixelEngine.Simulation;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 专用窗口探针，预布置反应与温度相变样本并在真实窗口相位中统计结果。
/// </summary>
internal sealed class DemoReactionTemperatureProbe(
    CellGrid grid,
    TemperatureField temperature,
    MaterialTable materials,
    SimulationKernel kernel) : IEnginePhaseDriver
{
    private readonly CellGrid _grid = grid ?? throw new ArgumentNullException(nameof(grid));
    private readonly TemperatureField _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly SimulationKernel _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

    private ushort _empty;
    private ushort _water;
    private ushort _lava;
    private ushort _stone;
    private ushort _steam;
    private ushort _moltenMetal;
    private ushort _metal;
    private ushort _fire;
    private ushort _smoke;
    private ushort _wood;
    private ushort _oil;
    private ushort _acid;
    private ushort _acidGas;
    private ushort _ice;
    private ushort _sand;
    private ushort _glass;

    /// <summary>
    /// 是否已经完成样本布置。
    /// </summary>
    public bool Initialized { get; private set; }

    /// <summary>
    /// 熔岩遇水是否产出石头与蒸汽。
    /// </summary>
    public bool LavaWater { get; private set; }

    /// <summary>
    /// 熔融金属遇水是否凝固并产蒸汽。
    /// </summary>
    public bool MoltenWater { get; private set; }

    /// <summary>
    /// 水灭火是否产出蒸汽或烟。
    /// </summary>
    public bool WaterFire { get; private set; }

    /// <summary>
    /// 火接触木头是否传播为火。
    /// </summary>
    public bool FireWood { get; private set; }

    /// <summary>
    /// 火接触油是否快速燃烧为火。
    /// </summary>
    public bool FireOil { get; private set; }

    /// <summary>
    /// 酸腐蚀可腐蚀材质是否产出酸气或酸扩散。
    /// </summary>
    public bool AcidCorrosion { get; private set; }

    /// <summary>
    /// 蒸汽接触冷材质是否冷凝成水。
    /// </summary>
    public bool SteamCondense { get; private set; }

    /// <summary>
    /// 冰是否融化成水。
    /// </summary>
    public bool IceMelted { get; private set; }

    /// <summary>
    /// 水是否沸腾成蒸汽。
    /// </summary>
    public bool WaterBoiled { get; private set; }

    /// <summary>
    /// 水是否冻结成冰。
    /// </summary>
    public bool WaterFroze { get; private set; }

    /// <summary>
    /// 熔岩是否冷却成石头。
    /// </summary>
    public bool LavaCooled { get; private set; }

    /// <summary>
    /// 金属是否熔化成熔融金属。
    /// </summary>
    public bool MetalMelted { get; private set; }

    /// <summary>
    /// 沙是否烤成玻璃。
    /// </summary>
    public bool SandGlassed { get; private set; }

    /// <summary>
    /// 所有反应样本是否均已观测到目标产物。
    /// </summary>
    public bool ReactionsObserved => LavaWater &&
        MoltenWater &&
        WaterFire &&
        FireWood &&
        FireOil &&
        AcidCorrosion &&
        SteamCondense;

    /// <summary>
    /// 所有温度相变样本是否均已观测到目标产物。
    /// </summary>
    public bool PhaseTransitionsObserved => IceMelted &&
        WaterBoiled &&
        WaterFroze &&
        LavaCooled &&
        MetalMelted &&
        SandGlassed;

    /// <summary>
    /// 当前探针区域关键材质计数摘要。
    /// </summary>
    public string CountSummary => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"water={CountMaterial(24, 8, 367, 224, _water)};lava={CountMaterial(24, 8, 367, 224, _lava)};stone={CountMaterial(24, 8, 367, 224, _stone)};steam={CountMaterial(24, 8, 367, 224, _steam)};fire={CountMaterial(24, 8, 367, 224, _fire)};smoke={CountMaterial(24, 8, 367, 224, _smoke)};wood={CountMaterial(24, 8, 367, 224, _wood)};oil={CountMaterial(24, 8, 367, 224, _oil)};acid={CountMaterial(24, 8, 367, 224, _acid)};acid_gas={CountMaterial(24, 8, 367, 224, _acidGas)};ice={CountMaterial(24, 8, 367, 224, _ice)};metal={CountMaterial(24, 8, 367, 224, _metal)};molten_metal={CountMaterial(24, 8, 367, 224, _moltenMetal)};sand={CountMaterial(24, 8, 367, 224, _sand)};glass={CountMaterial(24, 8, 367, 224, _glass)}");

    /// <summary>
    /// 注册布置与统计相位。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.GameLogicAndScripts, Initialize);
        phases.Register(EnginePhase.BuildRenderBuffer, Capture);
    }

    private void Initialize(EngineTickContext context)
    {
        _ = context;
        if (Initialized)
        {
            return;
        }

        ResolveMaterials();
        ClearArea(24, 24, 360, 280);
        BuildReactionPairs();
        BuildPhaseTransitionBlocks();
        Initialized = true;
    }

    private void Capture(EngineTickContext context)
    {
        _ = context;
        if (!Initialized)
        {
            return;
        }

        LavaWater |= CountAny(32, 0, 81, 112, _stone) && CountAny(32, 0, 81, 112, _steam);
        MoltenWater |= CountAny(88, 0, 137, 112, _metal) && CountAny(88, 0, 137, 112, _steam);
        WaterFire |= CountAny(144, 0, 193, 112, _steam, _smoke);
        FireWood |= CountAtLeast(200, 0, 249, 112, _fire, 8);
        FireOil |= CountAtLeast(256, 0, 305, 112, _fire, 8);
        AcidCorrosion |= CountAny(312, 0, 361, 112, _acidGas) && CountAny(312, 0, 361, 112, _acid);
        SteamCondense |= CountAny(32, 40, 81, 144, _water);

        IceMelted |= CountAny(33, 177, 48, 192, _water);
        WaterBoiled |= CountAny(64, 144, 81, 193, _steam);
        WaterFroze |= CountAny(97, 177, 112, 192, _ice);
        LavaCooled |= CountAny(129, 177, 144, 192, _stone);
        MetalMelted |= CountAny(161, 177, 176, 192, _moltenMetal);
        SandGlassed |= CountAny(193, 177, 208, 192, _glass);
    }

    private void ResolveMaterials()
    {
        _empty = Require("empty");
        _water = Require("water");
        _lava = Require("lava");
        _stone = Require("stone");
        _steam = Require("steam");
        _moltenMetal = Require("molten_metal");
        _metal = Require("metal");
        _fire = Require("fire");
        _smoke = Require("smoke");
        _wood = Require("wood");
        _oil = Require("oil");
        _acid = Require("acid");
        _acidGas = Require("acid_gas");
        _ice = Require("ice");
        _sand = Require("sand");
        _glass = Require("glass");
    }

    private ushort Require(string name)
    {
        return _materials.TryGetId(name, out ushort id)
            ? id
            : throw new InvalidOperationException($"Demo 反应探针缺少材质：{name}。");
    }

    private void BuildReactionPairs()
    {
        FillPairs(32, 32, _lava, _water);
        FillPairs(88, 32, _moltenMetal, _water);
        FillPairs(144, 32, _water, _fire);
        FillPairs(200, 32, _fire, _wood);
        FillPairs(256, 32, _fire, _oil);
        FillPairs(312, 32, _acid, _stone);
        FillPairs(32, 64, _steam, _stone);
    }

    private void BuildPhaseTransitionBlocks()
    {
        FillBlockWithTemperature(32, 176, _ice, 20f);
        FillBlockWithTemperature(64, 176, _water, 140f);
        FillBlockWithTemperature(96, 176, _water, -20f);
        FillBlockWithTemperature(128, 176, _lava, 100f);
        FillBlockWithTemperature(160, 176, _metal, 1_050f);
        FillBlockWithTemperature(192, 176, _sand, 1_000f);
    }

    private void FillPairs(int x, int y, ushort left, ushort right)
    {
        BuildBasin(x, y, 50, 20);
        for (int yy = y + 1; yy < y + 19; yy++)
        {
            for (int xx = x + 1; xx < x + 49; xx += 2)
            {
                WriteProbeCell(xx, yy, left);
                WriteProbeCell(xx + 1, yy, right);
            }
        }
    }

    private void FillBlockWithTemperature(int x, int y, ushort material, float targetTemperature)
    {
        BuildBasin(x, y, 18, 18);
        for (int yy = y + 1; yy < y + 17; yy++)
        {
            for (int xx = x + 1; xx < x + 17; xx++)
            {
                WriteProbeCell(xx, yy, material);
            }
        }

        for (int yy = y + 1; yy < y + 17; yy += 4)
        {
            for (int xx = x + 1; xx < x + 17; xx += 4)
            {
                SetTemperature(xx, yy, targetTemperature);
            }
        }
    }

    private void BuildBasin(int x, int y, int width, int height)
    {
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                bool border = yy == y || yy == y + height - 1 || xx == x || xx == x + width - 1;
                if (border)
                {
                    WriteProbeCell(xx, yy, _glass);
                }
            }
        }
    }

    private void WriteProbeCell(int x, int y, ushort material)
    {
        _kernel.EditCellAtInputPhase(x, y, material, persistentFlags: 0);
    }

    private void SetTemperature(int x, int y, float targetTemperature)
    {
        float current = _temperature.GetTemperature(x, y);
        _temperature.AddHeat(x, y, targetTemperature - current);
    }

    private void ClearArea(int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                WriteProbeCell(x, y, _empty);
            }
        }
    }

    private bool CountAny(int minX, int minY, int maxX, int maxY, params ushort[] materials)
    {
        return CountMaterials(minX, minY, maxX, maxY, materials) > 0;
    }

    private bool CountAtLeast(int minX, int minY, int maxX, int maxY, ushort material, int minimum)
    {
        return CountMaterial(minX, minY, maxX, maxY, material) >= minimum;
    }

    private int CountMaterial(int minX, int minY, int maxX, int maxY, ushort material)
    {
        int count = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (_grid.MaterialAt(x, y) == material)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private int CountMaterials(int minX, int minY, int maxX, int maxY, ReadOnlySpan<ushort> materials)
    {
        int count = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                ushort material = _grid.MaterialAt(x, y);
                for (int i = 0; i < materials.Length; i++)
                {
                    if (material == materials[i])
                    {
                        count++;
                        break;
                    }
                }
            }
        }

        return count;
    }
}
