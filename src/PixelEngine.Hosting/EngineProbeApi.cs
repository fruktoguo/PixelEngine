using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 提供给 Demo 窗口探针的受控诊断 API，避免 Demo 直接依赖 Simulation 内部类型。
/// </summary>
public sealed class EngineProbeApi
{
    private readonly CellGrid _grid;
    private readonly SimulationKernel _kernel;
    private readonly TemperatureField _temperature;
    private readonly MaterialTable _materials;
    private readonly ParticleSystem _particles;

    internal EngineProbeApi(
        CellGrid grid,
        SimulationKernel kernel,
        TemperatureField temperature,
        MaterialTable materials,
        ParticleSystem particles)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    }

    /// <summary>
    /// 当前活跃自由粒子数量。
    /// </summary>
    public int ActiveParticles => _particles.ActiveCount;

    /// <summary>
    /// 按材质与颜色变体统计当前活跃自由粒子数量；只读扫描活跃前缀，不分配。
    /// </summary>
    /// <param name="material">运行时材质 id。</param>
    /// <param name="colorVariant">粒子颜色变体。</param>
    /// <returns>匹配条件的活跃自由粒子数量。</returns>
    public int CountActiveParticles(ushort material, byte colorVariant)
    {
        ReadOnlySpan<Particle> active = _particles.ActiveReadOnly;
        int count = 0;
        for (int i = 0; i < active.Length; i++)
        {
            Particle particle = active[i];
            if (particle.Material == material && particle.ColorVariant == colorVariant)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 按稳定材质名解析运行时材质 id。
    /// </summary>
    /// <param name="name">材质稳定名称。</param>
    /// <param name="id">解析成功时返回运行时材质 id。</param>
    /// <returns>若材质存在则返回 true。</returns>
    public bool TryResolveMaterial(string name, out ushort id)
    {
        return _materials.TryGetId(name, out id);
    }

    /// <summary>
    /// 按稳定材质名解析运行时材质 id；缺失时抛出明确异常。
    /// </summary>
    /// <param name="name">材质稳定名称。</param>
    /// <returns>运行时材质 id。</returns>
    public ushort ResolveMaterial(string name)
    {
        return TryResolveMaterial(name, out ushort id)
            ? id
            : throw new InvalidOperationException($"缺少材质：{name}。");
    }

    /// <summary>
    /// 读取指定世界坐标的材质 id。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <returns>当前材质 id。</returns>
    public ushort MaterialAt(int x, int y)
    {
        return _grid.GetMaterial(x, y);
    }

    /// <summary>
    /// 在输入相位写入 cell 并唤醒对应 dirty 区域。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="material">运行时材质 id。</param>
    public void EditCellAtInputPhase(int x, int y, ushort material)
    {
        _kernel.EditCellAtInputPhase(x, y, material, persistentFlags: 0);
    }

    /// <summary>
    /// 把粗温度场指定坐标调整到目标温度。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="targetTemperature">目标摄氏温度。</param>
    public void SetTemperature(int x, int y, float targetTemperature)
    {
        float current = _temperature.GetTemperature(x, y);
        _temperature.AddHeat(x, y, targetTemperature - current);
    }

    /// <summary>
    /// 确保自由粒子池容量至少达到指定活跃粒子数。
    /// </summary>
    /// <param name="maxActiveCount">最小活跃粒子容量。</param>
    public void EnsureParticleCapacity(int maxActiveCount)
    {
        if (_particles.Settings.MaxActiveCount < maxActiveCount)
        {
            _particles.ApplySettings(_particles.Settings with { MaxActiveCount = maxActiveCount });
        }
    }

    /// <summary>
    /// 清空当前所有自由粒子。
    /// </summary>
    public void ClearParticles()
    {
        _particles.Clear();
    }

    /// <summary>
    /// 尝试生成一个自由粒子。
    /// </summary>
    /// <param name="x">起始 X 坐标。</param>
    /// <param name="y">起始 Y 坐标。</param>
    /// <param name="velocityX">初始 X 速度。</param>
    /// <param name="velocityY">初始 Y 速度。</param>
    /// <param name="material">运行时材质 id。</param>
    /// <param name="colorVariant">颜色变体。</param>
    /// <param name="life">粒子 lifetime。</param>
    /// <returns>若粒子已生成则返回 true。</returns>
    public bool TrySpawnParticle(
        float x,
        float y,
        float velocityX,
        float velocityY,
        ushort material,
        byte colorVariant,
        ushort life)
    {
        ParticleSpawn spawn = new(x, y, velocityX, velocityY, material, colorVariant, (byte)Math.Min(byte.MaxValue, life));
        return _particles.TrySpawn(in spawn);
    }
}
